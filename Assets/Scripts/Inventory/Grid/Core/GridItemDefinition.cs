using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;

/// <summary>物品品质，同时决定搜索容器中揭示该物品所需的时间档位。</summary>
public enum GridItemQuality
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4
}

/// <summary>
/// 单条物品使用效果。Type 使用可读字符串，方便直接维护 JSON 表。
/// 当前支持 Health、Hunger、Thirst、Fatigue、CureInfection 和 Infection。
/// </summary>
[Serializable]
public struct GridItemUseEffect
{
    [PropertyTooltip("效果类型：Health、Hunger、Thirst、Fatigue、CureInfection 或 Infection。")]
    public string type;
    [PropertyTooltip("效果数值。恢复类为正数、降低类为负数；感染开关类会忽略此值。")]
    public float value;
}

/// <summary>可被多种物品复用的消耗效果档案。</summary>
[Serializable]
public struct GridItemUseProfile
{
    [PropertyTooltip("使用档案唯一 ID，例如 fruit_small。")]
    public string profileId;
    [PropertyTooltip("该档案默认使用时间。")]
    public float useDuration;
    [PropertyTooltip("应用该档案时自动补充到物品上的标签。")]
    public List<string> itemTagsToAdd;
    [PropertyTooltip("该档案包含的生存属性效果。")]
    public List<GridItemUseEffect> useEffects;
}

/// <summary>把物品 ID 映射到一个可复用的使用档案。</summary>
[Serializable]
public struct GridItemUseProfileAssignment
{
    public string itemId;
    public string profileId;
}

/// <summary>由 JSON 或 Inspector 读取的网格物品静态定义。</summary>
[Serializable]
public struct GridItemDefinition
{
    [PropertyTooltip("物品唯一标识。")]
    public string itemId;
    [PropertyTooltip("UI 中显示的物品名称。")]
    public string displayName;
    [PropertyTooltip("Resources 下的物品图标路径，不包含扩展名。例如 texture/food_canned。")]
    public string iconResourcePath;
    [PropertyTooltip("物品占位宽度。")]
    public int width;
    [PropertyTooltip("物品占位高度。")]
    public int height;
    [PropertyTooltip("是否可旋转。")]
    public bool canRotate;
    [PropertyTooltip("单实例堆叠上限。")]
    public int maxStack;
    [PropertyTooltip("物品标签列表。")]
    public List<string> itemTags;
    [PropertyTooltip("物品重量。")]
    public float weight;
    [PropertyTooltip("物品品质。决定搜索容器中逐件鉴定该物品所需的时间。")]
    public GridItemQuality quality;
    [PropertyTooltip("是否允许通过背包的“使用”按钮消耗。")]
    public bool usable;
    [PropertyTooltip("可选的通用使用档案 ID。运行时会从同一 JSON 的 Use Profiles 解析。")]
    public string useProfile;
    [PropertyTooltip("完成使用需要的秒数。0 表示立即生效。")]
    public float useDuration;
    [PropertyTooltip("使用成功后施加到玩家生存属性上的效果列表。")]
    public List<GridItemUseEffect> useEffects;
}
