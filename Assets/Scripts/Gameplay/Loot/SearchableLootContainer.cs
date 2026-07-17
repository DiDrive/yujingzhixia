using System.Collections.Generic;
using System.Text;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SearchableLootContainer : MonoBehaviour, IWorldInteractable
{
    [Header("Identity")]
    [Tooltip("容器的持久化唯一 ID。正式地图建议手动填写且保持唯一；为空时会根据场景、名称和坐标自动生成。")]
    [SerializeField] private string persistentId;
    [Tooltip("交互提示中显示的容器名称。")]
    [SerializeField] private string displayName = "物资容器";
    [Header("Search")]
    [Tooltip("第一次搜索需要持续按住交互键的秒数。已经搜索过的容器再次打开不需要等待。")]
    [SerializeField] private float searchDuration = 1.5f;
    [Tooltip("使用的 JSON 掉落表 ID，对应 Resources/Data/loot_tables.json 中的 tableId。")]
    [SerializeField] private string lootTableId = "survival_basic";
    [Tooltip("覆盖掉落表的最少抽取次数。-1 表示使用 JSON 掉落表自己的配置。")]
    [SerializeField] private int minRolls = -1;
    [Tooltip("覆盖掉落表的最多抽取次数。-1 表示使用 JSON 掉落表自己的配置。")]
    [SerializeField] private int maxRolls = -1;
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

    private GridInventoryService gridInventory;
    private LootTableDatabase lootDatabase;
    private GridInventoryUnityUI spawnedContainerUi;
    private bool searched;
    private bool populated;
    private string runtimeContainerId;

    public string InteractionPrompt => searched ? $"打开 {displayName}" : $"搜索 {displayName}";
    public float InteractionHoldDuration => searched ? 0f : Mathf.Max(0f, searchDuration);
    public Transform InteractionTransform => transform;
    public bool HasBeenSearched => searched;
    public string RuntimeContainerId => runtimeContainerId;

    private void Awake()
    {
        ResolveServices();
        EnsureRegistered();
    }

    public bool CanInteract(PlayerInteractor2D interactor)
    {
        return isActiveAndEnabled && interactor != null;
    }

    public void Interact(PlayerInteractor2D interactor)
    {
        ResolveServices();
        if (!EnsureRegistered())
        {
            interactor.ShowFeedback("搜索容器初始化失败");
            return;
        }

        if (searched && closeOnInteractAgain && TryCloseOpenUi(interactor)) return;
        if (!populated) PopulateOnce();
        searched = true;
        OpenContainerUi(interactor);
    }

    private void ResolveServices()
    {
        if (gridInventory == null) gridInventory = FindObjectOfType<GridInventoryService>();
        if (lootDatabase == null) lootDatabase = FindObjectOfType<LootTableDatabase>();
    }

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
    }

    private void OpenContainerUi(PlayerInteractor2D interactor)
    {
        var mainUi = interactor.InventoryUi;
        if (containerUiSceneView != null)
        {
            containerUiSceneView.BindToContainer(gridInventory, runtimeContainerId, true);
            containerUiSceneView.SetOpen(true);
            if (mainUi != null) mainUi.SetOpen(true);
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

    private bool TryCloseOpenUi(PlayerInteractor2D interactor)
    {
        if (containerUiSceneView != null && containerUiSceneView.IsOpen())
        {
            containerUiSceneView.SetOpen(false);
            return true;
        }

        if (spawnedContainerUi != null && spawnedContainerUi.IsOpen())
        {
            spawnedContainerUi.SetOpen(false);
            return true;
        }

        var mainUi = interactor.InventoryUi;
        if (mainUi == null || !mainUi.IsContainerSidePanelOpen(runtimeContainerId)) return false;
        mainUi.HideContainerSidePanel(runtimeContainerId);
        return true;
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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = searched
            ? new Color(0.4f, 0.8f, 0.5f, 0.85f)
            : new Color(1f, 0.75f, 0.2f, 0.85f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);
    }
}
