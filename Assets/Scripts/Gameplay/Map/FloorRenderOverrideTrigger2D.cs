using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class FloorRenderOverrideTrigger2D : MonoBehaviour
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
    [Tooltip("触发器生效时要求进入者当前的 FloorIndex。用于避免不同楼层角色误触发。")]
    [SerializeField] private int fromFloorIndex;
    [Tooltip("楼梯进度起点。若填写，则优先用 Start/End 两点计算进度，适合斜着放、带旋转的楼梯区域。")]
    [SerializeField] private Transform progressStartPoint;
    [Tooltip("楼梯进度终点。若填写，则优先用 Start/End 两点计算进度，适合斜着放、带旋转的楼梯区域。")]
    [SerializeField] private Transform progressEndPoint;
    [Tooltip("按本地哪条轴来判断“进入楼梯的深入进度”。旋转过的触发区也可以正常使用。")]
    [SerializeField] private ProgressAxis progressAxis = ProgressAxis.LocalY;
    [Tooltip("要求从哪一侧进入触发区才算有效。用于避免从外墙侧或错误侧面误触发。")]
    [SerializeField] private EntrySideMode requiredEntrySide = EntrySideMode.Any;
    [Tooltip("至少深入触发区多少比例才生效。0.66 表示需要进入到至少 2/3 深处才会切层。")]
    [Range(0f, 1f)]
    [SerializeField] private float activationProgress = 0.66f;

    [Header("Effect")]
    [Tooltip("进入触发器后临时设置的 RenderFloorIndex。常用于楼梯过渡段：FloorIndex 还没变，但渲染先抬到高一层的 Actor_Fx。")]
    [SerializeField] private int toRenderFloorIndex;
    [Tooltip("为 true 时：达到触发阈值后直接正式切换到目标楼层，不再依赖离开触发区时回退。适合楼梯入口一进入就切到高一层。")]
    [SerializeField] private bool commitFloorImmediately = true;
    [Tooltip("为 true 时：即使入口触发器已经正式切换了碰撞楼层，楼层显示仍暂时保持在进入前楼层，直到平台触发器再清除覆盖。")]
    [SerializeField] private bool keepVisibilityOnSourceFloorUntilTransition = true;
    [Tooltip("为 true 时：进入触发器后，同时临时把逻辑楼层切到 toRenderFloorIndex。这样角色能提前与高一层的 Wall_Fx 碰撞，不会被当前层挡住上楼。")]
    [SerializeField] private bool syncTemporaryFloorOverride = true;

    [Tooltip("为 true 时：只有当进入者 FloorIndex == fromFloorIndex 才会应用覆盖；退出时也只在 FloorIndex 仍等于 fromFloorIndex 时清除覆盖。")]
    [SerializeField] private bool onlyApplyWhenFromFloorMatches = true;

    [Header("Collision Switch")]
    [Tooltip("触发成功时要启用碰撞的对象。脚本会自动启用这些对象及其子物体上的 Collider2D。推荐直接拖楼梯辅助碰撞对象。")]
    [SerializeField] private GameObject[] collisionObjectsToEnableOnActivate;
    [Tooltip("触发成功时要禁用碰撞的对象。脚本会自动禁用这些对象及其子物体上的 Collider2D。推荐直接拖 Floor_1/Collision 这类 Tilemap 根对象。")]
    [SerializeField] private GameObject[] collisionObjectsToDisableOnActivate;
    [Tooltip("只有在非 commit 模式下才有意义。为 true 时：离开触发区会把上面启停的组件恢复到进入前的状态。")]
    [SerializeField] private bool restoreBehaviourStateOnExit = true;

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
        if (onlyApplyWhenFromFloorMatches && state.FloorIndex != fromFloorIndex) return;
        trackedEntries[state] = new EntryState(GetEntrySide(other));
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        var state = other.GetComponentInParent<FloorSortingState>();
        if (state == null) return;
        if (!trackedEntries.TryGetValue(state, out var entryState)) return;
        if (onlyApplyWhenFromFloorMatches && state.FloorIndex != fromFloorIndex) return;
        if (!MatchesRequiredEntrySide(entryState.EntrySide)) return;
        if (GetProgressFromEntrySide(other, entryState.EntrySide) < Mathf.Clamp01(activationProgress)) return;
        if (!activatedStates.TryAdd(state, true)) return;

        ApplyCollisionObjectState(collisionObjectsToEnableOnActivate, true);
        ApplyCollisionObjectState(collisionObjectsToDisableOnActivate, false);

        if (commitFloorImmediately)
        {
            state.SetFloor(toRenderFloorIndex);
            if (keepVisibilityOnSourceFloorUntilTransition)
            {
                state.SetVisibilityOverride(fromFloorIndex);
            }
            trackedEntries.Remove(state);
            return;
        }

        state.SetRenderOverride(toRenderFloorIndex);
        if (syncTemporaryFloorOverride) state.SetTemporaryFloorOverride(toRenderFloorIndex);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var state = other.GetComponentInParent<FloorSortingState>();
        if (state == null) return;
        trackedEntries.Remove(state);
        var wasActivated = activatedStates.Remove(state);
        if (commitFloorImmediately) return;

        if (wasActivated && restoreBehaviourStateOnExit)
        {
            ApplyCollisionObjectState(collisionObjectsToEnableOnActivate, false);
            ApplyCollisionObjectState(collisionObjectsToDisableOnActivate, true);
        }

        if (onlyApplyWhenFromFloorMatches && state.FloorIndex == fromFloorIndex)
        {
            state.ClearRenderOverride();
            if (syncTemporaryFloorOverride) state.ClearTemporaryFloorOverride();
            return;
        }

        state.ClearRenderOverride();
        if (syncTemporaryFloorOverride) state.ClearTemporaryFloorOverride();
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
