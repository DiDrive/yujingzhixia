using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class FloorTransitionTrigger2D : MonoBehaviour
{
    private enum ProgressAxis
    {
        LocalX,
        LocalY
    }

    private enum EntrySideMode
    {
        Any,
        MinSide,
        MaxSide
    }

    private readonly struct EntryState
    {
        public readonly EntrySideMode EntrySide;

        public EntryState(EntrySideMode entrySide)
        {
            EntrySide = entrySide;
        }
    }

    [Header("Condition")]
    [Tooltip("楼梯进度起点。若填写，则优先用 Start/End 两点计算进度，适合斜着放、带旋转的楼梯区域。")]
    [SerializeField] private Transform progressStartPoint;
    [Tooltip("楼梯进度终点。若填写，则优先用 Start/End 两点计算进度，适合斜着放、带旋转的楼梯区域。")]
    [SerializeField] private Transform progressEndPoint;
    [Tooltip("按本地哪条轴来判断“进入楼梯的深入进度”。旋转过的触发区也可以正常使用。")]
    [SerializeField] private ProgressAxis progressAxis = ProgressAxis.LocalY;
    [Tooltip("要求从哪一侧进入触发区才算有效。用于避免从外墙侧或错误侧面误触发正式切层。")]
    [SerializeField] private EntrySideMode requiredEntrySide = EntrySideMode.Any;
    [Tooltip("至少深入触发区多少比例才正式切层。0.66 表示需要进入到至少 2/3 深处才会切层。")]
    [Range(0f, 1f)]
    [SerializeField] private float activationProgress = 0.66f;

    [Header("Effect")]
    [Tooltip("进入触发器后要切换到的目标逻辑楼层（FloorIndex）。用于楼梯顶端/底端平台的正式切层。")]
    [SerializeField] private int targetFloorIndex;

    [Header("Collision Switch")]
    [Tooltip("正式切层成功后要启用碰撞的对象。脚本会自动启用这些对象及其子物体上的 Collider2D。推荐直接拖 Floor_1/Collision 这类 Tilemap 根对象。")]
    [SerializeField] private GameObject[] collisionObjectsToEnableOnActivate;
    [Tooltip("正式切层成功后要禁用碰撞的对象。脚本会自动禁用这些对象及其子物体上的 Collider2D。推荐直接拖楼梯辅助碰撞对象。")]
    [SerializeField] private GameObject[] collisionObjectsToDisableOnActivate;

    private readonly Dictionary<FloorSortingState, EntryState> trackedEntries = new();
    private readonly Dictionary<FloorSortingState, bool> activatedStates = new();
    private BoxCollider2D triggerCollider;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        triggerCollider = GetComponent<BoxCollider2D>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var state = other.GetComponentInParent<FloorSortingState>();
        if (state == null) return;
        trackedEntries[state] = new EntryState(GetEntrySide(other));
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        var state = other.GetComponentInParent<FloorSortingState>();
        if (state == null) return;
        if (!trackedEntries.TryGetValue(state, out var entryState)) return;
        if (!MatchesRequiredEntrySide(entryState.EntrySide)) return;
        if (GetProgressFromEntrySide(other, entryState.EntrySide) < Mathf.Clamp01(activationProgress)) return;
        if (!activatedStates.TryAdd(state, true)) return;

        state.SetFloor(targetFloorIndex);
        state.ClearVisibilityOverride();
        ApplyCollisionObjectState(collisionObjectsToEnableOnActivate, true);
        ApplyCollisionObjectState(collisionObjectsToDisableOnActivate, false);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var state = other.GetComponentInParent<FloorSortingState>();
        if (state == null) return;
        trackedEntries.Remove(state);
        activatedStates.Remove(state);
    }

    private EntrySideMode GetEntrySide(Collider2D other)
    {
        return GetNormalizedProgress(other) <= 0.5f ? EntrySideMode.MinSide : EntrySideMode.MaxSide;
    }

    private bool MatchesRequiredEntrySide(EntrySideMode entrySide)
    {
        return requiredEntrySide == EntrySideMode.Any || requiredEntrySide == entrySide;
    }

    private float GetProgressFromEntrySide(Collider2D other, EntrySideMode entrySide)
    {
        var normalized = GetNormalizedProgress(other);
        return entrySide == EntrySideMode.MaxSide ? 1f - normalized : normalized;
    }

    private float GetNormalizedProgress(Collider2D other)
    {
        if (TryGetPointProgress(other, out var pointProgress)) return pointProgress;
        if (triggerCollider == null) triggerCollider = GetComponent<BoxCollider2D>();
        if (triggerCollider == null) return 0f;

        var localPoint = transform.InverseTransformPoint(other.bounds.center);
        if (progressAxis == ProgressAxis.LocalX)
        {
            var min = triggerCollider.offset.x - triggerCollider.size.x * 0.5f;
            var max = triggerCollider.offset.x + triggerCollider.size.x * 0.5f;
            return Mathf.Clamp01(Mathf.InverseLerp(min, max, localPoint.x));
        }

        var minY = triggerCollider.offset.y - triggerCollider.size.y * 0.5f;
        var maxY = triggerCollider.offset.y + triggerCollider.size.y * 0.5f;
        return Mathf.Clamp01(Mathf.InverseLerp(minY, maxY, localPoint.y));
    }

    private bool TryGetPointProgress(Collider2D other, out float progress)
    {
        progress = 0f;
        if (progressStartPoint == null || progressEndPoint == null) return false;

        var start = (Vector2)progressStartPoint.position;
        var end = (Vector2)progressEndPoint.position;
        var axis = end - start;
        var lengthSqr = axis.sqrMagnitude;
        if (lengthSqr <= 0.0001f) return false;

        var actorPoint = (Vector2)other.bounds.center;
        progress = Mathf.Clamp01(Vector2.Dot(actorPoint - start, axis) / lengthSqr);
        return true;
    }

    private static void ApplyCollisionObjectState(GameObject[] objects, bool enabled)
    {
        if (objects == null) return;
        for (var i = 0; i < objects.Length; i++)
        {
            var obj = objects[i];
            if (obj == null) continue;

            var colliders = obj.GetComponentsInChildren<Collider2D>(true);
            for (var j = 0; j < colliders.Length; j++)
            {
                if (colliders[j] == null) continue;
                colliders[j].enabled = enabled;
            }
        }
    }
}
