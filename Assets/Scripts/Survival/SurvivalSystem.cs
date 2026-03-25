using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>
/// 生存指标系统：负责饥饿、口渴、疲劳、感染与属性惩罚计算。
/// </summary>
public class SurvivalSystem : MonoBehaviour
{
    [Header("Runtime Stats")]
    [PropertyTooltip("当前饥饿值。越低惩罚越重。")]
    [ProgressBar(0, 100)] public float hunger = 100f;
    [PropertyTooltip("当前口渴值。越低惩罚越重。")]
    [ProgressBar(0, 100)]public float thirst = 100f;
    [PropertyTooltip("当前疲劳值。越低行动能力越差。")]
    [ProgressBar(0, 100)] public float fatigue = 100f;
    [PropertyTooltip("当前生命值。降到 0 将导致角色死亡。")]
    [ProgressBar(0, 100)] public float hp = 100f;
    [PropertyTooltip("是否处于感染状态。感染会持续掉血。")]
    public bool infected;

    [Header("Decay")]
    [PropertyTooltip("饥饿值每次衰减的触发间隔（秒）。")]
    [SerializeField] private float hungerIntervalSeconds = 480f;
    [PropertyTooltip("口渴值每次衰减的触发间隔（秒）。")]
    [SerializeField] private float thirstIntervalSeconds = 480f;
    [PropertyTooltip("疲劳值每次衰减的触发间隔（秒）。")]
    [SerializeField] private float fatigueIntervalSeconds = 1440f;
    [PropertyTooltip("每个饥饿衰减周期扣减的数值。")]
    [SerializeField] private float hungerDecayPerTick = 8f;
    [PropertyTooltip("每个口渴衰减周期扣减的数值。")]
    [SerializeField] private float thirstDecayPerTick = 10f;
    [PropertyTooltip("每个疲劳衰减周期扣减的数值。")]
    [SerializeField] private float fatigueDecayPerTick = 15f;

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

    private float hungerTimer;
    private float thirstTimer;
    private float fatigueTimer;
    private float hpPenaltyTimer;

    public float MoveSpeedMultiplier { get; private set; } = 1f;
    public float DamageMultiplier { get; private set; } = 1f;
    public float StaminaMultiplier { get; private set; } = 1f;
    public bool CanUseChargeAttack { get; private set; } = true;

    /// <summary>
    /// 每帧推进生存值衰减与惩罚刷新。
    /// </summary>
    private void Update()
    {
        var dt = Time.deltaTime;
        TickDecay(dt);
        TickPenaltyHp(dt);
        RecalculateModifiers();
        EventBus.Publish(new SurvivalTickEvent(dt));
    }

    /// <summary>
    /// 消耗食物恢复饥饿值。
    /// </summary>
    public void Eat(float amount)
    {
        hunger = Mathf.Clamp(hunger + amount, 0f, 100f);
    }

    /// <summary>
    /// 消耗饮水恢复口渴值。
    /// </summary>
    public void Drink(float amount)
    {
        thirst = Mathf.Clamp(thirst + amount, 0f, 100f);
    }

    /// <summary>
    /// 休息恢复疲劳值。
    /// </summary>
    public void Sleep(float amount)
    {
        fatigue = Mathf.Clamp(fatigue + amount, 0f, 100f);
    }

    /// <summary>
    /// 使用药物解除感染状态。
    /// </summary>
    public void CureInfection()
    {
        infected = false;
    }

    private void TickDecay(float dt)
    {
        hungerTimer += dt;
        thirstTimer += dt;
        fatigueTimer += dt;

        if (hungerTimer >= hungerIntervalSeconds)
        {
            hunger = Mathf.Clamp(hunger - hungerDecayPerTick, 0f, 100f);
            hungerTimer -= hungerIntervalSeconds;
        }

        if (thirstTimer >= thirstIntervalSeconds)
        {
            thirst = Mathf.Clamp(thirst - thirstDecayPerTick, 0f, 100f);
            thirstTimer -= thirstIntervalSeconds;
        }

        if (fatigueTimer >= fatigueIntervalSeconds)
        {
            fatigue = Mathf.Clamp(fatigue - fatigueDecayPerTick, 0f, 100f);
            fatigueTimer -= fatigueIntervalSeconds;
        }
    }

    private void TickPenaltyHp(float dt)
    {
        hpPenaltyTimer += dt;
        if (hpPenaltyTimer < 60f) return;
        hpPenaltyTimer -= 60f;

        if (hunger <= criticalThreshold) hp = Mathf.Clamp(hp - hpLossPerMinuteWhenHungry, 0f, 100f);
        if (thirst <= criticalThreshold) hp = Mathf.Clamp(hp - hpLossPerMinuteWhenThirsty, 0f, 100f);
        if (infected) hp = Mathf.Clamp(hp - hpLossPerMinuteWhenInfected, 0f, 100f);
    }

    private void RecalculateModifiers()
    {
        MoveSpeedMultiplier = thirst <= warningThreshold ? 0.7f : 1f;
        DamageMultiplier = hunger <= warningThreshold ? 0.8f : 1f;
        StaminaMultiplier = fatigue <= warningThreshold ? 0.5f : 1f;
        CanUseChargeAttack = fatigue > criticalThreshold;
    }
}
