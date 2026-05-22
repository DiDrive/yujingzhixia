using UnityEngine;

[DisallowMultipleComponent]
public class FloorSortingState : MonoBehaviour
{
    [Header("Floor")]
    [Tooltip("角色/敌人的逻辑楼层（决定碰撞与 AI 归属）。")]
    [SerializeField] private int floorIndex;

    [Header("Render")]
    [Tooltip("渲染楼层的临时覆盖值。-1 表示不覆盖，使用 FloorIndex。适用于楼梯过渡段让角色提前显示在高一层。")]
    [SerializeField] private int renderFloorOverride = -1;

    public int FloorIndex
    {
        get => floorIndex;
        set => floorIndex = Mathf.Max(0, value);
    }

    public int RenderFloorOverride
    {
        get => renderFloorOverride;
        set => renderFloorOverride = value;
    }

    [Tooltip("最终用于渲染的楼层索引。优先使用 RenderFloorOverride，否则使用 FloorIndex。")]
    public int RenderFloorIndex => renderFloorOverride >= 0 ? renderFloorOverride : floorIndex;

    [Tooltip("正式切换逻辑楼层：同时清除渲染覆盖。用于楼梯平台的切层触发器。")]
    public void SetFloor(int newFloorIndex)
    {
        FloorIndex = newFloorIndex;
        renderFloorOverride = -1;
    }

    [Tooltip("设置渲染楼层的临时覆盖。用于楼梯过渡段提前显示在高一层 Actor_Fx。")]
    public void SetRenderOverride(int newRenderFloorIndex)
    {
        renderFloorOverride = newRenderFloorIndex;
    }

    [Tooltip("清除渲染楼层的临时覆盖。角色会回到 FloorIndex 对应的 Actor_Fx。")]
    public void ClearRenderOverride()
    {
        renderFloorOverride = -1;
    }
}
