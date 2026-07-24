/// <summary>
/// 夜袭结算结果定义。
/// </summary>
public enum NightAssaultResult
{
    Victory,
    Defeat
}

/// <summary>
/// 供电状态变更事件。
/// </summary>
public readonly struct PowerStateChangedEvent
{
    public readonly bool IsPowered;

    public PowerStateChangedEvent(bool isPowered)
    {
        IsPowered = isPowered;
    }
}

/// <summary>
/// 夜袭开始事件。
/// </summary>
public readonly struct NightAssaultStartedEvent
{
    public readonly int WarningLeadSeconds;

    public NightAssaultStartedEvent(int warningLeadSeconds)
    {
        WarningLeadSeconds = warningLeadSeconds;
    }
}

/// <summary>
/// 夜袭结算事件。
/// </summary>
public readonly struct NightAssaultResolvedEvent
{
    public readonly NightAssaultResult Result;
    public readonly int RewardCount;

    public NightAssaultResolvedEvent(NightAssaultResult result, int rewardCount)
    {
        Result = result;
        RewardCount = rewardCount;
    }
}

/// <summary>
/// 掉落发放事件。
/// </summary>
public readonly struct LootGrantedEvent
{
    public readonly string TableId;
    public readonly int StackCount;

    public LootGrantedEvent(string tableId, int stackCount)
    {
        TableId = tableId;
        StackCount = stackCount;
    }
}

/// <summary>
/// 玩家完成一次地图撤离时发布，后续正式结算、存档和场景切换可以订阅该事件。
/// </summary>
public readonly struct ExtractionCompletedEvent
{
    public readonly string ExtractionId;
    public readonly string TargetId;

    public ExtractionCompletedEvent(string extractionId, string targetId)
    {
        ExtractionId = extractionId;
        TargetId = targetId;
    }
}
