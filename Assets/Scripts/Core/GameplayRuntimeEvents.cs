/// <summary>
/// 夜袭结算结果定义。
/// </summary>
public enum NightAssaultResult
{
    Victory,
    Defeat
}

/// <summary>
/// 摸金箱体类型定义。
/// </summary>
public enum LootChestType
{
    Survival,
    Weapon,
    Resource,
    Rare,
    Medical
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
    public readonly LootChestType ChestType;
    public readonly int StackCount;

    public LootGrantedEvent(LootChestType chestType, int stackCount)
    {
        ChestType = chestType;
        StackCount = stackCount;
    }
}
