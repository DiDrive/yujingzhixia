using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 按世界坐标 Y 动态设置渲染排序。
/// 适用于玩家、敌人、家具等非 Tilemap 的 Sprite 对象：
/// Y 越小（越靠屏幕下方），sortingOrder 越大，显示越靠前。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class SpriteSortByY : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("用于计算排序的参考点。角色建议拖脚底点；为空时使用当前物体。")]
    [SerializeField] private Transform sortPoint;
    [Tooltip("优先驱动的 SortingGroup。多 Sprite 角色/敌人建议使用它；为空时自动查找当前物体上的 SortingGroup。")]
    [SerializeField] private SortingGroup targetSortingGroup;
    [Tooltip("当没有 SortingGroup 时回退驱动的 SpriteRenderer。为空时自动查找当前物体上的 SpriteRenderer。")]
    [SerializeField] private SpriteRenderer targetSpriteRenderer;
    [Tooltip("为 true 时：运行时如果没有 SortingGroup，会自动补一个到当前物体，方便多 Sprite 对象统一排序。")]
    [SerializeField] private bool autoAddSortingGroup = true;

    [Header("Rule")]
    [Tooltip("每 1 个世界单位对应多少 sortingOrder。值越大，前后层级越细。")]
    [SerializeField] private int orderPerUnit = 100;
    [Tooltip("最终 sortingOrder 的额外偏移。用于同一位置时手动微调先后。")]
    [SerializeField] private int orderOffset = 0;
    [Tooltip("是否每帧更新排序。角色/敌人建议开启；静态家具也可只在编辑器或位置变化时刷新。")]
    [SerializeField] private bool updateContinuously = true;

    private int lastAppliedOrder = int.MinValue;

    private void Reset()
    {
        if (sortPoint == null) sortPoint = transform;
        CacheTargets(true);
        ApplySorting(force: true);
    }

    private void Awake()
    {
        if (sortPoint == null) sortPoint = transform;
        CacheTargets(autoAddSortingGroup);
        ApplySorting(force: true);
    }

    private void LateUpdate()
    {
        if (!updateContinuously) return;
        ApplySorting(force: false);
    }

    private void OnEnable()
    {
        if (sortPoint == null) sortPoint = transform;
        CacheTargets(autoAddSortingGroup && Application.isPlaying);
        ApplySorting(force: true);
    }

    private void OnValidate()
    {
        if (orderPerUnit < 1) orderPerUnit = 1;
        if (sortPoint == null) sortPoint = transform;
        CacheTargets(false);
        ApplySorting(force: true);
    }

    /// <summary>
    /// 手动刷新排序。适合静态家具在移动后调用一次。
    /// </summary>
    [ContextMenu("Refresh Sorting")]
    public void RefreshSorting()
    {
        ApplySorting(force: true);
    }

    private void CacheTargets(bool allowAddSortingGroup)
    {
        if (targetSortingGroup == null) targetSortingGroup = GetComponent<SortingGroup>();
        if (targetSortingGroup == null && allowAddSortingGroup && autoAddSortingGroup)
        {
            targetSortingGroup = gameObject.AddComponent<SortingGroup>();
        }

        if (targetSpriteRenderer == null) targetSpriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void ApplySorting(bool force)
    {
        var anchor = sortPoint != null ? sortPoint.position : transform.position;
        var order = orderOffset - Mathf.RoundToInt(anchor.y * orderPerUnit);
        if (!force && order == lastAppliedOrder) return;

        if (targetSortingGroup != null)
        {
            targetSortingGroup.sortingOrder = order;
        }
        else if (targetSpriteRenderer != null)
        {
            targetSpriteRenderer.sortingOrder = order;
        }

        lastAppliedOrder = order;
    }
}
