using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;


public class MyYooAsset : MonoBehaviour
{
    public EPlayMode playMode = EPlayMode.HostPlayMode; //运行模式：联网模式
    public string packageName = "DefaultPackage";  
    public string packageVersion = "";  //资源版本号
    public ResourcePackage package;

    //网络相关
    //默认主机服务器地址
    public string defaultHostServer = "http://127.0.0.1/CDN/PC/v1.0";
    //备用主机服务器地址
    public string fallbackHostServer = "http://127.0.0.1/CDN/PC/v1.0";

    //下载相关
    public int downloadMaxNum = 10; //最大下载任务数
    public int filedRetryNum = 3; //失败重试次数
    public ResourceDownloaderOperation downloader;//下载器操作对象

    private AssetHandle _testSpriteHandle;//测试Sprite加载句柄

    [SerializeField]
    private bool clearDownloadCacheForTest = false;//是否在测试时清理下载缓存

    [SerializeField]
    private LoadDll hotUpdateDllLoader;//热更新DLL加载器
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }
    // Start is called before the first frame update
    IEnumerator Start()
    {
        
        //初始化YooAsset
        YooAssets.Initialize();
        YooAssets.TryGetPackage(packageName,out package);
        if (package == null){
            package = YooAssets.CreatePackage(packageName);
        }

        //设置远程服务
        IRemoteService remoteService =new RemoteService(defaultHostServer,fallbackHostServer);

        var createParameters = new HostPlayModeOptions();
        
        //设置默认文件系统参数（内置资源，挂解密器）
        var builtinFs = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
        builtinFs.AddParameter(EFileSystemParameter.AssetBundleDecryptor, new BundleEncryption.Decryptor());
        createParameters.BuiltinFileSystemParameters = builtinFs;
        
        //设置缓存文件系统参数（沙盒资源，挂解密器）
        var cacheFs = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
        cacheFs.AddParameter(EFileSystemParameter.AssetBundleDecryptor, new BundleEncryption.Decryptor());
        createParameters.CacheFileSystemParameters = cacheFs;
        
        //执行初始化操作
        InitializePackageOperation initializeOperation = package.InitializePackageAsync(createParameters);
        yield return initializeOperation;

        if(initializeOperation.Status != EOperationStatus.Succeeded){
            Debug.LogWarning(initializeOperation.Error);
            yield break;
        }else{
            Debug.Log("初始化成功........");
        }

        //获取资源版本
        Debug.Log("开始请求远程资源版本...");
        var versionOptions = new RequestPackageVersionOptions();
        var operation = package.RequestPackageVersionAsync(versionOptions);
        yield return operation;
        Debug.Log($"远程版本请求结束，状态：{operation.Status}");
        if(operation.Status != EOperationStatus.Succeeded){
            Debug.LogError($"获取远程版本失败：{operation.Error}");
            yield break;
        }else{
            Debug.Log($"获取版本:{operation.PackageVersion}");
            packageVersion = operation.PackageVersion;
        }

        //获取文件清单
        var options = new LoadPackageManifestOptions(packageVersion, 60);//加载文件清单选项,超时时间60秒
        var loadOperationManifest = package.LoadPackageManifestAsync(options);//加载文件清单操作
        yield return loadOperationManifest;
        if(loadOperationManifest.Status != EOperationStatus.Succeeded){
            Debug.LogWarning(loadOperationManifest.Error);
            yield break;
        }else{
            Debug.Log($"获取文件清单成功...........");
        }
        // 测试用：清除以前下载到沙盒里的Bundle缓存
        // 必须放在加载Manifest之后、创建下载器之前
        if (clearDownloadCacheForTest)
        {
            Debug.Log("测试模式：开始清除YooAsset下载缓存...");

            var clearAllOptions =
                new ClearCacheOptions(ClearCacheMethods.ClearAllBundleFiles);

            var clearAllOperation = package.ClearCacheAsync(clearAllOptions);
            yield return clearAllOperation;

            if (clearAllOperation.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"清除下载缓存失败：{clearAllOperation.Error}");
                yield break;
            }

            Debug.Log("测试模式：YooAsset下载缓存清除完成");
        }

        //创建下载器
        var optionsDownloader = new ResourceDownloaderOptions(downloadMaxNum, filedRetryNum);
        downloader =  package.CreateResourceDownloader(optionsDownloader);
        if(downloader.TotalDownloadCount==0){
            Debug.LogWarning("没有文件需要下载");
            yield return UpdateDone();
            yield break;
        }else{
            int downloadCount = downloader.TotalDownloadCount;
            long bytes =downloader.TotalDownloadBytes;
            Debug.Log($"需要下载{downloadCount}个文件,大小是{bytes/1024/1024}MB");
        }

        //开始下载
        downloader.DownloadProgressChanged += ProgressCallback;
        downloader.DownloadError += DownloadErrorCallback;
        downloader.StartDownload();
        yield return downloader;
        if(downloader.Status != EOperationStatus.Succeeded){
            Debug.LogWarning(downloader.Error);
            yield break;
        }else{
            Debug.Log($"下载完成...........");
            //UpdateDone();
        }


        //清理文件
        var clearoptions = new ClearCacheOptions(ClearCacheMethods.ClearUnusedBundleFiles);
        var clearOperation = package.ClearCacheAsync(clearoptions);
        clearOperation.Completed += ClearCacheOperation_Completed;

    }

    /// <summary>
    /// 下载进度回调
    /// </summary>
    private void ProgressCallback(DownloadProgressChangedEventArgs args)
    {

        Debug.Log($"下载进度:{args.CurrentDownloadCount}/{args.TotalDownloadCount},"+
        $"{args.CurrentDownloadBytes/1024f/1024f:F2}/{args.TotalDownloadBytes/1024f/1024f:F2}MB,"+
        $"进度{args.Progress:P1}");
    
    }
    /// <summary>
    /// 下载错误回调
    /// </summary>
    private void DownloadErrorCallback(DownloadErrorEventArgs args)
    {
        Debug.LogError($"下载错误:{args.ErrorInfo}");
    }

    /// <summary>
    /// 热更新完成
    /// </summary>
