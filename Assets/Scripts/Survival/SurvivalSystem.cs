using UnityEngine;
using Sirenix.OdinInspector;
using Gameplay.Combat;

/// <summary>
/// 生存指标系统：负责饥饿、口渴、疲劳、感染与属性惩罚计算。
/// </summary>
public class SurvivalSystem : MonoBehaviour, IDamageable
{
    /// <summary>
    /// survival_rules.json 的 AOT 解析结构。
    /// 字段名必须与 JSON 一致；正式从 BootScene 启动时会覆盖下方 Inspector 兜底值。
    /// </summary>
    [System.Serializable]
    private sealed class SurvivalRulesConfig
    {
        public int schemaVersion = 1;
        public string balanceVersion = "survival-v1";
        public float maxHunger = 100f;
        public float maxThirst = 100f;
        public float maxFatigue = 100f;
        public float maxHp = 100f;
        public bool decayOnlyDuringRun = true;
        public bool decayDuringNightAssault = true;
        public float hungerDecayPerMinute = 1f;
        public float thirstDecayPerMinute = 1.25f;
        public float fatigueDecayPerMinute = 0.625f;
        public float warningThreshold = 30f;
        public float criticalThreshold = 10f;
        public float hpLossPerMinuteWhenHungry = 3f;
        public float hpLossPerMinuteWhenThirsty = 5f;
        public float hpLossPerMinuteWhenInfected = 2f;
    }

    // 传给热更新层的 Tick 参数缓冲区，固定复用以避免 Update 中产生 GC。
    // 各位置含义统一定义在 HotUpdateSurvivalBridge.Setting* 常量中，不要在这里直接写数字下标。
    private readonly float[] hotUpdateTickSettings =
        new float[HotUpdateSurvivalBridge.SettingLength];

    [Header("Current Stats（当前状态）")]
    [PropertyTooltip("当前饥饿值。0 表示极度饥饿，达到上限表示饱腹。")]
    [ProgressBar(0, 100)] public float hunger = 100f;
    [PropertyTooltip("当前口渴值。0 表示严重缺水，达到上限表示水分充足。")]
    [ProgressBar(0, 100)] public float thirst = 100f;
    [PropertyTooltip("当前疲劳值。0 表示精疲力竭，达到上限表示精力充足。")]
    [ProgressBar(0, 100)] public float fatigue = 100f;
    [PropertyTooltip("当前生命值。降到 0 将导致角色死亡。")]
    [ProgressBar(0, 100)] public float hp = 100f;
    [PropertyTooltip("是否处于感染状态。感染会持续掉血。")]
    public bool infected;

    [Header("Maximum Stats（属性上限，正式启动时由 JSON 覆盖）")]
    [PropertyTooltip("饥饿值上限。HUD 填充比例和恢复操作都使用此值。")]
    [Min(1f)] [SerializeField] private float maxHunger = 100f;
    [PropertyTooltip("口渴值上限。HUD 填充比例和恢复操作都使用此值。")]
    [Min(1f)] [SerializeField] private float maxThirst = 100f;
    [PropertyTooltip("疲劳值上限。HUD 填充比例和恢复操作都使用此值。")]
    [Min(1f)] [SerializeField] private float maxFatigue = 100f;
    [PropertyTooltip("生命值上限。受到伤害和恢复生命时不会超过此值。")]
    [Min(1f)] [SerializeField] private float maxHp = 100f;

    [Header("Decay（每分钟自然流逝，正式启动时由 JSON 覆盖）")]
    [PropertyTooltip("是否只在突围、搜刮等局内阶段流逝生存属性。开启后，庇护所与结算阶段不会自动衰减。")]
    [SerializeField] private bool decayOnlyDuringRun = true;
    [PropertyTooltip("夜袭阶段是否也流逝生存属性。")]
    [SerializeField] private bool decayDuringNightAssault = true;
    [PropertyTooltip("主流程状态机。为空时运行时自动查找。")]
    [SerializeField] private GameStateMachine gameStateMachine;
    [PropertyTooltip("每分钟减少的饥饿值。默认 1 与旧配置的平均速度一致。")]
    [Min(0f)] [SerializeField] private float hungerDecayPerMinute = 1f;
    [PropertyTooltip("每分钟减少的口渴值。默认 1.25 与旧配置的平均速度一致。")]
    [Min(0f)] [SerializeField] private float thirstDecayPerMinute = 1.25f;
    [PropertyTooltip("每分钟减少的疲劳值。默认 0.625 与旧配置的平均速度一致。")]
    [Min(0f)] [SerializeField] private float fatigueDecayPerMinute = 0.625f;

