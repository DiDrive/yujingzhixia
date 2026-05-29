using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider2D))]
public class BuildingVisibilityZone2D : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("要控制的建筑楼层显示控制器。通常拖挂在 Grid 上的 FloorVisibilityController。")]
    [SerializeField] private FloorVisibilityController visibilityController;

    [Header("Floor Filter")]
    [Tooltip("为 true 时：该室内区只在指定楼层范围内生效。适合二楼外扩区域，避免一楼站在楼外投影下也被判定为进楼。")]
    [SerializeField] private bool useFloorRange = true;
    [Tooltip("该区域生效的最低楼层。")]
    [SerializeField] private int minFloorIndex = 0;
    [Tooltip("该区域生效的最高楼层。")]
    [SerializeField] private int maxFloorIndex = 0;

    private readonly Dictionary<int, FloorSortingState> trackedPlayers = new();

    private void Reset()
    {
        var trigger = GetComponent<Collider2D>();
        if (trigger != null) trigger.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!TryGetPlayerState(other, out var state)) return;
        trackedPlayers[other.GetInstanceID()] = state;
        RefreshZoneState();
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!TryGetPlayerState(other, out var state)) return;
        trackedPlayers[other.GetInstanceID()] = state;
        RefreshZoneState();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;
        trackedPlayers.Remove(other.GetInstanceID());
        RefreshZoneState();
    }

    private void OnDisable()
    {
        if (visibilityController != null)
        {
            visibilityController.SetIndoorZoneActive(GetInstanceID(), false);
        }

        trackedPlayers.Clear();
    }

    private void RefreshZoneState()
    {
        if (visibilityController == null) return;

        var isActive = false;
        foreach (var pair in trackedPlayers)
        {
            var state = pair.Value;
            if (state == null) continue;
            if (!MatchesFloorRange(state.FloorIndex)) continue;
            isActive = true;
            break;
        }

        visibilityController.SetIndoorZoneActive(GetInstanceID(), isActive);
    }

    private bool MatchesFloorRange(int floorIndex)
    {
        if (!useFloorRange) return true;

        var min = Mathf.Min(minFloorIndex, maxFloorIndex);
        var max = Mathf.Max(minFloorIndex, maxFloorIndex);
        return floorIndex >= min && floorIndex <= max;
    }

    private static bool TryGetPlayerState(Collider2D other, out FloorSortingState state)
    {
        state = null;
        if (!IsPlayer(other)) return false;
        state = other.GetComponentInParent<FloorSortingState>();
        return state != null;
    }

    private static bool IsPlayer(Collider2D other)
    {
        if (other == null) return false;
        return other.GetComponentInParent<PlayerCombatController2D>() != null;
    }
}
