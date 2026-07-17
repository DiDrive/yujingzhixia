using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class WorldLootItem : MonoBehaviour, IWorldInteractable
{
    [Tooltip("物品唯一 ID，必须存在于 GridInventoryService 的物品定义或物品 JSON 表中。")]
    [SerializeField] private string itemId = "food_canned";
    [Tooltip("拾取后尝试放入玩家背包的数量。空间不足时物品会完整留在地面。")]
    [Min(1)] [SerializeField] private int quantity = 1;
    [Tooltip("玩家靠近时交互提示中显示的名称。")]
    [SerializeField] private string displayName = "物资";
    [Tooltip("需要持续按住交互键的秒数。设为 0 可单击立即拾取。")]
    [SerializeField] private float pickupDuration = 0.1f;

    public string InteractionPrompt => $"拾取 {displayName} x{Mathf.Max(1, quantity)}";
    public float InteractionHoldDuration => Mathf.Max(0f, pickupDuration);
    public Transform InteractionTransform => transform;
    public string ItemId => itemId;
    public int Quantity => Mathf.Max(1, quantity);

    public bool CanInteract(PlayerInteractor2D interactor)
    {
        return isActiveAndEnabled && interactor != null;
    }

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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}
