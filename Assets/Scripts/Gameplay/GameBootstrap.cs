using System.Collections;
using UnityEngine;

/// <summary>
/// 场景启动入口，提供联调阶段的调试调用方法。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateBootstrap()
    {
        if (FindObjectOfType<GameBootstrap>() != null) return;
        // GameBootstrap 只属于局内地图。基地场景不能自动生成它，否则会错误进入 Raid 阶段并生成局内调试对象。
        if (FindObjectOfType<ShelterSceneController>() != null) return;
        if (FindObjectOfType<MapSpawnPoint>() == null && FindObjectOfType<ExtractionPoint>() == null) return;
        var bootstrapObject = new GameObject("GameBootstrap");
        bootstrapObject.AddComponent<GameBootstrap>();
    }

    [Tooltip("主流程状态机引用。")]
    [SerializeField] private GameStateMachine gameStateMachine;
    [Tooltip("生存数值系统引用。")]
    [SerializeField] private SurvivalSystem survivalSystem;
    [Tooltip("背包系统引用。")]
    [SerializeField] private InventoryService inventoryService;
    [Tooltip("工作台服务引用。")]
    [SerializeField] private WorkbenchService workbenchService;
    [Tooltip("庇护所模块服务引用。")]
    [SerializeField] private ShelterModuleService shelterModuleService;
    [Tooltip("夜袭系统引用。")]
    [SerializeField] private NightAssaultSystem nightAssaultSystem;
    [Tooltip("网格背包服务引用。为空时自动查找或自动创建。")]
    [SerializeField] private GridInventoryService gridInventoryService;
    [Tooltip("网格背包预览引用。为空时可自动创建。")]
    [SerializeField] private GridInventoryRuntimePreview gridInventoryRuntimePreview;
    [Tooltip("网格背包UnityUI引用。为空时可自动创建。")]
    [SerializeField] private GridInventoryUnityUI gridInventoryUnityUI;
    [Tooltip("JSON 掉落表数据库引用。为空时自动创建。")]
    [SerializeField] private LootTableDatabase lootTableDatabase;
    [Tooltip("场景 Canvas 下的世界交互提示和搜索进度 UI。必须在编辑器中预先创建；运行时不会生成。")]
    [SerializeField] private WorldInteractionPromptUI interactionPromptUi;
    [Header("Run Flow (出生与撤离)")]
    [Tooltip("局内玩家预制体。场景中不需要预摆 Player；启动后会从该预制体生成。")]
    [SerializeField] private PlayerCombatController2D playerPrefab;
    [Tooltip("场景中没有活动玩家时，是否自动从 Player Prefab 生成。正式地图建议开启。")]
    [SerializeField] private bool spawnPlayerWhenMissing = true;
    [Tooltip("进入地图时是否把玩家移动到 MapSpawnPoint。点位必须在编辑器中预先放置。")]
    [SerializeField] private bool placePlayerAtSpawnOnStart = true;
    [Tooltip("优先使用的玩家出生点 ID。为空时从所有 Player 类型出生点中随机选择。")]
    [SerializeField] private string preferredPlayerSpawnId;
    [Tooltip("通过调试入口明确开始新一局时，是否清空上一局的搜索容器状态。普通场景重载不会清空。")]
    [SerializeField] private bool clearLootContainerStateOnNewRun = true;
    [Tooltip("地图启动后是否把主流程切换到摸金阶段。")]
    [SerializeField] private bool enterRaidPhaseOnStart = true;
    [Tooltip("撤离成功反馈在公共提示 UI 中显示的秒数。")]
    [Min(0.1f)] [SerializeField] private float extractionSuccessMessageDuration = 3f;
    [Tooltip("是否自动给玩家挂载世界交互组件。")]
    [SerializeField] private bool autoCreatePlayerInteractor = true;
    [Header("World Interaction")]
    [Tooltip("仅在运行时自动创建 PlayerInteractor2D 时使用的默认检测半径。玩家已经挂载交互器时，以玩家组件上的配置为准。")]
    [Min(0.1f)] [SerializeField] private float playerInteractionRange = 1.6f;
    [Tooltip("仅在运行时自动创建 PlayerInteractor2D 时使用的默认扫描间隔。")]
    [Min(0.02f)] [SerializeField] private float interactionScanInterval = 0.08f;
    [Tooltip("运行时显示当前目标、距离以及搜索/拾取进度。正式 UI 接入后可以关闭。")]
    [SerializeField] private bool showInteractionOverlay = true;
    [Tooltip("在 Scene 视图中显示玩家交互范围的蓝色圆圈。")]
    [SerializeField] private bool showInteractionRangeGizmo = true;
    [Tooltip("启动时是否自动创建运行时调试面板。")]
    [SerializeField] private bool autoCreateRuntimeDebugPanel = true;
    [Tooltip("启动时是否自动创建网格背包预览窗口。")]
    [SerializeField] private bool autoCreateGridInventoryPreview = false;
    [Tooltip("启动时是否自动创建网格背包UnityUI。")]
    [SerializeField] private bool autoCreateGridInventoryUnityUI = true;
    [Tooltip("运行时调试面板引用。为空时可自动查找或自动创建。")]
    [SerializeField] private RuntimeDebugPanel runtimeDebugPanel;
    private bool extractionCompleted;
    private PlayerCombatController2D runtimePlayer;

    /// <summary>
    /// 自动查找关键系统引用，减少手工挂载遗漏。
    /// </summary>
    private void Awake()
    {
        if (gameStateMachine == null) gameStateMachine = FindSceneObjectOfType<GameStateMachine>();
        if (survivalSystem == null) survivalSystem = FindSceneObjectOfType<SurvivalSystem>();
        if (inventoryService == null) inventoryService = FindSceneObjectOfType<InventoryService>();
        if (workbenchService == null) workbenchService = FindSceneObjectOfType<WorkbenchService>();
        if (shelterModuleService == null) shelterModuleService = FindSceneObjectOfType<ShelterModuleService>();
        if (nightAssaultSystem == null) nightAssaultSystem = FindSceneObjectOfType<NightAssaultSystem>();
        if (gridInventoryService == null) gridInventoryService = FindSceneObjectOfType<GridInventoryService>();
        if (gridInventoryRuntimePreview == null) gridInventoryRuntimePreview = FindSceneObjectOfType<GridInventoryRuntimePreview>();
        if (gridInventoryUnityUI == null) gridInventoryUnityUI = FindMainInventoryUi();
        if (lootTableDatabase == null) lootTableDatabase = FindSceneObjectOfType<LootTableDatabase>();
        if (interactionPromptUi == null) interactionPromptUi = FindSceneObjectOfType<WorldInteractionPromptUI>();
        if (runtimeDebugPanel == null) runtimeDebugPanel = FindSceneObjectOfType<RuntimeDebugPanel>();
        EnsureGridInventorySystems();
        EnsureLootInteractionSystems();
        if (autoCreateRuntimeDebugPanel) EnsureRuntimeDebugPanel();
        BindRuntimeDebugPanel();
    }

    /// <summary>所有场景对象完成 Awake 后，开始本次地图流程并放置玩家。</summary>
    private void Start()
    {
        // 每张玩法场景都预摆相同核心预制体；切场景后优先重新绑定上一场景保留下来的权威实例。
        ResolveCoreReferences();
        EnsureGridInventorySystems();
        EnsureLootInteractionSystems();
        BindRuntimeDebugPanel();
        BeginMapRun(SceneFlowController.ConsumePendingNewRun());
    }

    /// <summary>
    /// 初始化当前地图。普通场景进入不会清除会话级容器快照，以便同一局重载后继续恢复。
    /// </summary>
    public void BeginMapRun()
    {
        BeginMapRun(false);
    }

    /// <summary>初始化地图，并按调用方意图决定是否开始全新的局内状态。</summary>
    private void BeginMapRun(bool startNewRun)
    {
        extractionCompleted = false;
        if (startNewRun && clearLootContainerStateOnNewRun) SearchableLootRuntimeStateStore.ClearAll();

        var extractionPoints = FindObjectsOfType<ExtractionPoint>(true);
        for (var i = 0; i < extractionPoints.Length; i++) extractionPoints[i].ResetForNewRun();

        if (enterRaidPhaseOnStart && gameStateMachine != null)
        {
            gameStateMachine.StartRun();
            gameStateMachine.EnterRaid();
        }

        if (placePlayerAtSpawnOnStart) PlacePlayerAtSpawn();
    }

    /// <summary>
    /// 选择配置的玩家出生点；未指定 ID 时随机选择一个 Player 类型点位。
    /// </summary>
    public bool PlacePlayerAtSpawn()
    {
        var player = ResolveOrSpawnPlayer();
        if (player == null)
        {
            Debug.LogWarning("无法设置玩家出生位置：没有活动玩家，并且没有可生成的 Player Prefab。", this);
            return false;
        }

        var spawnPoints = FindObjectsOfType<MapSpawnPoint>(true);
        var candidates = new System.Collections.Generic.List<MapSpawnPoint>();
        MapSpawnPoint selected = null;
        for (var i = 0; i < spawnPoints.Length; i++)
        {
            var point = spawnPoints[i];
            if (point == null || point.SpawnKind != MapSpawnKind.Player) continue;
            candidates.Add(point);
            if (!string.IsNullOrWhiteSpace(preferredPlayerSpawnId) &&
                point.SpawnId == preferredPlayerSpawnId)
            {
                selected = point;
            }
        }

        if (selected == null && candidates.Count > 0)
        {
            selected = candidates[Random.Range(0, candidates.Count)];
        }
        if (selected == null)
        {
            Debug.LogWarning("无法设置玩家出生位置：场景中没有 Player 类型的 MapSpawnPoint。", this);
            return false;
        }

        var spawnPosition = selected.GetRandomPosition();
        spawnPosition.z = player.transform.position.z;
        var body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.position = spawnPosition;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
        player.transform.position = spawnPosition;
        SetPlayerControlsEnabled(player, true);
        BindPlayerRuntime(player);
        Debug.Log($"玩家已放置到出生点 {selected.SpawnId}。", selected);
        return true;
    }

    /// <summary>优先返回场景中的活动玩家；没有时从配置的预制体实例化。</summary>
    private PlayerCombatController2D ResolveOrSpawnPlayer()
    {
        if (runtimePlayer != null && runtimePlayer.gameObject.activeInHierarchy) return runtimePlayer;

        runtimePlayer = FindObjectOfType<PlayerCombatController2D>();
        if (runtimePlayer != null) return runtimePlayer;
        if (!spawnPlayerWhenMissing || playerPrefab == null) return null;

        runtimePlayer = Instantiate(playerPrefab);
        runtimePlayer.name = playerPrefab.name;
        return runtimePlayer;
    }

    /// <summary>把运行时生成的玩家接入背包、搜索提示和交互系统。</summary>
    private void BindPlayerRuntime(PlayerCombatController2D player)
    {
        if (player == null) return;
        var interactor = player.GetComponent<PlayerInteractor2D>();
        var createdInteractor = interactor == null;
        if (createdInteractor && autoCreatePlayerInteractor)
        {
            interactor = player.gameObject.AddComponent<PlayerInteractor2D>();
        }
        if (interactor == null) return;

        if (createdInteractor)
        {
            interactor.Configure(
                playerInteractionRange,
                interactionScanInterval,
                showInteractionOverlay,
                showInteractionRangeGizmo);
        }
        interactor.enabled = true;
        interactor.Bind(inventoryService, gridInventoryService, gridInventoryUnityUI, interactionPromptUi);
    }

    /// <summary>统一开启或关闭玩家的移动战斗输入与世界交互输入。</summary>
    private static void SetPlayerControlsEnabled(PlayerCombatController2D player, bool enabled)
    {
        if (player == null) return;
        player.SetPlayerControlEnabled(enabled);
        var interactor = player.GetComponent<PlayerInteractor2D>();
        if (interactor != null) interactor.enabled = enabled;
        var body = player.GetComponent<Rigidbody2D>();
        if (!enabled && body != null) body.velocity = Vector2.zero;
    }

    /// <summary>
    /// 撤离点倒计时结束后的统一入口。当前样机保留背包并返回庇护所阶段；
    /// 后续存在正式庇护所场景时，可订阅 ExtractionCompletedEvent 进行存档和切场景。
    /// </summary>
    public bool CompleteExtraction(ExtractionPoint extractionPoint)
    {
        if (extractionCompleted || extractionPoint == null) return false;
        extractionCompleted = true;

        var player = runtimePlayer != null ? runtimePlayer : FindObjectOfType<PlayerCombatController2D>();
        SetPlayerControlsEnabled(player, false);
        gameStateMachine?.ReturnShelterBeforeNight();
        EventBus.Publish(new ExtractionCompletedEvent(
            extractionPoint.ExtractionId,
            extractionPoint.TargetId));

        if (interactionPromptUi == null) interactionPromptUi = FindSceneObjectOfType<WorldInteractionPromptUI>();
        interactionPromptUi?.ShowFeedback(
            $"撤离成功：{extractionPoint.DisplayName}",
            extractionSuccessMessageDuration);
        Debug.Log(
            $"撤离成功。撤离点={extractionPoint.ExtractionId}，目标流程={extractionPoint.TargetId}，玩家背包已保留。",
            extractionPoint);
        StartCoroutine(TransitionToShelterAfterDelay());
        return true;
    }

    /// <summary>给撤离成功提示留出显示时间，然后加载编辑器中预先制作的基地场景。</summary>
    private IEnumerator TransitionToShelterAfterDelay()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, extractionSuccessMessageDuration));
        if (SceneFlowController.Instance != null)
        {
            SceneFlowController.Instance.LoadShelter();
            yield break;
        }

        Debug.LogWarning("撤离已完成，但没有找到 SceneFlowController；当前仍停留在局内场景。", this);
    }

    /// <summary>重新解析会跨场景保留的核心服务。</summary>
    private void ResolveCoreReferences()
    {
        // 场景切换的第一帧，新场景自带的核心副本尚未完成延迟销毁；必须优先使用上一场景保留的实例。
        var persistent = PersistentGameServices.Instance;
        if (persistent != null)
        {
            gameStateMachine = persistent.GetService<GameStateMachine>() ?? gameStateMachine;
            survivalSystem = persistent.GetService<SurvivalSystem>() ?? survivalSystem;
            inventoryService = persistent.GetService<InventoryService>() ?? inventoryService;
            workbenchService = persistent.GetService<WorkbenchService>() ?? workbenchService;
            shelterModuleService = persistent.GetService<ShelterModuleService>() ?? shelterModuleService;
            nightAssaultSystem = persistent.GetService<NightAssaultSystem>() ?? nightAssaultSystem;
            gridInventoryService = persistent.GetService<GridInventoryService>() ?? gridInventoryService;
            lootTableDatabase = persistent.GetService<LootTableDatabase>() ?? lootTableDatabase;
        }

        if (gameStateMachine == null) gameStateMachine = FindSceneObjectOfType<GameStateMachine>();
        if (survivalSystem == null) survivalSystem = FindSceneObjectOfType<SurvivalSystem>();
        if (inventoryService == null) inventoryService = FindSceneObjectOfType<InventoryService>();
        if (workbenchService == null) workbenchService = FindSceneObjectOfType<WorkbenchService>();
        if (shelterModuleService == null) shelterModuleService = FindSceneObjectOfType<ShelterModuleService>();
        if (nightAssaultSystem == null) nightAssaultSystem = FindSceneObjectOfType<NightAssaultSystem>();
        if (gridInventoryService == null) gridInventoryService = FindSceneObjectOfType<GridInventoryService>();
        if (lootTableDatabase == null) lootTableDatabase = FindSceneObjectOfType<LootTableDatabase>();
    }

    /// <summary>
    /// 调试用：开始一轮出击流程。
    /// </summary>
    public void DebugStartRun()
    {
        BeginMapRun(true);
        Debug.Log("开始一轮出击流程");
    }

    /// <summary>
    /// 调试用：消耗罐头并恢复饥饿值。
    /// </summary>
    public void DebugEat()
    {
        if (inventoryService == null || survivalSystem == null) return;
        if (inventoryService.ConsumeItem("food_canned", 1))
            survivalSystem.Eat(25f);
    }

    /// <summary>
    /// 调试用：消耗瓶装水并恢复口渴值。
    /// </summary>
    public void DebugDrink()
    {
        if (inventoryService == null || survivalSystem == null) return;
        if (inventoryService.ConsumeItem("water_bottle", 1))
            survivalSystem.Drink(30f);
    }

    /// <summary>
    /// 调试用：恢复疲劳值。
    /// </summary>
    public void DebugSleep()
    {
        if (survivalSystem == null) return;
        survivalSystem.Sleep(40f);
    }

    /// <summary>
    /// 调试用：生成生存箱掉落并发放背包。
    /// </summary>
    public void DebugGrantSurvivalLoot()
    {
        if (lootTableDatabase == null || inventoryService == null) return;
        lootTableDatabase.GrantToInventory(inventoryService, "survival_basic", 2, 2);
    }

    /// <summary>
    /// 调试用：工作台尝试制作绷带。
    /// </summary>
    public void DebugCraftBandage()
    {
        if (workbenchService == null || inventoryService == null) return;
        workbenchService.TryCraft("craft_bandage", inventoryService);
    }

    /// <summary>
    /// 调试用：使用庇护所饮水模块。
    /// </summary>
    public void DebugUseShelterDrink()
    {
        if (shelterModuleService == null || survivalSystem == null) return;
        shelterModuleService.UseDrinkStation(survivalSystem);
    }

    /// <summary>
    /// 调试用：开始夜袭。
    /// </summary>
    public void DebugStartNightAssault()
    {
        if (nightAssaultSystem == null) return;
        nightAssaultSystem.StartAssault();
    }

    /// <summary>
    /// 调试用：按胜利结算夜袭。
    /// </summary>
    public void DebugResolveNightAssaultVictory()
    {
        if (nightAssaultSystem == null) return;
        nightAssaultSystem.ResolveAssault(NightAssaultResult.Victory);
    }

    private void EnsureRuntimeDebugPanel()
    {
        if (runtimeDebugPanel != null) return;
        var panelObject = new GameObject("RuntimeDebugPanel");
        runtimeDebugPanel = panelObject.AddComponent<RuntimeDebugPanel>();
    }

    private void EnsureGridInventorySystems()
    {
        if (gridInventoryService == null)
        {
            var serviceObject = new GameObject("GridInventoryService");
            gridInventoryService = serviceObject.AddComponent<GridInventoryService>();
        }

        gridInventoryService.EnsureInitialized();
        if (inventoryService != null) inventoryService.BindGridBackend(gridInventoryService, true);

        if (autoCreateGridInventoryUnityUI && gridInventoryUnityUI == null)
        {
            var uiObject = new GameObject("GridInventoryUnityUI");
            gridInventoryUnityUI = uiObject.AddComponent<GridInventoryUnityUI>();
        }

        if (gridInventoryUnityUI != null)
        {
            if (!gridInventoryUnityUI.gameObject.activeSelf) gridInventoryUnityUI.gameObject.SetActive(true);
            gridInventoryUnityUI.Bind(gridInventoryService);
            gridInventoryUnityUI.SetOpen(false);
        }

        if (!autoCreateGridInventoryPreview) return;

        if (gridInventoryRuntimePreview == null)
        {
            var previewObject = new GameObject("GridInventoryRuntimePreview");
            gridInventoryRuntimePreview = previewObject.AddComponent<GridInventoryRuntimePreview>();
        }
    }

    /// <summary>
    /// 连接场景中已有的搜索、交互提示和物品数据服务；仅在必要时补齐运行所需服务。
    /// </summary>
    private void EnsureLootInteractionSystems()
    {
        if (lootTableDatabase == null)
        {
            var databaseObject = new GameObject("LootTableDatabase");
            lootTableDatabase = databaseObject.AddComponent<LootTableDatabase>();
        }

        if (!autoCreatePlayerInteractor) return;
        var player = FindObjectOfType<PlayerCombatController2D>();
        if (player == null) return;
        runtimePlayer = player;
        BindPlayerRuntime(player);
    }

    private void BindRuntimeDebugPanel()
    {
        if (runtimeDebugPanel == null) return;
        runtimeDebugPanel.Bind(this, survivalSystem, inventoryService, gameStateMachine, nightAssaultSystem);
    }

    private static T FindSceneObjectOfType<T>() where T : Component
    {
        var activeObject = FindObjectOfType<T>();
        if (activeObject != null) return activeObject;

        var objects = Resources.FindObjectsOfTypeAll<T>();
        for (var i = 0; i < objects.Length; i++)
        {
            var candidate = objects[i];
            if (candidate == null) continue;
            if (!candidate.gameObject.scene.IsValid()) continue;
            return candidate;
        }

        return null;
    }

    private static GridInventoryUnityUI FindMainInventoryUi()
    {
        var views = Resources.FindObjectsOfTypeAll<GridInventoryUnityUI>();
        for (var i = 0; i < views.Length; i++)
        {
            var candidate = views[i];
            if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;
            if (candidate.name == "InventoryUI_main_bag") return candidate;
        }

        for (var i = 0; i < views.Length; i++)
        {
            var candidate = views[i];
            if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;
            if (!candidate.IsSideContainerView && candidate.ContainerId == "main_bag") return candidate;
        }

        return FindSceneObjectOfType<GridInventoryUnityUI>();
    }
}
