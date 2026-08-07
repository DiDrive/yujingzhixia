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

    /// <summary>获取测试面板显示用的容器容量状态。</summary>
    public bool TryGetContainerUnlockStatus(string containerId, out int unlocked, out int total, out int columns)
    {
        unlocked = 0;
        total = 0;
        columns = 0;
        if (gridInventoryService == null) ResolveServices();
        if (gridInventoryService == null || !gridInventoryService.TryGetContainerSize(containerId, out var rows, out columns)) return false;
        unlocked = gridInventoryService.GetUnlockedCellCount(containerId);
        total = rows * columns;
        return true;
    }

    /// <summary>测试：解锁基地仓库的下一整行。</summary>
    public void TestUnlockNextStashRow()
    {
        TestUnlockNextRow(StashContainerId, "基地仓库");
    }

    /// <summary>测试：解锁主背包的下一整行。</summary>
    public void TestUnlockNextMainBagRow()
    {
        TestUnlockNextRow("main_bag", "主背包");
    }

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
            // 仓库是侧边容器，但需要保留“整理”等动作按钮。
            stashUi.BindToContainer(gridInventoryService, StashContainerId, true, "基地仓库", true);
            stashUi.SetOpen(false);
        }
    }

    private void TestUnlockNextRow(string containerId, string displayName)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning($"{displayName}测试解锁只能在 Play Mode 中执行。", this);
            return;
        }

        if (gridInventoryService == null) ResolveServices();
        if (gridInventoryService == null || !gridInventoryService.TryGetContainerSize(containerId, out var rows, out var columns))
        {
            Debug.LogWarning($"找不到{displayName}容器：{containerId}。", this);
            return;
        }

        if (gridInventoryService.TryUnlockNextCells(containerId, columns, out var unlocked))
        {
            Debug.Log($"{displayName}已解锁下一行：{unlocked}/{rows * columns} 格。", this);
            return;
        }

        Debug.Log($"{displayName}已经全部解锁：{gridInventoryService.GetUnlockedCellCount(containerId)}/{rows * columns} 格。", this);
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
