using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;

public enum GridContainerOwnerType
{
    Player,
    WorldLoot,
    Storage,
    Corpse
}

[Serializable]
public struct GridContainerDefinition
{
    [PropertyTooltip("容器唯一标识。")]
    public string containerId;
    [PropertyTooltip("容器网格行数。")]
    public int rows;
    [PropertyTooltip("容器网格列数。")]
    public int columns;
    [PropertyTooltip("游戏开始时已解锁的格子数，按从上到下、从左到右的顺序解锁。0 或大于总格数表示全部解锁。")]
    public int initialUnlockedCellCount;
    [PropertyTooltip("是否允许嵌套容器。")]//是否允许套包
    public bool allowNestedContainer;
    [PropertyTooltip("允许放入的物品标签。为空时不限制。")]
    public List<string> allowedItemTags;
    [PropertyTooltip("容器归属。玩家物品统计和消耗只会读取 Player 容器。")]
    public GridContainerOwnerType ownerType;
}
