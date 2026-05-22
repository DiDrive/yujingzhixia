using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class FloorTransitionTrigger2D : MonoBehaviour
{
    [Header("Effect")]
    [Tooltip("进入触发器后要切换到的目标逻辑楼层（FloorIndex）。用于楼梯顶端/底端平台的正式切层。")]
    [SerializeField] private int targetFloorIndex;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var state = other.GetComponentInParent<FloorSortingState>();
        if (state == null) return;
        state.SetFloor(targetFloorIndex);
    }
}