    [Header("Penalty Threshold（正式启动时由 JSON 覆盖）")]
    [PropertyTooltip("警告阈值。低于该值会触发属性减益。")]
    [SerializeField] private float warningThreshold = 30f;
    [PropertyTooltip("危险阈值。低于该值会触发更严重后果。")]
    [SerializeField] private float criticalThreshold = 10f;

    [Header("Penalty Rates（正式启动时由 JSON 覆盖）")]
    [PropertyTooltip("饥饿处于危险状态时，每分钟生命损失。")]
    [SerializeField] private float hpLossPerMinuteWhenHungry = 3f;
    [PropertyTooltip("口渴处于危险状态时，每分钟生命损失。")]
    [SerializeField] private float hpLossPerMinuteWhenThirsty = 5f;
    [PropertyTooltip("感染状态下，每分钟生命损失。")]
    [SerializeField] private float hpLossPerMinuteWhenInfected = 2f;

    public float MoveSpeedMultiplier { get; private set; } = 1f;
    public float DamageMultiplier { get; private set; } = 1f;
    public float StaminaMultiplier { get; private set; } = 1f;
    public bool CanUseChargeAttack { get; private set; } = true;
    public float MaxHunger => maxHunger;
    public float MaxThirst => maxThirst;
    public float MaxFatigue => maxFatigue;
    public float MaxHp => maxHp;
    public float HungerNormalized => Normalize(hunger, maxHunger);
    public float ThirstNormalized => Normalize(thirst, maxThirst);
    public float FatigueNormalized => Normalize(fatigue, maxFatigue);
    public float HpNormalized => Normalize(hp, maxHp);

    private void Awake()
    {
        if (gameStateMachine == null) gameStateMachine = FindObjectOfType<GameStateMachine>();
        ApplySurvivalRulesConfig();
        ClampAllStats();
        RecalculateModifiers();
    }

    /// <summary>
    /// 应用 Boot 阶段由 YooAsset 加载的生存平衡配置。
    /// 编辑器直接打开业务场景时，会从项目路径读取同一份 JSON；读取失败则保留 Inspector 兜底值。
    /// </summary>
    private void ApplySurvivalRulesConfig()
    {
        string json = GameContentService.SurvivalRulesJson;

#if UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(json))
        {
            TextAsset editorAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/GameRes/Config/survival_rules.json");
            if (editorAsset != null)
                json = editorAsset.text;
        }
#endif

        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning("未加载生存规则配置，将使用 SurvivalSystem Inspector 兜底值。");
            return;
        }

        SurvivalRulesConfig config;
        try
        {
            config = JsonUtility.FromJson<SurvivalRulesConfig>(json);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("解析 survival_rules.json 失败，将使用 Inspector 兜底值：\n" + exception);
            return;
        }

        if (config == null || config.schemaVersion != 1)
        {
            Debug.LogError(
                $"不支持的生存配置版本：{config?.schemaVersion ?? 0}，将使用 Inspector 兜底值。");
            return;
        }

        maxHunger = Mathf.Max(1f, config.maxHunger);
        maxThirst = Mathf.Max(1f, config.maxThirst);
        maxFatigue = Mathf.Max(1f, config.maxFatigue);
        maxHp = Mathf.Max(1f, config.maxHp);
        decayOnlyDuringRun = config.decayOnlyDuringRun;
        decayDuringNightAssault = config.decayDuringNightAssault;
        hungerDecayPerMinute = Mathf.Max(0f, config.hungerDecayPerMinute);
        thirstDecayPerMinute = Mathf.Max(0f, config.thirstDecayPerMinute);
        fatigueDecayPerMinute = Mathf.Max(0f, config.fatigueDecayPerMinute);
        warningThreshold = Mathf.Clamp(config.warningThreshold, 0f, 100f);
        criticalThreshold = Mathf.Clamp(config.criticalThreshold, 0f, warningThreshold);
        hpLossPerMinuteWhenHungry = Mathf.Max(0f, config.hpLossPerMinuteWhenHungry);
        hpLossPerMinuteWhenThirsty = Mathf.Max(0f, config.hpLossPerMinuteWhenThirsty);
        hpLossPerMinuteWhenInfected = Mathf.Max(0f, config.hpLossPerMinuteWhenInfected);

