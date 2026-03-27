using System;
using System.Collections.Generic;
/// <summary>
/// 网格物品放置求解器：负责在网格上查找物品放置位置。
/// </summary>
public static class GridPlacementSolver 
{
    /// <summary>
    /// 检查指定位置是否可以放置物品。
    /// </summary>
    /// <param name="occupancyMap">网格占用地图。</param>
    /// <param name="originRow">左上角行索引。</param>
    /// <param name="originColumn">左上角列索引。</param>
    /// <param name="itemDefinition">物品定义。</param>
    /// <param name="rotation">物品旋转。</param>
    /// <param name="ignoreInstanceId">要忽略的物品实例ID，默认为null。</param>
    /// <returns>是否可以放置。</returns>
    public static bool CanPlace(
        GridOccupancyMap occupancyMap,
        int originRow,
        int originColumn,
        GridItemDefinition itemDefinition,
        GridItemRotation rotation,
        string ignoreInstanceId = null)
    {
        if (occupancyMap == null) return false;
        if (!TryGetFootprint(itemDefinition, rotation, out var width, out var height)) return false;
        return occupancyMap.IsAreaEmpty(originRow, originColumn, width, height, ignoreInstanceId);
    }

    /// <summary>
    /// 查找物品第一个符合要求的放置位置。
    /// </summary>
    /// <param name="occupancyMap">网格占用地图。</param>
    /// <param name="itemDefinition">物品定义。</param>
    /// <param name="placement">放置位置。</param>
    /// <param name="allowRotate">是否允许旋转，默认为true。</param>
    /// <param name="ignoreInstanceId">要忽略的物品实例ID，默认为null。</param>
    /// <returns>是否找到符合要求的放置位置。</returns>
    public static bool FindFirstFit(
        GridOccupancyMap occupancyMap,
        GridItemDefinition itemDefinition,
        out GridPlacement placement,
        bool allowRotate = true,
        string ignoreInstanceId = null)
    {
        placement = GridPlacement.Invalid;
        if (occupancyMap == null) return false;

        var rotations = GetRotationOrder(itemDefinition, allowRotate);
        for (var i = 0; i < rotations.Count; i++)
        {
            var rotation = rotations[i];
            if (!TryGetFootprint(itemDefinition, rotation, out var width, out var height)) continue;

            for (var row = 0; row <= occupancyMap.Rows - height; row++)
            {
                for (var column = 0; column <= occupancyMap.Columns - width; column++)
                {
                    if (!occupancyMap.IsAreaEmpty(row, column, width, height, ignoreInstanceId)) continue;
                    placement = new GridPlacement(row, column, rotation);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 尝试获取物品在网格上的占用区域尺寸。
    /// </summary>
    /// <param name="itemDefinition">物品定义。</param>
    /// <param name="rotation">物品旋转。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    /// <returns>是否成功获取尺寸。</returns>
    public static bool TryGetFootprint(GridItemDefinition itemDefinition, GridItemRotation rotation, out int width, out int height)
    {
        width = Math.Max(0, itemDefinition.width);
        height = Math.Max(0, itemDefinition.height);
        if (width == 0 || height == 0) return false;

        if (rotation == GridItemRotation.Deg90)
        {
            if (!itemDefinition.canRotate) return false;
            var temp = width;
            width = height;
            height = temp;
        }

        return true;
    }

    /// <summary>
    /// 获取物品旋转顺序。
    /// </summary>
    /// <param name="itemDefinition">物品定义。</param>
    /// <param name="allowRotate">是否允许旋转，默认为true。</param>
    /// <returns>旋转顺序列表。</returns>
    private static List<GridItemRotation> GetRotationOrder(GridItemDefinition itemDefinition, bool allowRotate)
    {
        var result = new List<GridItemRotation> { GridItemRotation.Deg0 };
        if (!allowRotate || !itemDefinition.canRotate) return result;
        result.Add(GridItemRotation.Deg90);
        return result;
    }
}

/// <summary>
/// 网格物品放置位置：包含行索引、列索引和旋转角度。
/// </summary>
public readonly struct GridPlacement
{
    public readonly int row;
    public readonly int column;
    public readonly GridItemRotation rotation;

    public bool IsValid => row >= 0 && column >= 0;

    public GridPlacement(int row, int column, GridItemRotation rotation)
    {
        this.row = row;
        this.column = column;
        this.rotation = rotation;
    }

    public static GridPlacement Invalid => new(-1, -1, GridItemRotation.Deg0);
}
