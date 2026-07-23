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
}
