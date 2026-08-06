using UnityEngine;

/// <summary>
/// 基地场景入口：生成场景本地玩家，并把基地 UI、交互器重新绑定到跨场景保留的数据服务。
/// 玩家本体不会跨场景保留，避免局内角色状态和基地角色对象互相污染。
/// </summary>
public sealed class ShelterSceneController : MonoBehaviour
{
    private const string StashContainerId = "shelter_stash";

    [Header("编辑器场景引用")]
    [Tooltip("基地玩家预制体。玩家只在进入基地后实例化，不属于持久数据服务。")]
    [SerializeField] private PlayerCombatController2D playerPrefab;
    [Tooltip("撤离返回基地时的玩家出生点。")]
    [SerializeField] private MapSpawnPoint returnSpawnPoint;
    [Tooltip("基地中的玩家主背包 UI 预制体实例。")]
    [SerializeField] private GridInventoryUnityUI mainBagUi;
    [Tooltip("基地中的口袋 UI 预制体实例。必须使用 InventoryUI_min_bag，确保与局内布局一致。")]
    [SerializeField] private GridInventoryUnityUI pocketUi;
    [Tooltip("基地仓库 UI 预制体实例。")]
    [SerializeField] private GridInventoryUnityUI stashUi;
    [Tooltip("基地场景中的世界交互提示 UI。")]
    [SerializeField] private WorldInteractionPromptUI interactionPromptUi;

    [Header("仓库")]
    [Tooltip("基地永久仓库的行数。后续可以根据升级系统调整。")]
    [Min(1)] [SerializeField] private int stashRows = 12;
    [Tooltip("基地永久仓库的列数。后续可以根据升级系统调整。")]
    [Min(1)] [SerializeField] private int stashColumns = 16;
    [Tooltip("仓库初始已解锁格数。按从上到下、从左到右解锁；0 表示全部解锁。")]
    [Min(0)] [SerializeField] private int stashInitialUnlockedCells = 96;

    private InventoryService inventoryService;
    private GridInventoryService gridInventoryService;
    private SurvivalSystem survivalSystem;
    private ShelterModuleService shelterModuleService;
    private PlayerCombatController2D runtimePlayer;

    public SurvivalSystem Survival => survivalSystem;
    public ShelterModuleService ShelterModules => shelterModuleService;

    private void Start()
    {
        ResolveServices();
        FindObjectOfType<GameStateMachine>()?.ReturnShelterBeforeNight();
        PrepareStorageUi();
        SpawnAndBindPlayer();
    }

    /// <summary>打开永久仓库与玩家背包，供仓库交互点调用。</summary>
    public void OpenStash()
    {
        PrepareStorageUi();
        mainBagUi?.SetOpen(true);
        stashUi?.SetOpen(true);
    }

    /// <summary>从基地进入新一轮局内地图。</summary>
    public void StartRaid()
    {
        if (runtimePlayer != null) runtimePlayer.SetPlayerControlEnabled(false);
        SceneFlowController.Instance?.LoadRaid();
    }

    /// <summary>编辑器构建工具用于绑定场景中可见并可调整的对象。</summary>
    public void Configure(
        PlayerCombatController2D prefab,
        MapSpawnPoint spawnPoint,
        GridInventoryUnityUI mainUi,
        GridInventoryUnityUI pocketView,
        GridInventoryUnityUI storageUi,
        WorldInteractionPromptUI promptUi)
    {
        playerPrefab = prefab;
        returnSpawnPoint = spawnPoint;
        mainBagUi = mainUi;
        pocketUi = pocketView;
        stashUi = storageUi;
        interactionPromptUi = promptUi;
    }

    private void ResolveServices()
    {
        inventoryService = FindObjectOfType<InventoryService>();
        gridInventoryService = FindObjectOfType<GridInventoryService>();
        survivalSystem = FindObjectOfType<SurvivalSystem>();
        shelterModuleService = FindObjectOfType<ShelterModuleService>();
        if (mainBagUi == null || pocketUi == null || stashUi == null)
        {
            var views = FindObjectsOfType<GridInventoryUnityUI>(true);
            for (var i = 0; i < views.Length; i++)
            {
                if (views[i] == null) continue;
                if (views[i].name == "InventoryUI_main_bag") mainBagUi = views[i];
                else if (views[i].name == "InventoryUI_min_bag") pocketUi = views[i];
                else if (views[i].name == "InventoryUI_shelter_stash") stashUi = views[i];
            }
        }
    }

    private void PrepareStorageUi()
    {
        if (gridInventoryService == null) return;
        gridInventoryService.EnsureInitialized();
        gridInventoryService.RegisterRuntimeContainer(
            StashContainerId,
            stashRows,
            stashColumns,
            GridContainerOwnerType.Storage,
            false,
            null,
            stashInitialUnlockedCells);

        inventoryService?.BindGridBackend(gridInventoryService, true);
        if (mainBagUi != null)
        {
            mainBagUi.Bind(gridInventoryService);
            mainBagUi.SetOpen(false);
        }
        if (pocketUi != null)
        {
            pocketUi.BindToContainer(gridInventoryService, "min_bag", true, "口袋");
            pocketUi.SetOpen(false);
        }
        if (stashUi != null)
        {
            stashUi.BindToContainer(gridInventoryService, StashContainerId, true, "基地仓库");
            stashUi.SetOpen(false);
        }
    }

    private void SpawnAndBindPlayer()
    {
        runtimePlayer = FindObjectOfType<PlayerCombatController2D>();
        if (runtimePlayer == null && playerPrefab != null)
        {
            runtimePlayer = Instantiate(playerPrefab);
            runtimePlayer.name = playerPrefab.name;
        }
        if (runtimePlayer == null) return;

        var spawnPosition = returnSpawnPoint != null
            ? returnSpawnPoint.GetRandomPosition()
            : Vector3.zero;
        spawnPosition.z = runtimePlayer.transform.position.z;
        runtimePlayer.transform.position = spawnPosition;
        runtimePlayer.SetPlayerControlEnabled(true);

        var body = runtimePlayer.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.position = spawnPosition;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        var interactor = runtimePlayer.GetComponent<PlayerInteractor2D>();
        if (interactor == null) interactor = runtimePlayer.gameObject.AddComponent<PlayerInteractor2D>();
        interactor.enabled = true;
        interactor.Bind(inventoryService, gridInventoryService, mainBagUi, interactionPromptUi);
    }
}
