using System;

[Serializable]
public sealed class GridOccupancyMap    // 网格占用地图，记录每个格子是否被占用以及占用的物品实例ID
{
    private readonly bool[,] occupied;// 记录每个格子是否被占用
    private readonly string[,] occupantInstanceIds;// 记录每个格子占用的物品实例ID

    public int Rows { get; }
    public int Columns { get; }

    /// <summary>
    /// 初始化网格占用地图。
    /// </summary>
    /// <param name="rows">行数。</param>
    /// <param name="columns">列数。</param>
    public GridOccupancyMap(int rows, int columns)
    {
        Rows = Math.Max(0, rows);
        Columns = Math.Max(0, columns);
        occupied = new bool[Rows, Columns];
        occupantInstanceIds = new string[Rows, Columns];
    }

    /// <summary>
    /// 检查指定格子是否在地图范围内。
    /// </summary>
    /// <param name="row">行索引。</param>
    /// <param name="column">列索引。</param>
    /// <returns>是否在范围内。</returns>
    public bool IsInside(int row, int column)
    {
        return row >= 0 && row < Rows && column >= 0 && column < Columns;
    }

    /// <summary>
    /// 检查指定区域是否在地图范围内。
    /// </summary>
    /// <param name="originRow">左上角行索引。</param>
    /// <param name="originColumn">左上角列索引。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    /// <returns>是否在范围内。</returns>
    public bool IsAreaInside(int originRow, int originColumn, int width, int height)
    {
        if (width <= 0 || height <= 0) return false;
        var maxRow = originRow + height - 1;
        var maxColumn = originColumn + width - 1;
        return IsInside(originRow, originColumn) && IsInside(maxRow, maxColumn);
    }

    /// <summary>
    /// 检查指定区域是否为空（所有格子未被占用）。
    /// </summary>
    /// <param name="originRow">左上角行索引。</param>
    /// <param name="originColumn">左上角列索引。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    /// <param name="ignoreInstanceId">要忽略的物品实例ID，默认为null。</param>
    /// <returns>是否为空。</returns>
    public bool IsAreaEmpty(int originRow, int originColumn, int width, int height, string ignoreInstanceId = null)
    {
        if (!IsAreaInside(originRow, originColumn, width, height)) return false;

        for (var row = originRow; row < originRow + height; row++)
        {
            for (var column = originColumn; column < originColumn + width; column++)
            {
                if (!occupied[row, column]) continue;
                if (!string.IsNullOrEmpty(ignoreInstanceId) && occupantInstanceIds[row, column] == ignoreInstanceId) continue;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 尝试填充指定区域为指定物品实例ID。
    /// </summary>
    /// <param name="originRow">左上角行索引。</param>
    /// <param name="originColumn">左上角列索引。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    /// <param name="instanceId">物品实例ID。</param>
    /// <param name="ignoreInstanceId">要忽略的物品实例ID，默认为null。</param>
    /// <returns>是否填充成功。</returns>
    public bool TryFillArea(int originRow, int originColumn, int width, int height, string instanceId, string ignoreInstanceId = null)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return false;
        if (!IsAreaEmpty(originRow, originColumn, width, height, ignoreInstanceId)) return false;

        for (var row = originRow; row < originRow + height; row++)
        {
            for (var column = originColumn; column < originColumn + width; column++)
            {
                occupied[row, column] = true;
                occupantInstanceIds[row, column] = instanceId;
            }
        }

        return true;
    }

    /// <summary>
    /// 清除指定物品实例占用的区域。
    /// </summary>
    /// <param name="instanceId">物品实例ID。</param>
    public void ClearAreaByInstance(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;

        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                if (occupantInstanceIds[row, column] != instanceId) continue;
                occupied[row, column] = false;
                occupantInstanceIds[row, column] = null;
            }
        }
    }

    public bool IsCellOccupied(int row, int column)
    {
        if (!IsInside(row, column)) return false;
        return occupied[row, column];
    }

    public string GetCellOccupantId(int row, int column)
    {
        if (!IsInside(row, column)) return null;
        return occupantInstanceIds[row, column];
    }
}
