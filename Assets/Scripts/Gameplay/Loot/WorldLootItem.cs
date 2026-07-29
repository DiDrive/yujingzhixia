using UnityEngine;

/// <summary>
/// 地图上的露天物资。玩家完成拾取后，物品进入背包，世界对象随即销毁。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class WorldLootItem : MonoBehaviour, IWorldInteractable
{
    [Header("Floor")]
    [Tooltip("该露天物资所在的逻辑楼层。0=一楼，1=二楼，2=三楼。只有玩家当前楼层与此值相同才可拾取。")]
    [Min(0)] [SerializeField] private int floorIndex;
    [Header("Item")]
    [Tooltip("物品唯一 ID，必须存在于 GridInventoryService 的物品定义或物品 JSON 表中。")]
    [SerializeField] private string itemId = "food_canned";
    [Tooltip("拾取后尝试放入玩家背包的数量。空间不足时物品会完整留在地面。")]
    [Min(1)] [SerializeField] private int quantity = 1;
    [Tooltip("玩家靠近时交互提示中显示的名称。")]
    [SerializeField] private string displayName = "物资";
    [Tooltip("需要持续按住交互键的秒数。设为 0 可单击立即拾取。")]
    [SerializeField] private float pickupDuration = 0.1f;
    [Tooltip("在 Scene 视图中显示露天物资的青色标记和物品 ID，方便在瓦片地图上定位。")]
    [SerializeField] private bool showSceneMarker = true;

    public string InteractionPrompt => $"拾取 {displayName} x{Mathf.Max(1, quantity)}";
    public float InteractionHoldDuration => Mathf.Max(0f, pickupDuration);
    public int InteractionFloorIndex => Mathf.Max(0, floorIndex);
    public Transform InteractionTransform => transform;
    public Vector2 InteractionLineOfSightPoint => transform.position;
    public string ItemId => itemId;
    public int Quantity => Mathf.Max(1, quantity);

    private void OnEnable()
    {
        WorldInteractableRegistry2D.Register(this);
    }

    private void OnDisable()
    {
        WorldInteractableRegistry2D.Unregister(this);
    }

    public bool CanInteract(PlayerInteractor2D interactor)
    {
        return isActiveAndEnabled && interactor != null &&
               interactor.IsOnInteractionFloor(InteractionFloorIndex);
    }

    /// <summary>尝试把完整数量放入玩家背包；失败时保持世界物品不变。</summary>
    public void Interact(PlayerInteractor2D interactor)
    {
        if (interactor.Inventory == null)
        {
            interactor.ShowFeedback("未找到玩家背包");
            return;
        }

        if (!interactor.Inventory.AddItem(itemId, Quantity))
        {
            interactor.ShowFeedback("背包空间不足");
            return;
        }

        interactor.ShowFeedback($"已拾取 {displayName} x{Quantity}");
        Destroy(gameObject);
    }

    /// <summary>供生成器在创建露天物资时写入物品 ID、数量和显示名称。</summary>
    public void Configure(string targetItemId, int targetQuantity, string targetDisplayName = null)
    {
        itemId = targetItemId;
        quantity = Mathf.Max(1, targetQuantity);
        if (!string.IsNullOrWhiteSpace(targetDisplayName)) displayName = targetDisplayName;
    }

    private void Reset()
    {
        var collider = GetComponent<Collider2D>();
        if (collider != null) collider.isTrigger = true;
    }

    private void OnDrawGizmos()
    {
        if (!showSceneMarker) return;
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, 0.3f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.45f,
            $"可拾取：{displayName}（F{InteractionFloorIndex}）\n{itemId} x{Mathf.Max(1, quantity)}");
#endif
    }
}
