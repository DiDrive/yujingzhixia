using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine;

/// <summary>
/// 地图上的可搜索容器。首次交互生成掉落并打开容器 UI，随后按格子顺序逐件揭示物品。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SearchableLootContainer : MonoBehaviour, IWorldInteractable
{
    [Header("Identity")]
    [Tooltip("容器的持久化唯一 ID。正式地图建议手动填写且保持唯一；为空时会根据场景、名称和坐标自动生成。")]
    [SerializeField] private string persistentId;
    [Tooltip("交互提示中显示的容器名称。")]
    [SerializeField] private string displayName = "物资容器";
    [Header("Floor")]
    [Tooltip("该容器所在的逻辑楼层。0=一楼，1=二楼，2=三楼。只有玩家当前楼层与此值相同才可搜索。")]
    [Min(0)] [SerializeField] private int floorIndex;
    [Header("Interaction")]
    [Tooltip("隔墙检测射向的实际操作点。贴墙容器必须把它放在玩家能够操作的一侧；为空时退回容器 Transform。")]
    [SerializeField] private Transform interactionLineOfSightPoint;
    [Header("Search")]
    [Tooltip("第一次搜索需要持续按住交互键的秒数。已经搜索过的容器再次打开不需要等待。")]
    [SerializeField] private float searchDuration = 1.5f;
    [Tooltip("使用的 JSON 掉落表 ID，对应 Resources/Data/loot_tables.json 中的 tableId。")]
    [SerializeField] private string lootTableId = "survival_basic";
    [Tooltip("覆盖掉落表的最少抽取次数。-1 表示使用 JSON 掉落表自己的配置。")]
    [SerializeField] private int minRolls = -1;
    [Tooltip("覆盖掉落表的最多抽取次数。-1 表示使用 JSON 掉落表自己的配置。")]
    [SerializeField] private int maxRolls = -1;
    [Header("Item Search")]
    [Tooltip("普通品质物品的逐件搜索时间（秒）。容器打开后，物品会按格子位置依次搜索。")]
    [Min(0.05f)] [SerializeField] private float commonItemSearchDuration = 0.8f;
    [Tooltip("优秀品质物品的逐件搜索时间（秒）。")]
    [Min(0.05f)] [SerializeField] private float uncommonItemSearchDuration = 1.2f;
    [Tooltip("稀有品质物品的逐件搜索时间（秒）。")]
    [Min(0.05f)] [SerializeField] private float rareItemSearchDuration = 1.8f;
    [Tooltip("史诗品质物品的逐件搜索时间（秒）。")]
    [Min(0.05f)] [SerializeField] private float epicItemSearchDuration = 2.6f;
    [Tooltip("传说品质物品的逐件搜索时间（秒）。")]
    [Min(0.05f)] [SerializeField] private float legendaryItemSearchDuration = 4f;
    [Header("Grid")]
    [Tooltip("容器网格行数。行数越多，面板越高。")]
    [Min(1)] [SerializeField] private int rows = 4;
    [Tooltip("容器网格列数。列数越多，面板越宽。")]
    [Min(1)] [SerializeField] private int columns = 5;
    [Tooltip("允许放入该容器的物品标签。为空时不限制。")]
    [SerializeField] private List<string> allowedItemTags = new();
    [Header("Container UI")]
    [Tooltip("物资侧栏相对主背包面板的偏移。X 增大向右移动，Y 增大向上移动。")]
    [SerializeField] private Vector2 containerUiOffset = new(440f, 0f);
    [Tooltip("物资容器单格的像素大小。建议 28~48；数值越小，整个容器面板越紧凑。")]
    [Range(20f, 96f)] [SerializeField] private float containerUiCellSize = 36f;
    [Tooltip("物资容器格子间距。建议 1~4。")]
    [Range(0f, 16f)] [SerializeField] private float containerUiCellSpacing = 2f;
    [Tooltip("开启后，再次按交互键会关闭已经打开的物资容器。")]
    [SerializeField] private bool closeOnInteractAgain = true;
    [Header("Editor / Prefab UI")]
    [Tooltip("场景中可直接编辑的物资容器 UI。可通过 Inspector 下方的“创建场景容器 UI”按钮生成；运行时优先复用它。")]
    [SerializeField] private GridInventoryUnityUI containerUiSceneView;
    [Tooltip("可选的物资容器 UI 预制体。为空时复用主背包的样式并动态创建侧栏。")]
    [SerializeField] private GridInventoryUnityUI containerUiPrefab;
    [Tooltip("可选的 UI 实例父节点。为空时放到主背包 UI 的同级节点。")]
    [SerializeField] private Transform containerUiParent;
    [Tooltip("兼容旧场景：找不到场景 UI 时允许运行时实例化/创建侧栏。新内容请保持关闭，并在编辑器中创建场景容器 UI。")]
    [SerializeField] private bool allowRuntimeUiFallback;
    [Header("Scene Marker")]
    [Tooltip("在 Scene 视图中始终显示该搜索容器的橙色标记和名称，方便在瓦片地图上查看哪些位置可以搜索。")]
    [SerializeField] private bool showSceneMarker = true;
    [Tooltip("没有可读取尺寸的碰撞器时，Scene 标记使用的默认大小。")]
    [SerializeField] private Vector2 sceneMarkerSize = new(0.8f, 0.8f);

    private GridInventoryService gridInventory;
    private LootTableDatabase lootDatabase;
    private GridInventoryUnityUI spawnedContainerUi;
    private bool searched;
    private bool populated;
    private string runtimeContainerId;
    private PlayerInteractor2D lastInteractor;
    // 队列保存首次打开时物品的固定搜索顺序；集合记录本次运行中已经揭示的实例。
    private readonly List<string> itemSearchQueue = new();
    private readonly HashSet<string> revealedItemIds = new();
    private int currentItemSearchIndex = -1;
    private float currentItemSearchElapsed;
    private float nextRuntimeStateSyncTime;
    // 同一时间只允许一个世界容器保持打开，打开新容器时会关闭旧容器。
    private static SearchableLootContainer activeOpenContainer;

    public string InteractionPrompt => searched ? $"打开 {displayName}" : $"搜索 {displayName}";
    public float InteractionHoldDuration => searched ? 0f : Mathf.Max(0f, searchDuration);
    public int InteractionFloorIndex => Mathf.Max(0, floorIndex);
    public Transform InteractionTransform => transform;
    public Vector2 InteractionLineOfSightPoint =>
        interactionLineOfSightPoint != null
            ? (Vector2)interactionLineOfSightPoint.position
            : (Vector2)transform.position;
    public bool HasBeenSearched => searched;
    /// <summary>容器已经生成掉落，且当前已没有任何物品。</summary>
    public bool IsEmpty => populated && gridInventory != null &&
                           !gridInventory.ItemInstances.Values.Any(item => item.containerId == runtimeContainerId);
    public string RuntimeContainerId => runtimeContainerId;
    public string PersistentId => persistentId;
    public bool IsContainerUiOpen => IsContainerUiOpenInternal();

    /// <summary>
    /// 磁盘存档晚于场景 Awake 恢复完成时，由存档服务再次把持久状态应用到当前容器。
    /// </summary>
    public void RestorePersistentState()
    {
        ResolveServices();
        if (EnsureRegistered()) RestoreRuntimeState();
    }

    /// <summary>
    /// 把本组件配置成运行时生成的容器（例如僵尸尸体）。调用时组件所在物体应保持未激活，
    /// 从而确保 Awake/OnEnable 使用新的唯一 ID、楼层和共享场景 UI 完成注册。
    /// </summary>
    public void ConfigureRuntime(
        string uniqueId,
        string runtimeDisplayName,
        int runtimeFloorIndex,
        string runtimeLootTableId,
        int runtimeRows,
        int runtimeColumns,
        GridInventoryUnityUI sharedSceneUi)
    {
        persistentId = uniqueId;
        runtimeContainerId = string.Empty;
        displayName = string.IsNullOrWhiteSpace(runtimeDisplayName) ? "尸体" : runtimeDisplayName;
        floorIndex = Mathf.Max(0, runtimeFloorIndex);
        lootTableId = string.IsNullOrWhiteSpace(runtimeLootTableId) ? "zombie_civilian" : runtimeLootTableId;
        rows = Mathf.Max(1, runtimeRows);
        columns = Mathf.Max(1, runtimeColumns);
        containerUiSceneView = sharedSceneUi;
        allowRuntimeUiFallback = false;
    }

    private void Awake()
    {
        ResolveSceneUiView();
        ResolveServices();
        RestorePersistentState();
    }

    private void OnEnable()
    {
        WorldInteractableRegistry2D.Register(this);
    }

    private void Update()
    {
        // 换楼后立即关闭仍显示的容器，避免隔层继续查看或搬动物资。
        if (lastInteractor != null && IsContainerUiOpenInternal() &&
            !lastInteractor.IsOnInteractionFloor(InteractionFloorIndex))
        {
            ForceCloseOpenUi();
            return;
        }

        if (searched && IsContainerUiOpenInternal() &&
            currentItemSearchIndex >= 0 && currentItemSearchIndex < itemSearchQueue.Count)
        {
            var instanceId = itemSearchQueue[currentItemSearchIndex];
            if (!gridInventory.TryGetItemInstance(instanceId, out var instance))
            {
                CompleteCurrentItemSearch();
            }
            else
            {
                currentItemSearchElapsed += Time.unscaledDeltaTime;
                if (currentItemSearchElapsed >= GetItemSearchDuration(instance.itemId)) CompleteCurrentItemSearch();
            }
        }

        // 会话级仓库只复制内存快照，定期同步可捕获玩家从容器取走或放回物品的变化。
        if (populated && Time.unscaledTime >= nextRuntimeStateSyncTime)
        {
            nextRuntimeStateSyncTime = Time.unscaledTime + 0.5f;
            SaveRuntimeState();
        }
    }

    public bool CanInteract(PlayerInteractor2D interactor)
    {
        return isActiveAndEnabled && interactor != null &&
               interactor.IsOnInteractionFloor(InteractionFloorIndex);
    }

    /// <summary>处理首次搜索、再次开关，以及不同世界容器之间的互斥。</summary>
    public void Interact(PlayerInteractor2D interactor)
    {
        lastInteractor = interactor;
        ResolveServices();
        if (!EnsureRegistered())
        {
            interactor.ShowFeedback("搜索容器初始化失败");
            return;
        }

        if (searched && closeOnInteractAgain && TryCloseOpenUi(interactor))
        {
            if (activeOpenContainer == this) activeOpenContainer = null;
            return;
        }
        if (activeOpenContainer != null && activeOpenContainer != this)
        {
            activeOpenContainer.ForceCloseOpenUi();
        }
        if (!populated) PopulateOnce();
        searched = true;
        EnsureItemSearchQueue();
        SaveRuntimeState();
        OpenContainerUi(interactor);
        if (containerUiSceneView != null || allowRuntimeUiFallback) activeOpenContainer = this;
    }

    private void ResolveServices()
    {
        if (gridInventory == null) gridInventory = FindObjectOfType<GridInventoryService>();
        if (lootDatabase == null) lootDatabase = FindObjectOfType<LootTableDatabase>();
    }

    private void ResolveSceneUiView()
    {
        if (containerUiSceneView != null) return;
        var expectedName = $"InventoryUI_loot_{Sanitize(persistentId)}";
        var views = FindObjectsOfType<GridInventoryUnityUI>(true);
        for (var i = 0; i < views.Length; i++)
        {
            if (views[i] == null || views[i].name != expectedName) continue;
            containerUiSceneView = views[i];
            return;
        }
    }

    /// <summary>为该世界容器创建稳定的运行时网格容器，并同步行列数和标签限制。</summary>
    private bool EnsureRegistered()
    {
        if (gridInventory == null) return false;
        if (string.IsNullOrWhiteSpace(runtimeContainerId)) runtimeContainerId = BuildRuntimeContainerId();
        return gridInventory.RegisterRuntimeContainer(
            runtimeContainerId,
            Mathf.Max(1, rows),
            Mathf.Max(1, columns),
            GridContainerOwnerType.WorldLoot,
            false,
            allowedItemTags);
    }

    /// <summary>仅在首次成功搜索时按照掉落表填充物资，避免重复开关反复刷物品。</summary>
    private void PopulateOnce()
    {
        populated = true;
        if (lootDatabase == null) return;
        var loot = lootDatabase.Roll(lootTableId, minRolls, maxRolls);
        var addedStacks = 0;
        for (var i = 0; i < loot.Count; i++)
        {
            var stack = loot[i];
            if (gridInventory.TryAddItemToContainer(runtimeContainerId, stack.itemId, stack.quantity))
            {
                addedStacks++;
            }
        }

        if (addedStacks == 0)
        {
            Debug.LogWarning($"SearchableLootContainer '{name}' 没有生成可放入的物资，请检查掉落表和物品定义。", this);
        }
        SaveRuntimeState();
    }

    /// <summary>优先打开编辑器中绑定的场景 UI；旧内容才允许使用运行时回退方案。</summary>
    private void OpenContainerUi(PlayerInteractor2D interactor)
    {
        var mainUi = interactor.InventoryUi;
        if (containerUiSceneView != null)
        {
            containerUiSceneView.SetItemSearchStateProvider(GetItemSearchState);
            containerUiSceneView.BindToContainer(gridInventory, runtimeContainerId, true, displayName);
            containerUiSceneView.SetOpen(true);
            if (mainUi != null) mainUi.SetOpen(true);
            return;
        }

        if (!allowRuntimeUiFallback)
        {
            interactor.ShowFeedback("该容器尚未绑定场景 UI，请在编辑器 Inspector 中创建");
            return;
        }

        if (containerUiPrefab != null)
        {
            if (spawnedContainerUi == null)
            {
                var parent = containerUiParent != null
                    ? containerUiParent
                    : mainUi != null ? mainUi.transform.parent : null;
                spawnedContainerUi = Instantiate(containerUiPrefab, parent);
                spawnedContainerUi.name = $"InventoryUI_{runtimeContainerId}";
            }

            spawnedContainerUi.BindToContainer(gridInventory, runtimeContainerId, true);
            spawnedContainerUi.SetItemSearchStateProvider(GetItemSearchState);
            spawnedContainerUi.ApplyExternalContainerLayout(containerUiOffset, containerUiCellSize, containerUiCellSpacing);
            spawnedContainerUi.SetOpen(true);
            if (mainUi != null) mainUi.SetOpen(true);
            return;
        }

        if (mainUi != null &&
            mainUi.ShowContainerAsSidePanel(runtimeContainerId, containerUiOffset, containerUiCellSize, containerUiCellSpacing))
        {
            return;
        }
        interactor.ShowFeedback("未找到背包 UI");
    }

    /// <summary>按行、列从左上到右下建立逐件搜索队列。</summary>
    private void EnsureItemSearchQueue()
    {
        if (itemSearchQueue.Count > 0 || revealedItemIds.Count > 0) return;
        itemSearchQueue.AddRange(gridInventory.ItemInstances.Values
            .Where(item => item.containerId == runtimeContainerId && item.originRow >= 0 && item.originColumn >= 0)
            .OrderBy(item => item.originRow)
            .ThenBy(item => item.originColumn)
            .Select(item => item.instanceId));
        currentItemSearchIndex = itemSearchQueue.Count > 0 ? 0 : -1;
        currentItemSearchElapsed = 0f;
    }

    /// <summary>标记当前物品已揭示，并自动切换到队列中的下一件物品。</summary>
    private void CompleteCurrentItemSearch()
    {
        if (currentItemSearchIndex < 0 || currentItemSearchIndex >= itemSearchQueue.Count) return;
        revealedItemIds.Add(itemSearchQueue[currentItemSearchIndex]);
        currentItemSearchIndex++;
        currentItemSearchElapsed = 0f;
        AdvanceToNextUnrevealedItem();
        SaveRuntimeState();
    }

    /// <summary>向网格 UI 提供某件物品的蒙版、当前搜索状态和归一化进度。</summary>
    private bool GetItemSearchState(string containerId, string instanceId, out bool revealed, out bool searching, out float progress)
    {
        revealed = true;
        searching = false;
        progress = 1f;
        if (containerId != runtimeContainerId || string.IsNullOrWhiteSpace(instanceId)) return false;

        // 玩家后来放入容器的物品不在原始搜索队列中，默认视为已知物品。
        if (!itemSearchQueue.Contains(instanceId))
        {
            revealed = true;
            return true;
        }

        revealed = revealedItemIds.Contains(instanceId);
        if (revealed) return true;
        searching = currentItemSearchIndex >= 0 && currentItemSearchIndex < itemSearchQueue.Count && itemSearchQueue[currentItemSearchIndex] == instanceId;
        if (searching && gridInventory.TryGetItemInstance(instanceId, out var instance))
        {
            progress = Mathf.Clamp01(currentItemSearchElapsed / GetItemSearchDuration(instance.itemId));
        }
        else progress = 0f;
        return true;
    }

    /// <summary>根据物品定义中的品质选择对应的逐件搜索时长。</summary>
    private float GetItemSearchDuration(string itemId)
    {
        if (!gridInventory.TryGetItemDefinition(itemId, out var definition)) return commonItemSearchDuration;
        return Mathf.Max(0.05f, definition.quality switch
        {
            GridItemQuality.Uncommon => uncommonItemSearchDuration,
            GridItemQuality.Rare => rareItemSearchDuration,
            GridItemQuality.Epic => epicItemSearchDuration,
            GridItemQuality.Legendary => legendaryItemSearchDuration,
            _ => commonItemSearchDuration
        });
    }

    private bool IsContainerUiOpenInternal()
    {
        if (containerUiSceneView != null) return containerUiSceneView.IsOpen();
        if (spawnedContainerUi != null) return spawnedContainerUi.IsOpen();
        return false;
    }

    private bool TryCloseOpenUi(PlayerInteractor2D interactor)
    {
        if (containerUiSceneView != null && containerUiSceneView.IsOpen())
        {
            containerUiSceneView.SetOpen(false);
            SaveRuntimeState();
            return true;
        }

        if (spawnedContainerUi != null && spawnedContainerUi.IsOpen())
        {
            spawnedContainerUi.SetOpen(false);
            SaveRuntimeState();
            return true;
        }

        var mainUi = interactor.InventoryUi;
        if (mainUi == null || !mainUi.IsContainerSidePanelOpen(runtimeContainerId)) return false;
        mainUi.HideContainerSidePanel(runtimeContainerId);
        SaveRuntimeState();
        return true;
    }

    private void ForceCloseOpenUi()
    {
        SaveRuntimeState();
        if (containerUiSceneView != null) containerUiSceneView.SetOpen(false);
        if (spawnedContainerUi != null) spawnedContainerUi.SetOpen(false);
        var mainUi = lastInteractor != null ? lastInteractor.InventoryUi : null;
        if (mainUi != null && !string.IsNullOrWhiteSpace(runtimeContainerId))
        {
            mainUi.HideContainerSidePanel(runtimeContainerId);
        }
        if (activeOpenContainer == this) activeOpenContainer = null;
    }

    private void OnDisable()
    {
        SaveRuntimeState();
        WorldInteractableRegistry2D.Unregister(this);
        if (activeOpenContainer == this) activeOpenContainer = null;
    }

    /// <summary>跳过已经揭示的队列项，并定位下一件仍需搜索的物品。</summary>
    private void AdvanceToNextUnrevealedItem()
    {
        while (currentItemSearchIndex >= 0 && currentItemSearchIndex < itemSearchQueue.Count &&
               revealedItemIds.Contains(itemSearchQueue[currentItemSearchIndex]))
        {
            currentItemSearchIndex++;
        }
        if (currentItemSearchIndex >= itemSearchQueue.Count) currentItemSearchIndex = -1;
    }

    /// <summary>从会话级快照恢复剩余物品、布局和逐物品揭示状态。</summary>
    private void RestoreRuntimeState()
    {
        var persistenceKey = GetPersistenceKey();
        if (!SearchableLootRuntimeStateStore.TryGet(persistenceKey, out var state)) return;

        populated = state.populated;
        searched = state.searched;
        itemSearchQueue.Clear();
        revealedItemIds.Clear();

        var savedItems = state.items ?? new List<SearchableLootItemState>();
        var existingItems = gridInventory.ItemInstances.Values
            .Where(item => item.containerId == runtimeContainerId)
            .OrderBy(item => item.originRow)
            .ThenBy(item => item.originColumn)
            .ToList();

        // 若背包服务跨场景常驻，运行时容器及物品可能仍存在；此时只重建搜索引用，禁止重复生成。
        if (existingItems.Count > 0)
        {
            for (var i = 0; i < existingItems.Count; i++)
            {
                var existing = existingItems[i];
                itemSearchQueue.Add(existing.instanceId);
                var saved = savedItems.FirstOrDefault(item =>
                    item != null &&
                    item.itemId == existing.itemId &&
                    item.row == existing.originRow &&
                    item.column == existing.originColumn);
                if (saved != null && saved.revealed) revealedItemIds.Add(existing.instanceId);
            }

            currentItemSearchIndex = 0;
            currentItemSearchElapsed = 0f;
            AdvanceToNextUnrevealedItem();
            return;
        }

        var orderedItems = savedItems
            .Where(item => item != null)
            .OrderBy(item => item.row)
            .ThenBy(item => item.column);
        foreach (var item in orderedItems)
        {
            if (!gridInventory.TrySpawnToCell(
                    item.itemId,
                    runtimeContainerId,
                    item.row,
                    item.column,
                    item.rotation,
                    item.stackCount,
                    out var restoredInstanceId))
            {
                Debug.LogWarning($"SearchableLootContainer '{name}' 无法恢复物品 '{item.itemId}'。", this);
                continue;
            }

            itemSearchQueue.Add(restoredInstanceId);
            if (item.revealed) revealedItemIds.Add(restoredInstanceId);
        }

        currentItemSearchIndex = itemSearchQueue.Count > 0 ? 0 : -1;
        currentItemSearchElapsed = 0f;
        AdvanceToNextUnrevealedItem();
    }

    /// <summary>捕获当前剩余物品；已经从容器取走的实例不会进入快照，因此重载后不会再次出现。</summary>
    private void SaveRuntimeState()
    {
        if (!populated || gridInventory == null || string.IsNullOrWhiteSpace(runtimeContainerId)) return;

        var state = new SearchableLootContainerState
        {
            persistentId = GetPersistenceKey(),
            populated = populated,
            searched = searched
        };
        var items = gridInventory.ItemInstances.Values
            .Where(item => item.containerId == runtimeContainerId)
            .OrderBy(item => item.originRow)
            .ThenBy(item => item.originColumn);
        foreach (var item in items)
        {
            state.items.Add(new SearchableLootItemState
            {
                itemId = item.itemId,
                stackCount = item.stackCount,
                rotation = item.rotation,
                row = item.originRow,
                column = item.originColumn,
                revealed = revealedItemIds.Contains(item.instanceId) || !itemSearchQueue.Contains(item.instanceId)
            });
        }
        SearchableLootRuntimeStateStore.Save(state);
    }

    private string GetPersistenceKey()
    {
        return !string.IsNullOrWhiteSpace(persistentId) ? persistentId : runtimeContainerId;
    }

    private string BuildRuntimeContainerId()
    {
        if (!string.IsNullOrWhiteSpace(persistentId)) return $"loot_{Sanitize(persistentId)}";
        var position = transform.position;
        return $"loot_{Sanitize(gameObject.scene.name)}_{Sanitize(name)}_{position.x:F2}_{position.y:F2}";
    }

    private static string Sanitize(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "unnamed";
        var builder = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var character = source[i];
            builder.Append(char.IsLetterOrDigit(character) || character == '_' || character == '-' ? character : '_');
        }
        return builder.ToString();
    }

    private void OnDrawGizmos()
    {
        var sightPoint = InteractionLineOfSightPoint;
        Gizmos.color = new Color(1f, 0.75f, 0.15f, 0.95f);
        Gizmos.DrawWireSphere(sightPoint, 0.08f);
        Gizmos.DrawLine(transform.position, sightPoint);

        if (!showSceneMarker) return;
        Gizmos.color = searched
            ? new Color(0.4f, 0.8f, 0.5f, 0.85f)
            : new Color(1f, 0.75f, 0.2f, 0.85f);
        var collider = GetComponent<Collider2D>();
        var size = collider != null ? (Vector2)collider.bounds.size : sceneMarkerSize;
        if (size.sqrMagnitude < 0.01f) size = sceneMarkerSize;
        Gizmos.DrawWireCube(transform.position, size);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * (size.y * 0.5f + 0.15f),
            $"可搜索：{displayName}（F{InteractionFloorIndex}）\nID: {persistentId}");
#endif
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.25f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.15f);
    }
}