private IEnumerator UpdateDone()
{
    // 此时Package初始化、Manifest加载和资源下载流程都已完成。
    // 在这里加载HotUpdate.dll，避免LoadDll.Start抢在YooAsset初始化之前执行。
    if (hotUpdateDllLoader == null)
        hotUpdateDllLoader = FindObjectOfType<LoadDll>();

    if (hotUpdateDllLoader == null)
    {
        Debug.LogError("没有找到LoadDll组件，无法加载热更新DLL");
        yield break;
    }

    yield return hotUpdateDllLoader.LoadHotUpdateDll(package);

    if (!hotUpdateDllLoader.IsLoadSucceeded)
    {
        Debug.LogError("热更新DLL加载或Hello.Run执行失败，停止后续流程");
        yield break;
    }

    // 使用构建报告里的资源地址
    _testSpriteHandle = package.LoadAssetAsync<Sprite>("bandage");

    // 等待异步加载完成
    yield return _testSpriteHandle;

    if (_testSpriteHandle.Status != EOperationStatus.Succeeded)
    {
        Debug.LogError(
            $"Sprite 加载失败：{_testSpriteHandle.Error}");
        yield break;
    }

    Sprite sprite = _testSpriteHandle.AssetObject as Sprite;

    if (sprite == null)
    {
        Debug.LogError("资源操作成功，但 AssetObject 转换为 Sprite 失败");
        yield break;
    }

    GameObject test = new GameObject("YooAssetSpriteTest");
    SpriteRenderer renderer = test.AddComponent<SpriteRenderer>();
    renderer.sprite = sprite;


    Debug.Log($"Sprite 加载成功：{sprite.name}");

    //LoadScene("StartScene");
    LoadSubObject("Arrow");
    UnloadUnusedAssets();
}

    /// <summary>
    /// 清理文件完成回调
    /// </summary>
    private void ClearCacheOperation_Completed(AsyncOperationBase obj)
    {
        StartCoroutine(UpdateDone());
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    /// <summary>
    /// 场景加载
    /// </summary>
    public async void LoadScene(string scenename, UnityEngine.SceneManagement.LoadSceneMode sceneMode = UnityEngine.SceneManagement.LoadSceneMode.Single)
    {
        bool suspendLoad = false;//是否激活场景
        var physicsMode =LocalPhysicsMode.None;
        if (string.IsNullOrEmpty(scenename))
        {
            Debug.LogError("场景名称不能为空");
            return;
        }
        SceneHandle handle = package.LoadSceneAsync(scenename, sceneMode,physicsMode, suspendLoad);
        await handle;
        Debug.Log($"场景加载完成:{handle.SceneName}");
    }


    /// <summary>
    /// Prefab加载,并实例化
    /// </summary>
    public async void LoadPrefab(string prefabname)
    {
        if (string.IsNullOrEmpty(prefabname))
        {
            Debug.LogError("prefab名称不能为空");
            return;
        }
        AssetHandle handle = package.LoadAssetAsync<GameObject>(prefabname);
        await handle;
        GameObject obj = handle.InstantiateSync();
        Debug.Log($"prefab加载完成:{obj.name}");
    }

    //图片子对象加载
    public async void LoadSubObject(string subObjectName)
    {
        if (string.IsNullOrEmpty(subObjectName))
        {
            Debug.LogError("子对象名称不能为空");
            return;
        }
        SubAssetsHandle handle = package.LoadSubAssetsAsync<Sprite>(subObjectName);
        await handle;
        var sprite =handle.GetSubAssetObject<Sprite>("Arrow_0");
        new GameObject().AddComponent<SpriteRenderer>().sprite = sprite;
        Debug.Log($"子对象加载完成:{sprite.name}");
    }

    /// <summary>
    /// 卸载未使用资源
    /// </summary>
    public async void UnloadUnusedAssets()
    {
       var unloadOperation = package.UnloadUnusedAssetsAsync();
       await unloadOperation;
       Debug.Log($"卸载未使用资源完成");
    }

    /// <summary>
    /// 卸载所有资源,包括正在使用的资源
    /// 注意:卸载所有资源后,所有资源将无法再使用
    /// </summary>
    public async void UnloadAllAssets()
    {
        var unloadOperation = package.UnloadAllAssetsAsync();
        await unloadOperation;
        Debug.Log($"卸载所有资源完成");
    }

    /// <summary>
    /// 卸载资源包中指定资源
    /// </summary>
    public void UnloadAsset(string assetName)
    {
        package.TryUnloadUnusedAsset(assetName);
        Debug.Log($"卸载资源完成:{assetName}");
    }

}

public class RemoteService : IRemoteService
{
    private readonly string _defaultHostServer;
    private readonly string _fallbackHostServer;
    public RemoteService(string defaultHostServer, string fallbackHostServer)
    {
        _defaultHostServer = defaultHostServer;
        _fallbackHostServer = fallbackHostServer;
    }
    public IReadOnlyList<string> GetRemoteUrls(string fileName)
    {
        List<string> result = new List<string>();
        result.Add($"{_defaultHostServer}/{fileName}");
        result.Add($"{_fallbackHostServer}/{fileName}");
        return result;
    }

    
}
