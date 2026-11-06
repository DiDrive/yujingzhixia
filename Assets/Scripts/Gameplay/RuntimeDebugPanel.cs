using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 运行时调试面板：展示核心状态、提供调试按钮、记录日志，并支持窗口拖拽。
/// </summary>
public class RuntimeDebugPanel : MonoBehaviour
{
    [PropertyTooltip("是否显示调试面板。可通过 F1 快捷键切换。")]
    [SerializeField] private bool visible = true;
    [PropertyTooltip("调试面板初始左上角位置。")]
    [SerializeField] private Vector2 panelPosition = new(20f, 20f);
    [PropertyTooltip("调试面板初始尺寸。")]
    [SerializeField] private Vector2 panelSize = new(520f, 620f);
    [PropertyTooltip("日志最大保留条数。超过后自动丢弃最旧日志。")]
    [SerializeField] private int maxLogs = 80;
    [PropertyTooltip("日志显示区域高度。")]
    [SerializeField] private float logAreaHeight = 220f;
    [PropertyTooltip("可拖拽标题栏高度。")]
    [SerializeField] private float draggableHeaderHeight = 24f;

    private GameBootstrap bootstrap;
    private SurvivalSystem survivalSystem;
    private InventoryService inventoryService;
    private GameStateMachine gameStateMachine;
    private NightAssaultSystem nightAssaultSystem;
    private readonly Queue<string> logs = new();
    private Vector2 contentScrollPosition;
    private Vector2 logScrollPosition;
    private bool snapLogToBottom;
    private Rect windowRect;
    private bool isWindowRectInitialized;
    private const int WindowId = 295104;

    /// <summary>
    /// 绑定调试面板需要访问的系统引用。
    /// </summary>
    public void Bind(
        GameBootstrap bootstrapRef,
        SurvivalSystem survivalRef,
        InventoryService inventoryRef,
        GameStateMachine stateMachineRef,
        NightAssaultSystem nightRef)
    {
        bootstrap = bootstrapRef;
        survivalSystem = survivalRef;
        inventoryService = inventoryRef;
        gameStateMachine = stateMachineRef;
        nightAssaultSystem = nightRef;
    }