        Debug.Log(
            $"生存规则配置已应用：{config.balanceVersion}，" +
            $"饥饿/口渴/精力衰减={hungerDecayPerMinute}/" +
            $"{thirstDecayPerMinute}/{fatigueDecayPerMinute} 每分钟");
    }

    private void OnValidate()
    {
        maxHunger = Mathf.Max(1f, maxHunger);
        maxThirst = Mathf.Max(1f, maxThirst);
        maxFatigue = Mathf.Max(1f, maxFatigue);
        maxHp = Mathf.Max(1f, maxHp);
        warningThreshold = Mathf.Clamp(warningThreshold, 0f, 100f);
        criticalThreshold = Mathf.Clamp(criticalThreshold, 0f, warningThreshold);
        ClampAllStats();
        RecalculateModifiers();
    }

    /// <summary>
    /// 每帧推进生存值衰减与惩罚刷新。
    /// </summary>
    private void Update()
    {
        var dt = Time.deltaTime;
        bool shouldAdvance = ShouldAdvanceSurvival();
        if (!TryTickHotUpdateRules(dt, shouldAdvance))
        {
            if (shouldAdvance)
            {
                TickDecay(dt);
                TickPenaltyHp(dt);
            }
            RecalculateModifiers();
        }
        EventBus.Publish(new SurvivalTickEvent(dt));
    }

    /// <summary>
    /// 调用 HotUpdate.dll 中的生存数值规则。返回 false 时由当前客户端内置算法兜底。
    /// </summary>
    private bool TryTickHotUpdateRules(float deltaTime, bool shouldAdvance)
    {
        hotUpdateTickSettings[HotUpdateSurvivalBridge.SettingDeltaTime] = deltaTime;
        hotUpdateTickSettings[HotUpdateSurvivalBridge.SettingAdvanceSurvival] =
            shouldAdvance ? 1f : 0f;
        hotUpdateTickSettings[HotUpdateSurvivalBridge.SettingHungerDecay] = hungerDecayPerMinute;
        hotUpdateTickSettings[HotUpdateSurvivalBridge.SettingThirstDecay] = thirstDecayPerMinute;
        hotUpdateTickSettings[HotUpdateSurvivalBridge.SettingFatigueDecay] = fatigueDecayPerMinute;
        hotUpdateTickSettings[HotUpdateSurvivalBridge.SettingWarningThreshold] = warningThreshold;
        hotUpdateTickSettings[HotUpdateSurvivalBridge.SettingCriticalThreshold] = criticalThreshold;
        hotUpdateTickSettings[HotUpdateSurvivalBridge.SettingHungryHpLoss] = hpLossPerMinuteWhenHungry;
        hotUpdateTickSettings[HotUpdateSurvivalBridge.SettingThirstyHpLoss] = hpLossPerMinuteWhenThirsty;
        hotUpdateTickSettings[HotUpdateSurvivalBridge.SettingInfectedHpLoss] = hpLossPerMinuteWhenInfected;
        return HotUpdateSurvivalBridge.TryTick(this, hotUpdateTickSettings);
    }

    /// <summary>
    /// 由桥接器写回热更新规则结果。数组布局属于已发布协议，只在桥接层内部使用。
    /// </summary>
    internal void ApplyHotUpdateState(float[] state)
    {
        if (state == null || state.Length < HotUpdateSurvivalBridge.StateLength)
            return;

        hunger = Mathf.Clamp(state[HotUpdateSurvivalBridge.StateHunger], 0f, maxHunger);
        thirst = Mathf.Clamp(state[HotUpdateSurvivalBridge.StateThirst], 0f, maxThirst);
        fatigue = Mathf.Clamp(state[HotUpdateSurvivalBridge.StateFatigue], 0f, maxFatigue);
        hp = Mathf.Clamp(state[HotUpdateSurvivalBridge.StateHp], 0f, maxHp);
        infected = state[HotUpdateSurvivalBridge.StateInfected] > 0.5f;
        MoveSpeedMultiplier = Mathf.Max(
            0f,
            state[HotUpdateSurvivalBridge.StateMoveSpeedMultiplier]);
        DamageMultiplier = Mathf.Max(
            0f,
            state[HotUpdateSurvivalBridge.StateDamageMultiplier]);
        StaminaMultiplier = Mathf.Max(
            0f,
            state[HotUpdateSurvivalBridge.StateStaminaMultiplier]);
        CanUseChargeAttack = state[HotUpdateSurvivalBridge.StateCanUseChargeAttack] > 0.5f;
    }

    /// <summary>
    /// 消耗食物恢复饥饿值。
    /// </summary>
    public void Eat(float amount)
    {
        hunger = Mathf.Clamp(hunger + amount, 0f, maxHunger);
    }

    /// <summary>
    /// 消耗饮水恢复口渴值。
    /// </summary>
    public void Drink(float amount)
    {
        thirst = Mathf.Clamp(thirst + amount, 0f, maxThirst);
    }

    /// <summary>
    /// 休息恢复疲劳值。
    /// </summary>
    public void Sleep(float amount)
    {
        fatigue = Mathf.Clamp(fatigue + amount, 0f, maxFatigue);
    }

    /// <summary>
    /// 使用药物解除感染状态。
    /// </summary>
    public void CureInfection()
    {
        infected = false;
    }

    /// <summary>
    /// 设置感染状态，供伤口、环境和药物系统统一调用。
    /// </summary>
    public void SetInfected(bool value)
    {
        infected = value;
    }

    /// <summary>
    /// 恢复生命值，结果不会超过生命上限。
    /// </summary>
    public void Heal(float amount)
    {
        hp = Mathf.Clamp(hp + Mathf.Max(0f, amount), 0f, maxHp);
    }

    public void ApplyDamage(float amount)
    {
        hp = Mathf.Clamp(hp - Mathf.Max(0f, amount), 0f, maxHp);
    }

    private void TickDecay(float dt)
    {
        var minutes = Mathf.Max(0f, dt) / 60f;
        hunger = Mathf.Clamp(hunger - hungerDecayPerMinute * minutes, 0f, maxHunger);
        thirst = Mathf.Clamp(thirst - thirstDecayPerMinute * minutes, 0f, maxThirst);
        fatigue = Mathf.Clamp(fatigue - fatigueDecayPerMinute * minutes, 0f, maxFatigue);
    }

    private void TickPenaltyHp(float dt)
    {
        var hpLossPerMinute = 0f;
        if (hunger <= criticalThreshold) hpLossPerMinute += hpLossPerMinuteWhenHungry;
        if (thirst <= criticalThreshold) hpLossPerMinute += hpLossPerMinuteWhenThirsty;
        if (infected) hpLossPerMinute += hpLossPerMinuteWhenInfected;
        ApplyDamage(hpLossPerMinute * Mathf.Max(0f, dt) / 60f);
    }

    private void RecalculateModifiers()
    {
        MoveSpeedMultiplier = thirst <= warningThreshold ? 0.7f : 1f;
        DamageMultiplier = hunger <= warningThreshold ? 0.8f : 1f;
        StaminaMultiplier = fatigue <= warningThreshold ? 0.5f : 1f;
        CanUseChargeAttack = fatigue > criticalThreshold;
    }

    /// <summary>
    /// 判断当前流程阶段是否允许自然流逝。
    /// </summary>
    private bool ShouldAdvanceSurvival()
    {
        if (!decayOnlyDuringRun || gameStateMachine == null) return true;
        var phase = gameStateMachine.Current;
        return phase == GamePhase.Breakout ||
               phase == GamePhase.Raid ||
               (decayDuringNightAssault && phase == GamePhase.NightAssault);
    }

    private void ClampAllStats()
    {
        hunger = Mathf.Clamp(hunger, 0f, maxHunger);
        thirst = Mathf.Clamp(thirst, 0f, maxThirst);
        fatigue = Mathf.Clamp(fatigue, 0f, maxFatigue);
        hp = Mathf.Clamp(hp, 0f, maxHp);
    }

    private static float Normalize(float current, float maximum)
    {
        return maximum <= 0f ? 0f : Mathf.Clamp01(current / maximum);
    }
}
