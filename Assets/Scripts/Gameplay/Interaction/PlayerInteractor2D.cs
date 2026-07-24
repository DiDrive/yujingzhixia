using UnityEngine;

/// <summary>
/// 玩家世界交互控制器。负责选择最近目标、处理按住 F 的计时，并驱动场景中预制的交互提示 UI。
/// </summary>
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
    [Header("Floor (楼层限制)")]
    [Tooltip("开启后，只能发现和操作与玩家 Active Floor Index 相同的容器及露天物资。建议始终开启。")]
    [SerializeField] private bool requireSameFloor = true;
    [Header("Line of Sight (视线阻挡)")]
    [Tooltip("开启后，玩家与目标之间存在墙体碰撞器时，不会显示交互提示，也不能开始或继续搜索。")]
    [SerializeField] private bool requireClearLineOfSight = true;
    [Tooltip("会阻挡搜索和拾取的物理层。默认包含 Wall、Wall_F0、Wall_F1、Wall_F2；地图墙体碰撞器必须位于这些层之一。")]
    [SerializeField] private LayerMask lineOfSightBlockingLayers =
        (1 << 14) | (1 << 18) | (1 << 19) | (1 << 20);
    [Tooltip("视线检测的起点。为空时使用玩家 Transform；如果角色碰撞中心与脚底位置差异较大，可指定一个玩家子物体。")]
    [SerializeField] private Transform lineOfSightOrigin;
    [Tooltip("玩家背包服务。为空时运行时自动查找。")]
    [SerializeField] private InventoryService inventoryService;
    [Tooltip("网格背包服务。为空时运行时自动查找。")]
    [SerializeField] private GridInventoryService gridInventoryService;
    [Tooltip("主背包 UI。为空时运行时优先查找 InventoryUI_main_bag。")]
    [SerializeField] private GridInventoryUnityUI inventoryUi;
    [Tooltip("是否显示原型阶段的交互提示和搜索进度条。后续接正式 UI Prefab 后可以关闭。")]
    [SerializeField] private bool showRuntimeOverlay = true;
    [Tooltip("场景 Canvas 下可直接编辑的交互提示 UI。为空时会查找 WorldInteractionPromptUI；不会在运行时创建。")]
    [SerializeField] private WorldInteractionPromptUI interactionUi;
    [Tooltip("在 Scene 视图中始终显示交互范围。蓝色圆圈就是可搜索容器和可拾取物资的检测范围。")]
    [SerializeField] private bool showInteractionRangeGizmo = true;
    [Header("Runtime Status (运行时状态)")]
    [Tooltip("运行时已登记的可交互物数量。这里只统计主动登记的搜索容器和露天物资，不包含瓦片碰撞器。")]
    [SerializeField] private int registeredInteractableCount;
    [Tooltip("运行时当前选中的搜索或拾取目标。显示“无”表示玩家不在任何可交互物自身碰撞器的范围内。")]
    [SerializeField] private string currentTargetDebug = "未运行";

    private MonoBehaviour targetBehaviour;
    private IWorldInteractable target;
    private float nextScanTime;
    private float holdProgress;
    private float targetDistance;
    private Vector2 targetClosestPoint;

    public InventoryService Inventory => inventoryService;
    public GridInventoryService GridInventory => gridInventoryService;
    public GridInventoryUnityUI InventoryUi => inventoryUi;

    /// <summary>
    /// 判断指定逻辑楼层是否就是玩家当前楼层。
    /// 玩家楼层来自自身或父节点上的 FloorSortingState.ActiveFloorIndex。
    /// </summary>
    public bool IsOnInteractionFloor(int floorIndex)
    {
        if (!requireSameFloor) return true;
        var playerFloorIndex = FloorLayerUtility.GetFloorIndex(transform);
        return playerFloorIndex >= 0 && playerFloorIndex == Mathf.Max(0, floorIndex);
    }

    private void Awake()
    {
        ResolveServices();
    }

    private void OnEnable()
    {
        nextScanTime = 0f;
    }

    private void OnDisable()
    {
        ClearCurrentTarget();
        interactionUi?.HideTarget();
    }

    private void Update()
    {
        // 按住交互键时锁定当前目标，避免多个物体靠得很近时目标切换导致进度反复归零。
        var isHoldingCurrentTarget = holdProgress > 0f && Input.GetKey(interactKey);
        if (!isHoldingCurrentTarget && Time.unscaledTime >= nextScanTime)
        {
            nextScanTime = Time.unscaledTime + Mathf.Max(0.02f, scanInterval);
            ScanForTarget();
        }

        UpdateInteractionInput();
        RefreshInteractionUi();
    }

    public void Bind(InventoryService inventory, GridInventoryService grid, GridInventoryUnityUI ui, WorldInteractionPromptUI promptUi = null)
    {
        inventoryService = inventory;
        gridInventoryService = grid;
        inventoryUi = ui;
        if (promptUi != null) interactionUi = promptUi;
        nextScanTime = 0f;
    }

    public void Configure(float range, float interval, bool runtimeOverlay, bool rangeGizmo)
    {
        interactionRange = Mathf.Max(0.1f, range);
        scanInterval = Mathf.Max(0.02f, interval);
        showRuntimeOverlay = runtimeOverlay;
        showInteractionRangeGizmo = rangeGizmo;
    }

    public void ShowFeedback(string message, float duration = 1.5f)
    {
        if (interactionUi == null) ResolveServices();
        interactionUi?.ShowFeedback(message, duration);
    }

    /// <summary>补齐背包服务、主背包 UI 和世界交互提示 UI 引用。</summary>
    private void ResolveServices()
    {
        if (inventoryService == null) inventoryService = FindObjectOfType<InventoryService>();
        if (gridInventoryService == null) gridInventoryService = FindObjectOfType<GridInventoryService>();
        if (interactionUi == null) interactionUi = FindObjectOfType<WorldInteractionPromptUI>(true);
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

    /// <summary>
    /// 从交互物注册表中选择玩家范围内最近的目标；距离按目标自身碰撞器的最近点计算。
    /// </summary>
    private void ScanForTarget()
    {
        IWorldInteractable bestTarget = null;
        MonoBehaviour bestBehaviour = null;
        var bestDistance = float.MaxValue;
        var bestClosestPoint = Vector2.zero;
        var entries = WorldInteractableRegistry2D.Entries;
        registeredInteractableCount = entries.Count;
        var rangeSqr = interactionRange * interactionRange;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var candidateBehaviour = entry.Behaviour;
            var candidate = entry.Interactable;
            if (candidateBehaviour == null || candidate == null) continue;
            if (!IsOnInteractionFloor(candidate.InteractionFloorIndex)) continue;
            if (!candidate.CanInteract(this)) continue;

            var colliders = entry.Colliders;
            for (var colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                var hit = colliders[colliderIndex];
                if (hit == null || !hit.enabled || !hit.gameObject.activeInHierarchy) continue;
                if ((interactionLayers.value & (1 << hit.gameObject.layer)) == 0) continue;

                var closestPoint = hit.ClosestPoint(transform.position);
                var distance = (closestPoint - (Vector2)transform.position).sqrMagnitude;
                if (distance > rangeSqr || distance >= bestDistance) continue;
                if (!HasClearLineOfSight(closestPoint)) continue;
                bestDistance = distance;
                bestClosestPoint = closestPoint;
                bestTarget = candidate;
                bestBehaviour = candidateBehaviour;
            }
        }

        targetDistance = bestTarget != null ? Mathf.Sqrt(bestDistance) : 0f;
        targetClosestPoint = bestClosestPoint;
        currentTargetDebug = bestBehaviour != null
            ? $"{bestBehaviour.name}（{targetDistance:0.00}/{interactionRange:0.00}m）"
            : "无";
        if (bestBehaviour == targetBehaviour) return;
        target = bestTarget;
        targetBehaviour = bestBehaviour;
        holdProgress = 0f;
    }

    /// <summary>处理即时交互和需要持续按键的交互计时。</summary>
    private void UpdateInteractionInput()
    {
        if (!RefreshCurrentTargetAccess())
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

    /// <summary>
    /// 重新确认当前目标仍在距离内且没有被墙体挡住。
    /// 搜索按键按住期间也会执行，玩家移动到墙后时会立即中断当前进度。
    /// </summary>
    private bool RefreshCurrentTargetAccess()
    {
        if (targetBehaviour == null || target == null ||
            !IsOnInteractionFloor(target.InteractionFloorIndex) ||
            !target.CanInteract(this))
        {
            ClearCurrentTarget();
            return false;
        }

        var entries = WorldInteractableRegistry2D.Entries;
        var rangeSqr = interactionRange * interactionRange;
        var bestDistance = float.MaxValue;
        var bestClosestPoint = Vector2.zero;
        var found = false;

        for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            var entry = entries[entryIndex];
            if (entry.Behaviour != targetBehaviour) continue;

            var colliders = entry.Colliders;
            for (var colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                var collider = colliders[colliderIndex];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                if ((interactionLayers.value & (1 << collider.gameObject.layer)) == 0) continue;

                var closestPoint = collider.ClosestPoint(transform.position);
                var distance = (closestPoint - (Vector2)transform.position).sqrMagnitude;
                if (distance > rangeSqr || distance >= bestDistance) continue;
                if (!HasClearLineOfSight(closestPoint)) continue;

                bestDistance = distance;
                bestClosestPoint = closestPoint;
                found = true;
            }

            break;
        }

        if (!found)
        {
            ClearCurrentTarget();
            return false;
        }

        targetDistance = Mathf.Sqrt(bestDistance);
        targetClosestPoint = bestClosestPoint;
        currentTargetDebug = $"{targetBehaviour.name}（{targetDistance:0.00}/{interactionRange:0.00}m）";
        return true;
    }

    /// <summary>判断玩家到目标碰撞器最近点的连线是否被指定墙体层阻挡。</summary>
    private bool HasClearLineOfSight(Vector2 destination)
    {
        if (!requireClearLineOfSight || lineOfSightBlockingLayers.value == 0) return true;

        var origin = lineOfSightOrigin != null
            ? (Vector2)lineOfSightOrigin.position
            : (Vector2)transform.position;
        if ((destination - origin).sqrMagnitude <= 0.0001f) return true;

        return Physics2D.Linecast(origin, destination, lineOfSightBlockingLayers).collider == null;
    }

    /// <summary>清空失效或被墙体遮挡的当前目标及其搜索进度。</summary>
    private void ClearCurrentTarget()
    {
        target = null;
        targetBehaviour = null;
        targetDistance = 0f;
        targetClosestPoint = Vector2.zero;
        currentTargetDebug = "无";
        holdProgress = 0f;
        nextScanTime = 0f;
    }

    /// <summary>清空本次按住进度并把实际行为交给当前目标执行。</summary>
    private void CompleteInteraction()
    {
        var completedTarget = target;
        holdProgress = 0f;
        completedTarget?.Interact(this);
        nextScanTime = 0f;
    }

    /// <summary>刷新世界提示；容器窗口打开后主动隐藏世界中的搜索提示。</summary>
    private void RefreshInteractionUi()
    {
        if (interactionUi == null)
        {
            ResolveServices();
            if (interactionUi == null) return;
        }
        if (!showRuntimeOverlay || targetBehaviour == null || target == null || !target.CanInteract(this))
        {
            interactionUi.HideTarget();
            return;
        }

        // 容器窗口已经打开时，世界中的“搜索/打开”提示不再显示。
        if (targetBehaviour is SearchableLootContainer openContainer && openContainer.IsContainerUiOpen)
        {
            interactionUi.HideTarget();
            return;
        }

        var duration = Mathf.Max(0f, target.InteractionHoldDuration);
        var showProgress = duration > 0.01f && holdProgress > 0f;
        var progress = showProgress ? Mathf.Clamp01(holdProgress / duration) : 0f;
        interactionUi.ShowTarget(
            interactKey.ToString(),
            target.InteractionPrompt,
            GetTargetDescription(),
            targetDistance,
            interactionRange,
            progress,
            showProgress);
    }

    private string GetTargetDescription()
    {
        if (targetBehaviour is WorldLootItem worldLoot)
        {
            return $"物资 {worldLoot.ItemId} x{worldLoot.Quantity}（{targetBehaviour.name}）";
        }
        if (targetBehaviour is SearchableLootContainer container)
        {
            return $"容器 {targetBehaviour.name}（{container.RuntimeContainerId}）";
        }
        return targetBehaviour.name;
    }

    private void OnDrawGizmos()
    {
        if (!showInteractionRangeGizmo) return;
        Gizmos.color = new Color(0.25f, 0.85f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.1f, interactionRange));
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.25f, 0.85f, 1f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.1f, interactionRange));

        if (targetBehaviour == null) return;
        var origin = lineOfSightOrigin != null ? lineOfSightOrigin.position : transform.position;
        Gizmos.color = HasClearLineOfSight(targetClosestPoint)
            ? new Color(0.25f, 1f, 0.45f, 0.9f)
            : new Color(1f, 0.25f, 0.2f, 0.9f);
        Gizmos.DrawLine(origin, targetClosestPoint);
    }
}
