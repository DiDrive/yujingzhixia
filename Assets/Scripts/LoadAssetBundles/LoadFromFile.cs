using System.IO;
using System.Collections.Generic;
using System;
using UnityEngine;

public class LoadFromFile : MonoBehaviour
{
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

    // AssetBundle 输出目录名，最终会拼成：Application.dataPath/bundleFolderName
    [SerializeField] private string bundleFolderName = "AssetBundles";
    // 通用加载清单：以后新增包只需在这里加一项，不需要再改加载流程代码。
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

    void Start()
    {
        // 绝对路径根目录，例如：D:/project/Assets/AssetBundles
        var bundleRoot = Path.Combine(Application.dataPath, bundleFolderName);
        // 记录本轮已加载的 bundle 实例，最后统一 Unload(false) 释放文件句柄。
        var loadedBundles = new List<AssetBundle>();
        // 以路径去重缓存，避免同一个 bundle 被重复加载（依赖包常见重复引用）。
        var loadedBundleByPath = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // 尝试加载根清单（AssetBundleManifest），用于查询依赖关系。
            // 如果没有清单，也不阻塞主流程，只是无法自动解析依赖。
            TryLoadManifest(bundleRoot, loadedBundles, loadedBundleByPath, out var manifest);

            // 按配置逐条执行加载与实例化。
            for (var i = 0; i < prefabLoadRequests.Count; i++)
            {
                TryInstantiateRequest(prefabLoadRequests[i], bundleRoot, manifest, loadedBundles, loadedBundleByPath);
            }
        }
        finally
        {
            // 无论中途成功/失败，都执行清理，避免文件句柄长期占用。
            UnloadAllBundles(loadedBundles);
        }
    }

    /// <summary>
    /// 尝试实例化指定加载请求。
    /// </summary>
    private static void TryInstantiateRequest(
        PrefabLoadRequest request,
        string bundleRoot,
        AssetBundleManifest manifest,
        List<AssetBundle> loadedBundles,
        Dictionary<string, AssetBundle> loadedBundleByPath)
    {
        // 基础参数校验：非法配置直接跳过，不中断其他请求。
        if (request == null) return;
        if (string.IsNullOrWhiteSpace(request.bundleName)) return;
        if (string.IsNullOrWhiteSpace(request.assetName)) return;

        // 先加载目标包（并在有 manifest 时自动先加载依赖包）。
        if (!TryLoadBundleWithDependencies(
                bundleRoot,
                request.bundleName,
                manifest,
                loadedBundles,
                loadedBundleByPath,
                out var bundle))
        {
            Debug.LogError($"Failed to load AssetBundle: {request.bundleName}");
            return;
        }

        // 再从目标包按名字取资源。拿不到时输出包内资源列表，方便快速排错。
        var prefab = bundle.LoadAsset<GameObject>(request.assetName);
        if (prefab == null)
        {
            var bundleAssets = string.Join(", ", bundle.GetAllAssetNames());
            Debug.LogError($"Failed to load asset: {request.assetName}. Bundle: {request.bundleName}. Bundle assets: {bundleAssets}");
            return;
        }

        // 生成实例并设置位姿。注意这里操作的是实例，不是 prefab 资源本体。
        var instance = Instantiate(prefab);
        instance.transform.position = request.position;
        instance.transform.eulerAngles = request.eulerAngles;
    }


    /// <summary>
    /// 尝试加载根清单（AssetBundleManifest），用于查询依赖关系。
    /// </summary>
    private static bool TryLoadManifest(
        string bundleRoot,
        List<AssetBundle> loadedBundles,
        Dictionary<string, AssetBundle> loadedBundleByPath,
        out AssetBundleManifest manifest)
    {
        manifest = null;
        // BuildPipeline.BuildAssetBundles 默认会生成一个“总清单包”，名称通常为输出目录同名文件。
        var manifestBundlePath = Path.Combine(bundleRoot, "AssetBundles");
        if (!TryLoadBundleFromPath(manifestBundlePath, loadedBundles, loadedBundleByPath, out var manifestBundle))
        {
            return false;
        }

        // 清单资源名固定为 "AssetBundleManifest"。
        manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        return manifest != null;
    }

    /// <summary>
    /// 尝试加载指定包（包括依赖包）。
    /// </summary>
    private static bool TryLoadBundleWithDependencies(
        string bundleRoot,
        string bundleName,
        AssetBundleManifest manifest,
        List<AssetBundle> loadedBundles,
        Dictionary<string, AssetBundle> loadedBundleByPath,
        out AssetBundle bundle)
    {
        // 解析真实文件名（支持传入不带后缀时自动尝试 .unity3d）。
        if (!TryResolveBundle(bundleRoot, bundleName, out var resolvedBundleName, out var resolvedBundlePath))
        {
            bundle = null;
            return false;
        }

        // 若有清单，先把依赖包全部加载进来，避免材质/贴图等跨包引用丢失。
        if (manifest != null)
        {
            var dependencies = manifest.GetAllDependencies(resolvedBundleName);
            for (var i = 0; i < dependencies.Length; i++)
            {
                var dependencyPath = Path.Combine(bundleRoot, dependencies[i]);
                TryLoadBundleFromPath(dependencyPath, loadedBundles, loadedBundleByPath, out _);
            }
        }

        // 最后加载目标包本体。
        return TryLoadBundleFromPath(resolvedBundlePath, loadedBundles, loadedBundleByPath, out bundle);
    }

    /// <summary>
    /// 尝试解析指定包名（支持传入不带后缀时自动尝试 .unity3d）。
    /// </summary>
    private static bool TryResolveBundle(
        string bundleRoot,
        string bundleName,
        out string resolvedBundleName,
        out string resolvedBundlePath)
    {
        // 先按原始名字尝试。
        resolvedBundleName = bundleName;
        resolvedBundlePath = Path.Combine(bundleRoot, bundleName);
        if (File.Exists(resolvedBundlePath)) return true;

        // 如果没写后缀，则自动补 .unity3d 再尝试。
        if (!bundleName.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
        {
            resolvedBundleName = bundleName + ".unity3d";
            resolvedBundlePath = Path.Combine(bundleRoot, resolvedBundleName);
            if (File.Exists(resolvedBundlePath)) return true;
        }

        return false;
    }

    /// <summary>
    /// 尝试从指定路径加载 AssetBundle。
    /// </summary>
    private static bool TryLoadBundleFromPath(
        string bundlePath,
        List<AssetBundle> loadedBundles,
        Dictionary<string, AssetBundle> loadedBundleByPath,
        out AssetBundle bundle)
    {
        // 已加载过同一路径则直接复用，避免重复读盘。
        if (loadedBundleByPath.TryGetValue(bundlePath, out bundle))
        {
            return true;
        }

        // 文件不存在直接失败。
        if (!File.Exists(bundlePath))
        {
            bundle = null;
            return false;
        }

        // 同步加载 bundle 文件。
        bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null)
        {
            return false;
        }

        // 登记到缓存与回收列表。
        loadedBundleByPath[bundlePath] = bundle;
        loadedBundles.Add(bundle);
        return true;
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
