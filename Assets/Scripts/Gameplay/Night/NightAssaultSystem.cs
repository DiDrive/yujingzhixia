using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>
/// 夜袭系统：负责夜袭触发、波次推进与结算奖励。
/// </summary>
public class NightAssaultSystem : MonoBehaviour
{
    [PropertyTooltip("夜袭开始前的预警时长（秒）。")]
    [SerializeField] private int warningLeadSeconds = 30;
    [PropertyTooltip("本次夜袭总波次数。")]
    [SerializeField] private int totalWaves = 2;
    [PropertyTooltip("夜袭胜利后发放奖励的抽取次数。")]
    [SerializeField] private int victoryRewardRollCount = 3;
    /// <summary>
    /// 夜袭奖励箱体类型。
    /// </summary>
    [PropertyTooltip("夜袭胜利时使用的奖励箱体类型。")]
    [SerializeField] private LootChestType rewardChestType = LootChestType.Resource;
    /// <summary>
    /// 掉落池服务，用于生成夜袭奖励。
    /// </summary>
    [PropertyTooltip("奖励掉落生成器引用。")]
    [SerializeField] private LootSpawner lootSpawner;
    /// <summary>
    /// 库存服务，用于存储夜袭奖励。
    /// </summary>
    [PropertyTooltip("奖励发放目标背包服务。")]
    [SerializeField] private InventoryService inventoryService;
    /// <summary>
    /// 游戏状态机，用于触发夜袭开始和结束事件。
    /// </summary>
    [PropertyTooltip("用于切换夜袭与结算阶段的状态机。")]
    [SerializeField] private GameStateMachine gameStateMachine;

    public bool IsRunning { get; private set; }
    public int CurrentWave { get; private set; }
    public int TotalWaves => totalWaves;

    /// <summary>
    /// 开始夜袭阶段并发出预警事件。
    /// </summary>
    public void StartAssault()
    {
        if (IsRunning) return;
        IsRunning = true;
        CurrentWave = 1;
        if (gameStateMachine != null) gameStateMachine.StartNightAssault();
        EventBus.Publish(new NightAssaultStartedEvent(Mathf.Max(0, warningLeadSeconds)));
    }

    /// <summary>
    /// 推进到下一波夜袭，达到上限后保持末波。
    /// </summary>
    public void AdvanceWave()
    {
        if (!IsRunning) return;
        CurrentWave = Mathf.Min(CurrentWave + 1, Mathf.Max(1, totalWaves));
    }

    /// <summary>
    /// 根据结果结束夜袭并发放奖励。
    /// </summary>
    public void ResolveAssault(NightAssaultResult result)
    {
        if (!IsRunning) return;
        IsRunning = false;

        var rewardCount = 0;
        if (result == NightAssaultResult.Victory && lootSpawner != null && inventoryService != null)
        {
            var granted = lootSpawner.GrantLoot(inventoryService, rewardChestType, Mathf.Max(1, victoryRewardRollCount));
            rewardCount = granted ? victoryRewardRollCount : 0;
        }

        if (gameStateMachine != null) gameStateMachine.EndCycleToResult();
        EventBus.Publish(new NightAssaultResolvedEvent(result, rewardCount));
    }
}
