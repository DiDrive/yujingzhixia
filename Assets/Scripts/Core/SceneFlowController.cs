using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 统一管理基地与局内地图之间的场景切换。
/// 本组件与 PersistentGameServices 放在同一个编辑器场景对象上并跨场景保留。
/// </summary>
public sealed class SceneFlowController : MonoBehaviour
{
    [Tooltip("局内地图场景名。开始新一轮出击时加载此场景。")]
    [SerializeField] private string raidSceneName = "RaidScene";
    [Tooltip("基地场景名。成功撤离后加载此场景。")]
    [SerializeField] private string shelterSceneName = "ShelterScene";
    [Tooltip("场景切换期间是否在 Console 输出流程信息。")]
    [SerializeField] private bool logTransitions = true;

    private bool loading;//是否正在加载场景
    private static bool pendingNewRun;//是否待启动新游戏

    private static SceneFlowController instance;
    public static SceneFlowController Instance
    {
        get
        {
            if (instance == null) instance = FindObjectOfType<SceneFlowController>(true);
            return instance;
        }
        private set => instance = value;
    }
    public bool IsLoading => loading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
        pendingNewRun = false;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    /// <summary>成功撤离后进入基地；背包和生存数据由常驻核心服务直接保留。</summary>
    public void LoadShelter()
    {
        if (!loading) StartCoroutine(LoadSceneRoutine(shelterSceneName, false));
    }

    /// <summary>从基地开始新一轮出击，并通知局内入口重置本局搜索状态。</summary>
    public void LoadRaid()
    {
        if (!loading) StartCoroutine(LoadSceneRoutine(raidSceneName, true));
    }

    /// <summary>GameBootstrap 在 Start 时消费一次，区分首次启动和从基地重新出击。</summary>
    public static bool ConsumePendingNewRun()
    {
        var value = pendingNewRun;
        pendingNewRun = false;
        return value;
    }

    /// <summary>供编辑器场景构建工具写入场景名。</summary>
    public void Configure(string raid, string shelter)
    {
        if (!string.IsNullOrWhiteSpace(raid)) raidSceneName = raid;
        if (!string.IsNullOrWhiteSpace(shelter)) shelterSceneName = shelter;
    }

    private IEnumerator LoadSceneRoutine(string sceneName, bool startsNewRun)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) yield break;
        loading = true;

        if (MyYooAsset.Instance == null)
        {
            loading = false;
            Debug.LogError("MyYooAsset 未加载，不能进入游戏场景", this);
            yield break;
        }

        if (startsNewRun)
        {
            if (SaveGameService.Instance == null || !SaveGameService.Instance.BeginRunTransaction())
            {
                loading = false;
                Debug.LogError("无法创建出击前存档快照，本次场景切换已取消。", this);
                yield break;
            }

            SearchableLootRuntimeStateStore.ClearAll();
            MinimapDiscoveryStateStore.ClearAll();
            FindObjectOfType<GridInventoryService>()?.ClearTransientWorldContainers();
            FindObjectOfType<GameStateMachine>()?.StartRun();
            pendingNewRun = true;
        }
        else
        {
            FindObjectOfType<GameStateMachine>()?.ReturnShelterBeforeNight();
            if (SaveGameService.Instance == null || !SaveGameService.Instance.CommitRunTransaction())
            {
                loading = false;
                Debug.LogError("撤离进度写入永久存档失败，已停止返回基地。", this);
                yield break;
            }
            pendingNewRun = false;
        }

        // if (logTransitions) Debug.Log($"正在切换场景：{SceneManager.GetActiveScene().name} -> {sceneName}", this);
        // var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        // if (operation == null)
        // {
        //     loading = false;
        //     Debug.LogError($"无法加载场景 {sceneName}。请确认场景已加入 Build Settings。", this);
        //     yield break;
        // }

        // while (!operation.isDone) yield return null;
        // loading = false;
        
        yield return MyYooAsset.Instance.LoadYooAssetScene(sceneName,LoadSceneMode.Single);
        if (SceneManager.GetActiveScene().name != sceneName && startsNewRun)
        {
            SaveGameService.Instance?.Load();
            pendingNewRun = false;
            Debug.LogError($"进入 {sceneName} 失败，已回滚到出击前存档。", this);
        }
        loading = false;//场景加载完成，重置加载状态
    }
}
