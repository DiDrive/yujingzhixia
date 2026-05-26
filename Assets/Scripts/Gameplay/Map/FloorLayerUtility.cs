using UnityEngine;

/// <summary>
/// 多楼层相关的运行时辅助方法：
/// 1. 统一 Actor_Fx / Wall_Fx 的命名；
/// 2. 读取对象当前楼层；
/// 3. 生成当前楼层可用的 LayerMask；
/// 4. 判断玩家/僵尸是否属于敌对阵营。
/// </summary>
public static class FloorLayerUtility
{
    public const string ActorLayerPrefix = "Actor_F";
    public const string WallLayerPrefix = "Wall_F";

    public static string GetActorSortingLayerName(int floorIndex)
    {
        return $"{ActorLayerPrefix}{Mathf.Max(0, floorIndex)}";
    }

    public static string GetActorLayerName(int floorIndex)
    {
        return GetActorSortingLayerName(floorIndex);
    }

    public static string GetWallLayerName(int floorIndex)
    {
        return $"{WallLayerPrefix}{Mathf.Max(0, floorIndex)}";
    }

    public static int GetActorLayer(int floorIndex)
    {
        return LayerMask.NameToLayer(GetActorLayerName(floorIndex));
    }

    public static int GetWallLayer(int floorIndex)
    {
        return LayerMask.NameToLayer(GetWallLayerName(floorIndex));
    }

    public static int GetFloorIndex(Transform target)
    {
        if (target == null) return -1;
        var state = target.GetComponentInParent<FloorSortingState>();
        return state != null ? state.ActiveFloorIndex : -1;
    }

    public static bool AreOnSameFloor(Transform a, Transform b)
    {
        var floorA = GetFloorIndex(a);
        var floorB = GetFloorIndex(b);
        if (floorA < 0 || floorB < 0) return true;
        return floorA == floorB;
    }

    public static LayerMask GetActorHitMaskForCurrentFloor(Transform source, LayerMask baseMask)
    {
        var value = baseMask.value;
        var floorIndex = GetFloorIndex(source);
        if (floorIndex < 0) return value;

        var actorLayer = GetActorLayer(floorIndex);
        if (actorLayer >= 0) value |= 1 << actorLayer;
        return value;
    }

    public static LayerMask GetMovementCollisionMaskForCurrentFloor(Transform source, LayerMask baseMask)
    {
        var value = baseMask.value;
        var floorIndex = GetFloorIndex(source);
        if (floorIndex < 0) return value;

        var wallLayer = GetWallLayer(floorIndex);
        if (wallLayer >= 0) value |= 1 << wallLayer;
        return value;
    }

    public static LayerMask GetSightBlockMaskForCurrentFloor(Transform source, LayerMask baseMask)
    {
        var value = baseMask.value;
        var floorIndex = GetFloorIndex(source);
        if (floorIndex < 0) return value;

        var wallLayer = GetWallLayer(floorIndex);
        if (wallLayer >= 0) value |= 1 << wallLayer;
        return value;
    }

    public static LayerMask GetWallOnlyMaskForCurrentFloor(Transform source)
    {
        var value = 0;
        var legacyWallLayer = LayerMask.NameToLayer("Wall");
        if (legacyWallLayer >= 0) value |= 1 << legacyWallLayer;

        var floorIndex = GetFloorIndex(source);
        if (floorIndex >= 0)
        {
            var wallLayer = GetWallLayer(floorIndex);
            if (wallLayer >= 0) value |= 1 << wallLayer;
        }

        return value;
    }

    public static LayerMask GetProjectileHitMaskForCurrentFloor(Transform source, LayerMask baseMask)
    {
        var value = GetActorHitMaskForCurrentFloor(source, baseMask).value;
        var floorIndex = GetFloorIndex(source);
        if (floorIndex < 0) return value;

        var wallLayer = GetWallLayer(floorIndex);
        if (wallLayer >= 0) value |= 1 << wallLayer;
        return value;
    }

    public static bool AreHostile(Transform source, Transform target)
    {
        if (source == null || target == null) return false;

        var sourceIsPlayer = source.GetComponentInParent<PlayerCombatController2D>() != null;
        var targetIsPlayer = target.GetComponentInParent<PlayerCombatController2D>() != null;
        var sourceIsZombie = source.GetComponentInParent<ZombieController2D>() != null;
        var targetIsZombie = target.GetComponentInParent<ZombieController2D>() != null;

        if (sourceIsPlayer && targetIsZombie) return true;
        if (sourceIsZombie && targetIsPlayer) return true;
        return false;
    }

    public static bool IsWallLayer(int layer)
    {
        if (layer < 0) return false;
        var layerName = LayerMask.LayerToName(layer);
        return layerName == "Wall" || layerName.StartsWith(WallLayerPrefix);
    }
}
