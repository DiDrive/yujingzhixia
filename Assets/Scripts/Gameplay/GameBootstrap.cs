using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>
/// 场景启动入口，提供联调阶段的调试调用方法。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [PropertyTooltip("主流程状态机引用。")]
    [SerializeField] private GameStateMachine gameStateMachine;
    [PropertyTooltip("生存数值系统引用。")]
    [SerializeField] private SurvivalSystem survivalSystem;
    [PropertyTooltip("背包系统引用。")]
    [SerializeField] private InventoryService inventoryService;
    [PropertyTooltip("掉落生成器引用。")]
    [SerializeField] private LootSpawner lootSpawner;
    [PropertyTooltip("工作台服务引用。")]
    [SerializeField] private WorkbenchService workbenchService;
    [PropertyTooltip("庇护所模块服务引用。")]
    [SerializeField] private ShelterModuleService shelterModuleService;
    [PropertyTooltip("夜袭系统引用。")]
    [SerializeField] private NightAssaultSystem nightAssaultSystem;
    [PropertyTooltip("启动时是否自动创建运行时调试面板。")]
    [SerializeField] private bool autoCreateRuntimeDebugPanel = true;
    [PropertyTooltip("运行时调试面板引用。为空时可自动查找或自动创建。")]
    [SerializeField] private RuntimeDebugPanel runtimeDebugPanel;

    /// <summary>
    /// 自动查找关键系统引用，减少手工挂载遗漏。
    /// </summary>
    private void Awake()
    {
        if (gameStateMachine == null) gameStateMachine = FindObjectOfType<GameStateMachine>();
        if (survivalSystem == null) survivalSystem = FindObjectOfType<SurvivalSystem>();
        if (inventoryService == null) inventoryService = FindObjectOfType<InventoryService>();
        if (lootSpawner == null) lootSpawner = FindObjectOfType<LootSpawner>();
        if (workbenchService == null) workbenchService = FindObjectOfType<WorkbenchService>();
        if (shelterModuleService == null) shelterModuleService = FindObjectOfType<ShelterModuleService>();
        if (nightAssaultSystem == null) nightAssaultSystem = FindObjectOfType<NightAssaultSystem>();
        if (runtimeDebugPanel == null) runtimeDebugPanel = FindObjectOfType<RuntimeDebugPanel>();
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

    private void BindRuntimeDebugPanel()
    {
        if (runtimeDebugPanel == null) return;
        runtimeDebugPanel.Bind(this, survivalSystem, inventoryService, gameStateMachine, nightAssaultSystem);
    }
}
