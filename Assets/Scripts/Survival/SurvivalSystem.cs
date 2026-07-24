using UnityEngine;
using Sirenix.OdinInspector;
using Gameplay.Combat;

/// <summary>
/// 生存指标系统：负责饥饿、口渴、疲劳、感染与属性惩罚计算。
/// </summary>
public class SurvivalSystem : MonoBehaviour, IDamageable
{
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

    [Header("Maximum Stats（属性上限）")]
    [PropertyTooltip("饥饿值上限。HUD 填充比例和恢复操作都使用此值。")]
    [Min(1f)] [SerializeField] private float maxHunger = 100f;
    [PropertyTooltip("口渴值上限。HUD 填充比例和恢复操作都使用此值。")]
    [Min(1f)] [SerializeField] private float maxThirst = 100f;
    [PropertyTooltip("疲劳值上限。HUD 填充比例和恢复操作都使用此值。")]
    [Min(1f)] [SerializeField] private float maxFatigue = 100f;
    [PropertyTooltip("生命值上限。受到伤害和恢复生命时不会超过此值。")]
    [Min(1f)] [SerializeField] private float maxHp = 100f;

    [Header("Decay（每分钟自然流逝）")]
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

    [Header("Penalty Threshold")]
    [PropertyTooltip("警告阈值。低于该值会触发属性减益。")]
    [SerializeField] private float warningThreshold = 30f;
    [PropertyTooltip("危险阈值。低于该值会触发更严重后果。")]
    [SerializeField] private float criticalThreshold = 10f;

    [Header("Penalty Rates")]
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
        ClampAllStats();
        RecalculateModifiers();
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
        if (ShouldAdvanceSurvival())
        {
            TickDecay(dt);
            TickPenaltyHp(dt);
        }
        RecalculateModifiers();
        EventBus.Publish(new SurvivalTickEvent(dt));
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
