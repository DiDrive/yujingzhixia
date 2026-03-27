using System;
using Sirenix.OdinInspector;

[Serializable]
public struct GridItemInstance // 网格物品实例
{
    [PropertyTooltip("实例唯一标识。")]
    public string instanceId;
    [PropertyTooltip("所属物品定义ID。")]
    public string itemId;
    [PropertyTooltip("当前堆叠数量。")]
    public int stackCount;
    [PropertyTooltip("旋转状态。")]
    public GridItemRotation rotation;
    [PropertyTooltip("所在容器ID。")]
    public string containerId;
    [PropertyTooltip("左上角起始行。")]
    public int originRow;
    [PropertyTooltip("左上角起始列。")]
    public int originColumn;
}
