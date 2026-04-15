using System.IO;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.Networking;

public class LoadFromFile : MonoBehaviour
{
    private enum BundleLoadSource
    {
        LocalFile,
        RemoteUrl
    }

    // 每条加载请求对应“从哪个包加载哪个资源，并在场景中以什么位姿生成”
    // 配成 [Serializable] 后可在 Inspector 里直接新增/编辑，便于后续扩展新包。
    [Serializable]
    private class PrefabLoadRequest
    {
        // AssetBundle 名称。可写 "xxx" 或 "xxx.unity3d"。
        public string bundleName;
        // 包内资源名（LoadAsset 使用的名字，大小写敏感）。
        public string assetName;
        // 实例化后的世界坐标位置。
        public Vector3 position;
        // 实例化后的欧拉角旋转（单位：度）。
        public Vector3 eulerAngles;
    }

    [Tooltip("AssetBundle 输出目录名，最终会拼成：Application.dataPath/bundleFolderName")]
    [SerializeField] private string bundleFolderName = "AssetBundles";

    [Tooltip("资源来源：本地文件或服务器 URL。")]
    [SerializeField] private BundleLoadSource loadSource = BundleLoadSource.LocalFile;

    [Tooltip("服务器根地址（RemoteUrl 模式使用），例如：http://127.0.0.1:8080/AssetBundles")]
    [SerializeField] private string remoteBaseUrl = "http://127.0.0.1:8080/AssetBundles";
    
    [Tooltip("将包内资源名汇总到 Inspector 文本框，方便查看和复制。")]
    [SerializeField] private bool showAssetNamesInInspector = true;
    [TextArea(6, 20)]
    [SerializeField] private string bundleAssetReport;

    [Tooltip("通用加载清单：以后新增包只需在这里加一项，不需要再改加载流程代码。")]
    [SerializeField] private List<PrefabLoadRequest> prefabLoadRequests = new List<PrefabLoadRequest>
    {
        new PrefabLoadRequest
        {
            bundleName = "cube",
            assetName = "Cube",
            position = new Vector3(0f, 30f, 0f),
            eulerAngles = new Vector3(0f, 60f, 0f)
        },
        new PrefabLoadRequest
        {
            bundleName = "capsule.unity3d",
            assetName = "Capsule",
            position = Vector3.zero,
            eulerAngles = Vector3.zero
        }
    };

    private void Start()
    {
        bundleAssetReport = string.Empty;
        StartCoroutine(LoadAndInstantiateAll());
    }

