using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class FloorRenderOverrideTrigger2D : MonoBehaviour
{
    [Header("Condition")]
    [Tooltip("触发器生效时要求进入者当前的 FloorIndex。用于避免不同楼层角色误触发。")]
    [SerializeField] private int fromFloorIndex;

    [Header("Effect")]
    [Tooltip("进入触发器后临时设置的 RenderFloorIndex。常用于楼梯过渡段：FloorIndex 还没变，但渲染先抬到高一层的 Actor_Fx。")]
    [SerializeField] private int toRenderFloorIndex;

    [Tooltip("为 true 时：只有当进入者 FloorIndex == fromFloorIndex 才会应用覆盖；退出时也只在 FloorIndex 仍等于 fromFloorIndex 时清除覆盖。")]
    [SerializeField] private bool onlyApplyWhenFromFloorMatches = true;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var state = other.GetComponentInParent<FloorSortingState>();
        if (state == null) return;
        if (onlyApplyWhenFromFloorMatches && state.FloorIndex != fromFloorIndex) return;
        state.SetRenderOverride(toRenderFloorIndex);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var state = other.GetComponentInParent<FloorSortingState>();
        if (state == null) return;

        if (onlyApplyWhenFromFloorMatches && state.FloorIndex == fromFloorIndex)
        {
            state.ClearRenderOverride();
            return;
        }

        state.ClearRenderOverride();
    }
}
