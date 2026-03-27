using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;

[Serializable]
public struct GridItemDefinition// 网格物品定义
{
    [PropertyTooltip("物品唯一标识。")]
    public string itemId;
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
}