    private System.Collections.IEnumerator LoadAndInstantiateAll()
    {
        // 绝对路径根目录，例如：D:/project/Assets/AssetBundles
        var bundleRoot = Path.Combine(Application.dataPath, bundleFolderName);
        // 记录本轮已加载的 bundle 实例，最后统一 Unload(false) 释放文件句柄。
        var loadedBundles = new List<AssetBundle>();
        // 以 key 去重缓存（本地用路径，远端用 URL），避免同一个 bundle 被重复加载。
        var loadedBundleByKey = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // 尝试加载根清单（AssetBundleManifest），用于查询依赖关系。
            // 如果没有清单，也不阻塞主流程，只是无法自动解析依赖。
            yield return TryLoadManifest(bundleRoot, loadedBundles, loadedBundleByKey, manifestResult =>
            {
                cachedManifest = manifestResult;
            });

            // 按配置逐条执行加载与实例化。
            for (var i = 0; i < prefabLoadRequests.Count; i++)
            {
                yield return TryInstantiateRequest(prefabLoadRequests[i], bundleRoot, cachedManifest, loadedBundles, loadedBundleByKey);
            }
        }
        finally
        {
            // 无论中途成功/失败，都执行清理，避免文件句柄长期占用。
            UnloadAllBundles(loadedBundles);
        }
    }

    private AssetBundleManifest cachedManifest;

    /// <summary>
    /// 尝试实例化指定加载请求。
    /// </summary>
    private System.Collections.IEnumerator TryInstantiateRequest(
        PrefabLoadRequest request,
        string bundleRoot,
        AssetBundleManifest manifest,
        List<AssetBundle> loadedBundles,
        Dictionary<string, AssetBundle> loadedBundleByKey)
    {
        // 基础参数校验：非法配置直接跳过，不中断其他请求。
        if (request == null) yield break;
        if (string.IsNullOrWhiteSpace(request.bundleName)) yield break;
        if (string.IsNullOrWhiteSpace(request.assetName)) yield break;

        // 先加载目标包（并在有 manifest 时自动先加载依赖包）。
        AssetBundle bundle = null;
        yield return TryLoadBundleWithDependencies(
                bundleRoot,
                request.bundleName,
                manifest,
                loadedBundles,
                loadedBundleByKey,
                loaded =>
                {
                    bundle = loaded;
                });
        if (bundle == null)
        {
            Debug.LogError($"Failed to load AssetBundle: {request.bundleName}");
            yield break;
        }

        // 再从目标包按名字取资源。拿不到时输出包内资源列表，方便快速排错。
        var prefab = bundle.LoadAsset<GameObject>(request.assetName);
        if (prefab == null)
        {
            var bundleAssets = string.Join(", ", bundle.GetAllAssetNames());
            Debug.LogError($"Failed to load asset: {request.assetName}. Bundle: {request.bundleName}. Bundle assets: {bundleAssets}");
            yield break;
        }

        // 生成实例并设置位姿。注意这里操作的是实例，不是 prefab 资源本体。
        var instance = Instantiate(prefab);
        instance.transform.position = request.position;
        instance.transform.eulerAngles = request.eulerAngles;
    }


    /// <summary>
    /// 尝试加载根清单（AssetBundleManifest），用于查询依赖关系。
    /// </summary>
    private System.Collections.IEnumerator TryLoadManifest(
        string bundleRoot,
        List<AssetBundle> loadedBundles,
        Dictionary<string, AssetBundle> loadedBundleByKey,
        Action<AssetBundleManifest> onDone)
    {
        onDone?.Invoke(null);
        // BuildPipeline.BuildAssetBundles 默认会生成一个“总清单包”，名称通常为输出目录同名文件。
        var manifestBundleRef = ResolveBundleRef(bundleRoot, "AssetBundles");
        AssetBundle manifestBundle = null;
        yield return TryLoadBundleByRef(manifestBundleRef, loadedBundles, loadedBundleByKey, loaded =>
        {
            manifestBundle = loaded;
        });
        if (manifestBundle == null) yield break;

        // 清单资源名固定为 "AssetBundleManifest"。
        var manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        onDone?.Invoke(manifest);
    }

    /// <summary>
    /// 尝试加载指定包（包括依赖包）。
    /// </summary>
    private System.Collections.IEnumerator TryLoadBundleWithDependencies(
        string bundleRoot,
        string bundleName,
        AssetBundleManifest manifest,
        List<AssetBundle> loadedBundles,
        Dictionary<string, AssetBundle> loadedBundleByKey,
        Action<AssetBundle> onDone)
    {
        onDone?.Invoke(null);
        // 解析真实文件名（支持传入不带后缀时自动尝试 .unity3d）。
        if (!TryResolveBundleRef(bundleRoot, bundleName, out var resolvedBundleName, out var resolvedBundleRef))
        {
            yield break;
        }

        // 若有清单，先把依赖包全部加载进来，避免材质/贴图等跨包引用丢失。
        if (manifest != null)
        {
            var dependencies = manifest.GetAllDependencies(resolvedBundleName);
            for (var i = 0; i < dependencies.Length; i++)
            {
                var dependencyRef = ResolveBundleRef(bundleRoot, dependencies[i]);
                var dependencyLoaded = false;
                yield return TryLoadBundleByRef(dependencyRef, loadedBundles, loadedBundleByKey, _ =>
                {
                    dependencyLoaded = true;
                });
                if (!dependencyLoaded)
                {
                    Debug.LogWarning($"Dependency bundle load failed: {dependencies[i]} ({dependencyRef})");
                }
            }
        }

        // 最后加载目标包本体。
        AssetBundle bundle = null;
        yield return TryLoadBundleByRef(resolvedBundleRef, loadedBundles, loadedBundleByKey, loaded =>
        {
            bundle = loaded;
        });
        onDone?.Invoke(bundle);
    }

    /// <summary>
    /// 尝试解析指定包名（支持传入不带后缀时自动尝试 .unity3d）。
    /// </summary>
    private bool TryResolveBundleRef(
        string bundleRoot,
        string bundleName,
        out string resolvedBundleName,
        out string resolvedBundleRef)
    {
        if (string.IsNullOrWhiteSpace(bundleName))
        {
            resolvedBundleName = null;
            resolvedBundleRef = null;
            return false;
        }

        // 远端模式无法本地探测文件是否存在，直接按配置拼 URL。
        if (loadSource == BundleLoadSource.RemoteUrl)
        {
            resolvedBundleName = bundleName;
            resolvedBundleRef = ResolveBundleRef(bundleRoot, bundleName);
            return true;
        }

        // 先按原始名字尝试。
        if (TryResolveLocalBundlePath(bundleRoot, bundleName, out var localPath))
        {
            resolvedBundleName = ToManifestBundleName(bundleRoot, localPath);
            resolvedBundleRef = localPath;
            return true;
        }

        // 如果没写后缀，则自动补 .unity3d 再尝试。
        if (!bundleName.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
        {
            var withExtension = bundleName + ".unity3d";
            if (TryResolveLocalBundlePath(bundleRoot, withExtension, out var localPathWithExtension))
            {
                resolvedBundleName = ToManifestBundleName(bundleRoot, localPathWithExtension);
                resolvedBundleRef = localPathWithExtension;
                return true;
            }
        }

        resolvedBundleRef = null;
        resolvedBundleName = null;
        return false;
    }

    /// <summary>
    /// 尝试从指定来源（本地路径/远端URL）加载 AssetBundle。
    /// </summary>
    private System.Collections.IEnumerator TryLoadBundleByRef(
        string bundleRef,
        List<AssetBundle> loadedBundles,
        Dictionary<string, AssetBundle> loadedBundleByKey,
        Action<AssetBundle> onDone)
    {
        onDone?.Invoke(null);
        // 已加载过同一路径则直接复用，避免重复读盘。
        if (loadedBundleByKey.TryGetValue(bundleRef, out var cachedBundle))
        {
            onDone?.Invoke(cachedBundle);
            yield break;
        }

        if (!CanAccessBundle(bundleRef))
        {
            yield break;
        }

        AssetBundle bundle = null;
        if (loadSource == BundleLoadSource.LocalFile)
        {
            bundle = AssetBundle.LoadFromFile(bundleRef);
        }
        else
        {
            using (var request = UnityWebRequestAssetBundle.GetAssetBundle(bundleRef))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Failed to download AssetBundle: {bundleRef}. Error: {request.error}");
                    yield break;
                }

                bundle = DownloadHandlerAssetBundle.GetContent(request);
            }
        }
        if (bundle == null) yield break;

        // 登记到缓存与回收列表。
        loadedBundleByKey[bundleRef] = bundle;
        loadedBundles.Add(bundle);
        LogBundleAssetNames(bundleRef, bundle);
        onDone?.Invoke(bundle);
    }

    private string ResolveBundleRef(string bundleRoot, string bundleName)
    {
        if (loadSource == BundleLoadSource.LocalFile)
        {
            return Path.Combine(bundleRoot, bundleName);
        }

        var baseUrl = remoteBaseUrl?.TrimEnd('/');
        return $"{baseUrl}/{bundleName}";
    }

    private bool CanAccessBundle(string bundleRef)
    {
        if (string.IsNullOrWhiteSpace(bundleRef)) return false;
        if (loadSource == BundleLoadSource.LocalFile) return File.Exists(bundleRef);
        return Uri.IsWellFormedUriString(bundleRef, UriKind.Absolute);
    }

    private static bool TryResolveLocalBundlePath(string bundleRoot, string bundleName, out string bundlePath)
    {
        bundlePath = Path.Combine(bundleRoot, bundleName);
        if (File.Exists(bundlePath)) return true;

        var fileName = Path.GetFileName(bundleName);
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var matches = Directory.GetFiles(bundleRoot, fileName, SearchOption.AllDirectories);
        if (matches.Length == 0) return false;

        if (matches.Length > 1)
        {
            Debug.LogWarning($"Found multiple bundles named '{fileName}', use relative path in bundleName for precision. Using: {matches[0]}");
        }

        bundlePath = matches[0];
        return true;
    }

    private static string ToManifestBundleName(string bundleRoot, string localBundlePath)
    {
        var root = bundleRoot.Replace('\\', '/').TrimEnd('/');
        var full = localBundlePath.Replace('\\', '/');
        if (!full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(localBundlePath);
        }
        return full.Substring(root.Length + 1);
    }

    private void LogBundleAssetNames(string bundleRef, AssetBundle bundle)
    {
        if (bundle == null) return;
        var names = bundle.GetAllAssetNames();
        var text = names.Length == 0
            ? "  - (no assets)"
            : "  - " + string.Join("\n  - ", names);
        var report = $"[AssetBundle]\nBundle: {bundleRef}\nAssets:\n{text}";

        if (showAssetNamesInInspector)
        {
            if (!string.IsNullOrEmpty(bundleAssetReport))
            {
                bundleAssetReport += "\n\n----------------\n\n";
            }
            bundleAssetReport += report;
        }

    }

    /// <summary>
    /// 卸载所有已加载的 AssetBundle。
    /// </summary>
    private static void UnloadAllBundles(List<AssetBundle> loadedBundles)
    {
        
        // false：仅卸载 bundle 文件句柄，已加载到内存/场景中的资源保持可用。
        for (var i = 0; i < loadedBundles.Count; i++)
        {
            loadedBundles[i].Unload(false);
        }
    }

    
}
