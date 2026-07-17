using UnityEngine;

public class PlayerInteractor2D : MonoBehaviour
{
    [Tooltip("世界交互按键。默认 F。")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [Tooltip("玩家能够发现搜索容器和露天物品的半径。")]
    [SerializeField] private float interactionRange = 1.6f;
    [Tooltip("刷新附近可交互目标的时间间隔。数值越小响应越快，但检测频率更高。")]
    [SerializeField] private float scanInterval = 0.08f;
    [Tooltip("参与世界交互检测的物理层。正式地图建议只勾选 Interactable 层。")]
    [SerializeField] private LayerMask interactionLayers = ~0;
    [Tooltip("玩家背包服务。为空时运行时自动查找。")]
    [SerializeField] private InventoryService inventoryService;
    [Tooltip("网格背包服务。为空时运行时自动查找。")]
    [SerializeField] private GridInventoryService gridInventoryService;
    [Tooltip("主背包 UI。为空时运行时优先查找 InventoryUI_main_bag。")]
    [SerializeField] private GridInventoryUnityUI inventoryUi;
    [Tooltip("是否显示原型阶段的交互提示和搜索进度条。后续接正式 UI Prefab 后可以关闭。")]
    [SerializeField] private bool showRuntimeOverlay = true;

    private readonly Collider2D[] overlapResults = new Collider2D[32];
    private MonoBehaviour targetBehaviour;
    private IWorldInteractable target;
    private float nextScanTime;
    private float holdProgress;
    private string feedback;
    private float feedbackUntil;

    public InventoryService Inventory => inventoryService;
    public GridInventoryService GridInventory => gridInventoryService;
    public GridInventoryUnityUI InventoryUi => inventoryUi;

    private void Awake()
    {
        ResolveServices();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextScanTime)
        {
            nextScanTime = Time.unscaledTime + Mathf.Max(0.02f, scanInterval);
            ScanForTarget();
        }

        UpdateInteractionInput();
    }

    public void Bind(InventoryService inventory, GridInventoryService grid, GridInventoryUnityUI ui)
    {
        inventoryService = inventory;
        gridInventoryService = grid;
        inventoryUi = ui;
    }

    public void ShowFeedback(string message, float duration = 1.5f)
    {
        feedback = message;
        feedbackUntil = Time.unscaledTime + Mathf.Max(0.1f, duration);
    }

    private void ResolveServices()
    {
        if (inventoryService == null) inventoryService = FindObjectOfType<InventoryService>();
        if (gridInventoryService == null) gridInventoryService = FindObjectOfType<GridInventoryService>();
        if (inventoryUi == null)
        {
            var views = FindObjectsOfType<GridInventoryUnityUI>(true);
            for (var i = 0; i < views.Length; i++)
            {
                if (views[i] == null || views[i].IsSideContainerView) continue;
                inventoryUi = views[i];
                break;
            }
        }
    }

    private void ScanForTarget()
    {
        var count = Physics2D.OverlapCircleNonAlloc(transform.position, interactionRange, overlapResults, interactionLayers);
        IWorldInteractable bestTarget = null;
        MonoBehaviour bestBehaviour = null;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < count; i++)
        {
            var hit = overlapResults[i];
            if (hit == null) continue;
            if (!TryFindInteractable(hit.transform, out var candidate, out var candidateBehaviour)) continue;
            if (!candidate.CanInteract(this)) continue;

            var interactionTransform = candidate.InteractionTransform != null
                ? candidate.InteractionTransform
                : candidateBehaviour.transform;
            var distance = ((Vector2)interactionTransform.position - (Vector2)transform.position).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestTarget = candidate;
            bestBehaviour = candidateBehaviour;
        }

        if (bestBehaviour == targetBehaviour) return;
        target = bestTarget;
        targetBehaviour = bestBehaviour;
        holdProgress = 0f;
    }

    private static bool TryFindInteractable(Transform source, out IWorldInteractable interactable, out MonoBehaviour behaviour)
    {
        var cursor = source;
        while (cursor != null)
        {
            var components = cursor.GetComponents<MonoBehaviour>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] is not IWorldInteractable candidate) continue;
                interactable = candidate;
                behaviour = components[i];
                return true;
            }
            cursor = cursor.parent;
        }

        interactable = null;
        behaviour = null;
        return false;
    }

    private void UpdateInteractionInput()
    {
        if (targetBehaviour == null || target == null || !target.CanInteract(this))
        {
            holdProgress = 0f;
            return;
        }

        var duration = Mathf.Max(0f, target.InteractionHoldDuration);
        if (duration <= 0.01f)
        {
            if (Input.GetKeyDown(interactKey)) CompleteInteraction();
            return;
        }

        if (!Input.GetKey(interactKey))
        {
            holdProgress = 0f;
            return;
        }

        holdProgress += Time.unscaledDeltaTime;
        if (holdProgress >= duration) CompleteInteraction();
    }

    private void CompleteInteraction()
    {
        var completedTarget = target;
        holdProgress = 0f;
        completedTarget?.Interact(this);
        nextScanTime = 0f;
    }

    private void OnGUI()
    {
        if (!showRuntimeOverlay) return;
        var centerX = Screen.width * 0.5f;
        var bottom = Screen.height - 150f;

        if (targetBehaviour != null && target != null && target.CanInteract(this))
        {
            var promptRect = new Rect(centerX - 180f, bottom, 360f, 28f);
            GUI.Box(promptRect, $"[{interactKey}] {target.InteractionPrompt}");
            var duration = Mathf.Max(0f, target.InteractionHoldDuration);
            if (duration > 0.01f && holdProgress > 0f)
            {
                var progress = Mathf.Clamp01(holdProgress / duration);
                GUI.Box(new Rect(centerX - 150f, bottom + 32f, 300f, 18f), string.Empty);
                GUI.Box(new Rect(centerX - 148f, bottom + 34f, 296f * progress, 14f), $"{progress:P0}");
            }
        }

        if (!string.IsNullOrWhiteSpace(feedback) && Time.unscaledTime < feedbackUntil)
        {
            GUI.Box(new Rect(centerX - 180f, bottom - 38f, 360f, 28f), feedback);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.25f, 0.85f, 1f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.1f, interactionRange));
    }
}
