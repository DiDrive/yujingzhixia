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

    [SerializeField]
    private bool clearDownloadCacheForTest = false;//是否在测试时清理下载缓存

    [SerializeField]
    private LoadDll hotUpdateDllLoader;//热更新DLL加载器

    [SerializeField]
    private BootUpdateUI updateUI;//BootScene中可编辑的更新界面

    private bool isUpdating;
    private bool yooAssetsInitialized;
    private bool packageInitialized;
    private bool readyToEnter;
    private bool waitingForDownloadConfirmation;
    private bool downloadConfirmed;
    private bool downloadCancelled;

    public static MyYooAsset Instance { get; private set; }
    private void Awake()
    {
        if(Instance !=null && Instance != this){
            Destroy(transform.root.gameObject);
            return;
        }
        Instance = this;
        // MyYooAsset和LoadDll都是BootstrapRoot的子节点。
        // DontDestroyOnLoad只对根对象生效，因此保留整棵启动层对象。
        DontDestroyOnLoad(transform.root.gameObject);
    }
    private void Start()
    {
        if (updateUI == null)
            updateUI = FindObjectOfType<BootUpdateUI>(true);

        updateUI?.Bind(this);
        BeginUpdate();
    }

    public void RetryUpdate()
    {
        BeginUpdate();
    }

    public void EnterGame()
    {
        if (!readyToEnter || isUpdating)
            return;

        readyToEnter = false;
        StartCoroutine(EnterGameFlow());
    }

    /// <summary>
    /// 玩家确认本次更新后，放行正在等待的更新协程。
    /// </summary>
    public void ConfirmDownload()
    {
        if (!waitingForDownloadConfirmation)
            return;

        downloadConfirmed = true;
        waitingForDownloadConfirmation = false;
    }

    /// <summary>
    /// 玩家拒绝更新。先终止更新流程，再退出游戏。
    /// </summary>
    public void CancelUpdateAndExit()
    {
        if (waitingForDownloadConfirmation)
        {
            downloadCancelled = true;
            waitingForDownloadConfirmation = false;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void BeginUpdate()
    {
        if (isUpdating) return;
        readyToEnter = false;
        StartCoroutine(UpdateFlow());
    }

    private IEnumerator UpdateFlow()
    {
        isUpdating = true;
        waitingForDownloadConfirmation = false;
        downloadConfirmed = false;
        downloadCancelled = false;
        updateUI?.Bind(this);
        updateUI?.ShowPreparing();

        //初始化YooAsset
        if (!yooAssetsInitialized)
        {
            YooAssets.Initialize();
            yooAssetsInitialized = true;
        }
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
        
        //执行初始化操作。重试远程步骤时不重复初始化已经成功的Package。
        if (!packageInitialized)
        {
            updateUI?.ShowStage("正在初始化资源系统", packageName);
            InitializePackageOperation initializeOperation = package.InitializePackageAsync(createParameters);
            yield return initializeOperation;

            if(initializeOperation.Status != EOperationStatus.Succeeded){
                FinishWithError("资源系统初始化", initializeOperation.Error);
                yield break;
            }else{
                packageInitialized = true;
                Debug.Log("初始化成功........");
            }
        }

        //获取资源版本
        updateUI?.ShowStage("正在检查资源版本", defaultHostServer);
        Debug.Log("开始请求远程资源版本...");
        var versionOptions = new RequestPackageVersionOptions();
        var operation = package.RequestPackageVersionAsync(versionOptions);
        yield return operation;
        Debug.Log($"远程版本请求结束，状态：{operation.Status}");
        if(operation.Status != EOperationStatus.Succeeded){
            FinishWithError("获取资源版本", operation.Error);
            yield break;
        }else{
            Debug.Log($"获取版本:{operation.PackageVersion}");
            packageVersion = operation.PackageVersion;
            updateUI?.ShowVersion(packageVersion);
        }

        //获取文件清单
        updateUI?.ShowStage("正在获取资源清单", $"版本 {packageVersion}");
        var options = new LoadPackageManifestOptions(packageVersion, 60);//加载文件清单选项,超时时间60秒
        var loadOperationManifest = package.LoadPackageManifestAsync(options);//加载文件清单操作
        yield return loadOperationManifest;
        if(loadOperationManifest.Status != EOperationStatus.Succeeded){
            FinishWithError("获取资源清单", loadOperationManifest.Error);
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
                FinishWithError("清除测试缓存", clearAllOperation.Error);
                yield break;
            }

            Debug.Log("测试模式：YooAsset下载缓存清除完成");
        }

        //创建下载器
        var optionsDownloader = new ResourceDownloaderOptions(downloadMaxNum, filedRetryNum);
        downloader =  package.CreateResourceDownloader(optionsDownloader);
        if(downloader.TotalDownloadCount==0){
            Debug.LogWarning("没有文件需要下载");
            updateUI?.ShowStage("资源已经是最新版本", "正在验证游戏内容……");
        }else{
            int downloadCount = downloader.TotalDownloadCount;
            long bytes =downloader.TotalDownloadBytes;
            Debug.Log($"需要下载{downloadCount}个文件,大小是{bytes/1024/1024}MB");

            // 创建下载器只会统计更新量，不会开始传输。先把更新量交给玩家确认。
            waitingForDownloadConfirmation = true;
            bool canWaitForUser = updateUI != null &&
                                  updateUI.ShowDownloadConfirmation(downloadCount, bytes);
            if (!canWaitForUser)
            {
                // 旧场景或按钮未绑定时保持兼容，避免客户端永远停在启动页。
                Debug.LogWarning("更新确认按钮未完整绑定，将直接开始下载资源");
                downloadConfirmed = true;
                waitingForDownloadConfirmation = false;
            }

            while (waitingForDownloadConfirmation)
                yield return null;

            if (downloadCancelled || !downloadConfirmed)
            {
                isUpdating = false;
                yield break;
            }

            updateUI?.ShowStage("正在开始下载", $"共 {downloadCount} 个文件");
            //开始下载
            downloader.DownloadProgressChanged += ProgressCallback;
            downloader.DownloadError += DownloadErrorCallback;
            downloader.StartDownload();
            yield return downloader;
            downloader.DownloadProgressChanged -= ProgressCallback;
            downloader.DownloadError -= DownloadErrorCallback;
            if(downloader.Status != EOperationStatus.Succeeded){
                FinishWithError("下载资源", downloader.Error);
                yield break;
            }else{
                Debug.Log($"下载完成...........");
                //UpdateDone();
            }
        }

        //清理失败不阻断启动；新Manifest和资源已经可用。
        updateUI?.ShowStage("正在清理旧资源", "请稍候……");
        var clearoptions = new ClearCacheOptions(ClearCacheMethods.ClearUnusedBundleFiles);
        var clearOperation = package.ClearCacheAsync(clearoptions);
        yield return clearOperation;
        if(clearOperation.Status != EOperationStatus.Succeeded){
            Debug.LogWarning($"清理无用缓存失败：{clearOperation.Error}");
        }else{
            Debug.Log($"清除缓存完成...........");
        }
        yield return UpdateDone();
        isUpdating = false;
        if (readyToEnter)
        {
            bool waitingForClick = updateUI != null && updateUI.ShowReadyToEnter();
            if (!waitingForClick)
            {
                Debug.LogWarning("启动界面没有绑定进入游戏按钮，将继续自动进入游戏");
                EnterGame();
            }
        }
    }

    /// <summary>
    /// 下载进度回调
    /// </summary>
    private void ProgressCallback(DownloadProgressChangedEventArgs args)
    {

        Debug.Log($"下载进度:{args.CurrentDownloadCount}/{args.TotalDownloadCount},"+
        $"{args.CurrentDownloadBytes/1024f/1024f:F2}/{args.TotalDownloadBytes/1024f/1024f:F2}MB,"+
        $"进度{args.Progress:P1}");
        updateUI?.ShowDownloadProgress(
            args.CurrentDownloadCount,
            args.TotalDownloadCount,
            args.CurrentDownloadBytes,
            args.TotalDownloadBytes,
            args.Progress);
    
    }
    /// <summary>
    /// 下载错误回调
    /// </summary>
    private void DownloadErrorCallback(DownloadErrorEventArgs args)
    {
        Debug.LogError($"下载错误:{args.ErrorInfo}");
    }

    private void FinishWithError(string stage, string error)
    {
        isUpdating = false;
        waitingForDownloadConfirmation = false;
        readyToEnter = false;
        var message = string.IsNullOrWhiteSpace(error) ? "未知错误" : error;
        Debug.LogError($"{stage}失败：{message}");
        updateUI?.ShowError(stage, message);
    }

    /// <summary>
    /// 热更新完成
    /// </summary>
private IEnumerator UpdateDone()
{
    updateUI?.ShowStage("正在加载游戏配置", "物品、掉落与生存数据");
    yield return GameContentService.InitializeAsync(package);

    if (!GameContentService.IsReady)
    {
        FinishWithError("游戏配置初始化", GameContentService.Error);
        yield break;
    }
    // 此时Package初始化、Manifest加载和资源下载流程都已完成。
    // 在这里加载HotUpdate.dll，避免LoadDll.Start抢在YooAsset初始化之前执行。
    if (hotUpdateDllLoader == null)
        hotUpdateDllLoader = FindObjectOfType<LoadDll>();

    if (hotUpdateDllLoader == null)
    {
        FinishWithError("加载热更新代码", "没有找到LoadDll组件");
        yield break;
    }

    updateUI?.ShowStage("正在加载热更新代码", "HotUpdate.dll");
    yield return hotUpdateDllLoader.LoadHotUpdateDll(package);

    if (!hotUpdateDllLoader.IsLoadSucceeded)
    {
        FinishWithError("加载热更新代码", "HotUpdate.dll 加载失败，请查看日志");
        yield break;
    }
    Debug.Log($"HotUpdate.dll 加载成功...........");

    readyToEnter = true;
}

private IEnumerator EnterGameFlow()
{
    isUpdating = true;
    updateUI?.ShowEnteringGame();

    // 从这里开始交给HotUpdate.dll。AOT层只认识反射调用器，不引用GameEntry类型。
    yield return hotUpdateDllLoader.RunHotUpdateEntry(package);

    if (!hotUpdateDllLoader.IsEntrySucceeded)
    {
        FinishWithError("进入游戏", "热更新入口执行失败，请查看日志");
        yield break;
    }

    isUpdating = false;
}

    /// <summary>
    /// 清理文件完成回调
    /// </summary>
    // private void ClearCacheOperation_Completed(AsyncOperationBase obj)
    // {
    //     StartCoroutine(UpdateDone());
    // }

    // Update is called once per frame
    void Update()
    {
    }

    /// <summary>
    /// 销毁时，将实例设为null
    /// </summary>
    private void OnDestroy()
    {
        if(Instance == this){
            GameContentService.Release();
            Instance = null;
        }
    }

    /// <summary>
    /// 加载YooAsset场景
    /// </summary>
    public IEnumerator LoadYooAssetScene(
    string sceneName,
    LoadSceneMode loadMode = LoadSceneMode.Single)
{
    if (string.IsNullOrWhiteSpace(sceneName))
    {
        Debug.LogError("场景名称不能为空");
        yield break;
    }

    if (package == null)
    {
        Debug.LogError("YooAsset Package 尚未初始化");
        yield break;
    }

    Debug.Log($"开始加载 YooAsset 场景：{sceneName}");

    SceneHandle handle = package.LoadSceneAsync(
        sceneName,
        loadMode,
        LocalPhysicsMode.None,
        true);

    yield return handle;

    if (handle.Status != EOperationStatus.Succeeded)
    {
        Debug.LogError(
            $"场景加载失败：{sceneName}\n{handle.Error}");
        yield break;
    }

    Debug.Log($"场景加载完成：{handle.SceneName}");
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
