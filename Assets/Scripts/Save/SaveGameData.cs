using System;
using System.Collections.Generic;

/// <summary>
/// AOT 层拥有的稳定存档协议。热更新程序集不得直接把运行时类型写进磁盘存档。
/// </summary>
[Serializable]
public sealed class SaveGameData
{
    public const int CurrentSchemaVersion = 1;

    public int schemaVersion = CurrentSchemaVersion;
    public string savedAtUtc;
    public string applicationVersion;
    public string saveReason;
    public int gamePhase;
    public SurvivalSaveData survival = new();
    public GridInventorySaveData inventory = new();
    public bool shelterHasPower = true;
    public List<SearchableLootContainerState> searchableLoot = new();
    public List<string> discoveredMarkerIds = new();
}

[Serializable]
public sealed class SurvivalSaveData
{
    public float hunger;
    public float thirst;
    public float fatigue;
    public float hp;
    public bool infected;
}

[Serializable]
public sealed class GridInventorySaveData
{
    public List<GridInventoryContainerSaveData> containers = new();
}

[Serializable]
public sealed class GridInventoryContainerSaveData
{
    public string containerId;
    public int rows;
    public int columns;
    public GridContainerOwnerType ownerType;
    public int unlockedCellCount;
    public List<GridInventoryItemSaveData> items = new();
}

[Serializable]
public sealed class GridInventoryItemSaveData
{
    public string itemId;
    public int stackCount;
    public GridItemRotation rotation;
    public int row;
    public int column;
}