    /// <summary>
    /// 响应快捷键切换面板可见性。
    /// </summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) visible = !visible;
    }

    /// <summary>
    /// 绘制并刷新调试窗口。
    /// </summary>
    private void OnGUI()
    {
        if (!visible) return;
        EnsureWindowRect();
        windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "Runtime Debug Panel");
        panelPosition = new Vector2(windowRect.x, windowRect.y);
        panelSize = new Vector2(windowRect.width, windowRect.height);
    }

    /// <summary>
    /// 绘制窗口内容并处理拖拽区域。
    /// </summary>
    private void DrawWindow(int windowId)
    {
        var contentWidth = Mathf.Max(100f, windowRect.width - 24f);
        var contentHeight = Mathf.Max(80f, windowRect.height - draggableHeaderHeight - 14f);
        GUILayout.BeginArea(new Rect(12f, draggableHeaderHeight + 2f, contentWidth, contentHeight));
        contentScrollPosition = GUILayout.BeginScrollView(contentScrollPosition);

        DrawStats();
        GUILayout.Space(8f);
        DrawActions();
        GUILayout.Space(8f);
        DrawLogs();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.DragWindow(new Rect(0f, 0f, windowRect.width, draggableHeaderHeight));
    }

    /// <summary>
    /// 初始化窗口矩形，避免每帧重置位置与尺寸。
    /// </summary>
    private void EnsureWindowRect()
    {
        if (isWindowRectInitialized) return;
        windowRect = new Rect(panelPosition.x, panelPosition.y, panelSize.x, panelSize.y);
        isWindowRectInitialized = true;
    }

    /// <summary>
    /// 绘制状态信息区。
    /// </summary>
    private void DrawStats()
    {
        GUILayout.Label($"State: {GetPhaseText()}");
        GUILayout.Label($"Night: {(nightAssaultSystem != null && nightAssaultSystem.IsRunning ? "Running" : "Idle")}  Wave: {GetWaveText()}");
        GUILayout.Label($"HP: {GetFloat(survivalSystem != null ? survivalSystem.hp : 0f)}");
        GUILayout.Label($"Hunger: {GetFloat(survivalSystem != null ? survivalSystem.hunger : 0f)}");
        GUILayout.Label($"Thirst: {GetFloat(survivalSystem != null ? survivalSystem.thirst : 0f)}");
        GUILayout.Label($"Fatigue: {GetFloat(survivalSystem != null ? survivalSystem.fatigue : 0f)}");
        GUILayout.Label($"Food: {GetInventoryCount("food_canned")}  Water: {GetInventoryCount("water_bottle")}  Cloth: {GetInventoryCount("cloth_raw")}  Bandage: {GetInventoryCount("bandage")}");
    }

    /// <summary>
    /// 绘制调试动作按钮区。
    /// </summary>
    private void DrawActions()
    {
        GUILayout.Label("Save System V1");
        if (GUILayout.Button("Save Slot 0")) InvokeSaveAction(() => SaveGameService.Instance != null && SaveGameService.Instance.Save("debug-panel"), "Save Slot 0");
        if (GUILayout.Button("Load Slot 0")) InvokeSaveAction(() => SaveGameService.Instance != null && SaveGameService.Instance.Load(), "Load Slot 0");
        if (GUILayout.Button("Clear Slot 0")) InvokeSaveAction(() => SaveGameService.Instance != null && SaveGameService.Instance.DeleteSave(), "Clear Slot 0");
        GUILayout.Space(8f);
        GUILayout.Label("Gameplay Debug");
        if (GUILayout.Button("Start Run")) InvokeAction(() => bootstrap.DebugStartRun(), "Start Run");
        if (GUILayout.Button("Eat")) InvokeAction(() => bootstrap.DebugEat(), "Eat");
        if (GUILayout.Button("Drink")) InvokeAction(() => bootstrap.DebugDrink(), "Drink");
        if (GUILayout.Button("Sleep")) InvokeAction(() => bootstrap.DebugSleep(), "Sleep");
        if (GUILayout.Button("Grant Survival Loot")) InvokeAction(() => bootstrap.DebugGrantSurvivalLoot(), "Grant Survival Loot");
        if (GUILayout.Button("Craft Bandage")) InvokeAction(() => bootstrap.DebugCraftBandage(), "Craft Bandage");
        if (GUILayout.Button("Use Shelter Drink")) InvokeAction(() => bootstrap.DebugUseShelterDrink(), "Use Shelter Drink");
        if (GUILayout.Button("Start Night Assault")) InvokeAction(() => bootstrap.DebugStartNightAssault(), "Start Night Assault");
        if (GUILayout.Button("Resolve Night Assault Victory")) InvokeAction(() => bootstrap.DebugResolveNightAssaultVictory(), "Resolve Night Assault Victory");
    }

    /// <summary>
    /// 绘制日志列表并支持内部滚动和自动定位到底部。
    /// </summary>
    private void DrawLogs()
    {
        GUILayout.Label("Logs:");
        logScrollPosition = GUILayout.BeginScrollView(logScrollPosition, GUILayout.Height(logAreaHeight));
        foreach (var line in logs)
        {
            GUILayout.Label(line);
        }
        GUILayout.EndScrollView();
        if (snapLogToBottom && Event.current.type == EventType.Repaint)
        {
            logScrollPosition.y = float.MaxValue;
            snapLogToBottom = false;
        }
    }

    /// <summary>
    /// 执行按钮动作并写入日志。
    /// </summary>
    private void InvokeAction(System.Action action, string actionName)
    {
        if (bootstrap == null)
        {
            PushLog("Bootstrap missing");
            return;
        }

        action?.Invoke();
        PushLog($"{actionName} executed");
    }

    private void InvokeSaveAction(System.Func<bool> action, string actionName)
    {
        var succeeded = action != null && action();
        PushLog($"{actionName}: {(succeeded ? "OK" : "Failed")}");
    }

    /// <summary>
    /// 追加一条日志，并在下一帧自动滚动到底部。
    /// </summary>
    private void PushLog(string text)
    {
        if (logs.Count >= maxLogs) logs.Dequeue();
        logs.Enqueue($"[{System.DateTime.Now:HH:mm:ss}] {text}");
        snapLogToBottom = true;
    }

    /// <summary>
    /// 获取当前状态机阶段文本。
    /// </summary>
    private string GetPhaseText()
    {
        if (gameStateMachine == null) return "N/A";
        return gameStateMachine.Current.ToString();
    }

    /// <summary>
    /// 获取夜袭波次文本。
    /// </summary>
    private string GetWaveText()
    {
        if (nightAssaultSystem == null) return "N/A";
        return $"{nightAssaultSystem.CurrentWave}/{nightAssaultSystem.TotalWaves}";
    }

    /// <summary>
    /// 获取指定道具在背包中的数量。
    /// </summary>
    private int GetInventoryCount(string itemId)
    {
        if (inventoryService == null) return 0;
        return inventoryService.CountItem(itemId);
    }

    /// <summary>
    /// 格式化数值显示。
    /// </summary>
    private string GetFloat(float value)
    {
        return value.ToString("F1");
    }
}
