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
        var bootstrapObject = new GameObject("GameBootstrap");
        bootstrapObject.AddComponent<GameBootstrap>();
    }

    [Tooltip("主流程状态机引用。")]
    [SerializeField] private GameStateMachine gameStateMachine;
    [Tooltip("生存数值系统引用。")]
    [SerializeField] private SurvivalSystem survivalSystem;
    [Tooltip("背包系统引用。")]
    [SerializeField] private InventoryService inventoryService;
    [Tooltip("掉落生成器引用。")]
    [SerializeField] private LootSpawner lootSpawner;
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
    [Tooltip("启动时是否自动创建运行时调试面板。")]
    [SerializeField] private bool autoCreateRuntimeDebugPanel = true;
    [Tooltip("启动时是否自动创建网格背包预览窗口。")]
    [SerializeField] private bool autoCreateGridInventoryPreview = false;
    [Tooltip("启动时是否自动创建网格背包UnityUI。")]
    [SerializeField] private bool autoCreateGridInventoryUnityUI = true;
    [Tooltip("运行时调试面板引用。为空时可自动查找或自动创建。")]
    [SerializeField] private RuntimeDebugPanel runtimeDebugPanel;

    /// <summary>
    /// 自动查找关键系统引用，减少手工挂载遗漏。
    /// </summary>
    private void Awake()
    {
        if (gameStateMachine == null) gameStateMachine = FindSceneObjectOfType<GameStateMachine>();
        if (survivalSystem == null) survivalSystem = FindSceneObjectOfType<SurvivalSystem>();
        if (inventoryService == null) inventoryService = FindSceneObjectOfType<InventoryService>();
        if (lootSpawner == null) lootSpawner = FindSceneObjectOfType<LootSpawner>();
        if (workbenchService == null) workbenchService = FindSceneObjectOfType<WorkbenchService>();
        if (shelterModuleService == null) shelterModuleService = FindSceneObjectOfType<ShelterModuleService>();
        if (nightAssaultSystem == null) nightAssaultSystem = FindSceneObjectOfType<NightAssaultSystem>();
        if (gridInventoryService == null) gridInventoryService = FindSceneObjectOfType<GridInventoryService>();
        if (gridInventoryRuntimePreview == null) gridInventoryRuntimePreview = FindSceneObjectOfType<GridInventoryRuntimePreview>();
        if (gridInventoryUnityUI == null) gridInventoryUnityUI = FindSceneObjectOfType<GridInventoryUnityUI>();
        if (runtimeDebugPanel == null) runtimeDebugPanel = FindSceneObjectOfType<RuntimeDebugPanel>();
        EnsureGridInventorySystems();
        if (autoCreateRuntimeDebugPanel) EnsureRuntimeDebugPanel();
        BindRuntimeDebugPanel();
    }

    /// <summary>
    /// 调试用：开始一轮出击流程。
    /// </summary>
    public void DebugStartRun()
    {
        if (gameStateMachine == null) return;
        gameStateMachine.StartRun();
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
        if (lootSpawner == null || inventoryService == null) return;
        lootSpawner.GrantLoot(inventoryService, LootChestType.Survival, 2);
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
}
