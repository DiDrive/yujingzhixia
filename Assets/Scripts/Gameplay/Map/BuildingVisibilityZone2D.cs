using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class BuildingVisibilityZone2D : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("要控制的建筑楼层显示控制器。通常拖挂在 Grid 上的 FloorVisibilityController。")]
    [SerializeField] private FloorVisibilityController visibilityController;

    [Header("Mode")]
    [Tooltip("玩家进入该区域时切换到的模式。建筑内部通常选 IndoorFocus。")]
    [SerializeField] private FloorVisibilityController.VisibilityMode modeOnEnter = FloorVisibilityController.VisibilityMode.IndoorFocus;
    [Tooltip("玩家离开该区域时切换到的模式。建筑外部通常选 OutdoorShowAll。")]
    [SerializeField] private FloorVisibilityController.VisibilityMode modeOnExit = FloorVisibilityController.VisibilityMode.OutdoorShowAll;

    private int playerInsideCount;

    private void Reset()
    {
        var trigger = GetComponent<Collider2D>();
        if (trigger != null) trigger.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;
        playerInsideCount++;
        if (playerInsideCount == 1)
        {
            ApplyMode(modeOnEnter);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!IsPlayer(other)) return;
        playerInsideCount = Mathf.Max(0, playerInsideCount - 1);
        if (playerInsideCount == 0)
        {
            ApplyMode(modeOnExit);
        }
    }

    private void ApplyMode(FloorVisibilityController.VisibilityMode mode)
    {
        if (visibilityController == null) return;
        visibilityController.SetVisibilityMode(mode);
    }

    private static bool IsPlayer(Collider2D other)
    {
        if (other == null) return false;
        return other.GetComponentInParent<PlayerCombatController2D>() != null;
    }
}
