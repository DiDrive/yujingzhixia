using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 网格背包与搜索容器共用的 UI 视图，负责布局、拖拽、操作按钮及逐物品搜索蒙版。
/// </summary>
public class GridInventoryUnityUI : MonoBehaviour
{
    /// <summary>
    /// 由搜索容器提供单个物品的揭示状态；返回 false 表示该物品不受搜索蒙版控制。
    /// </summary>
    public delegate bool ItemSearchStateProvider(string containerId, string instanceId, out bool revealed, out bool searching, out float progress);
    private const float DefaultToolbarHeight = 34f;
    private const float DefaultToolbarSpacing = 4f;

    [System.Serializable]
    private struct GridItemVisual
    {
        [Tooltip("物品ID，必须和 GridInventoryService 的 Item Definitions 中的 Item Id 一致。")]
        public string itemId;
        [Tooltip("该物品在背包格子里显示的图标。为空时使用纯色方块。")]
        public Sprite icon;
        [Tooltip("没有图标时的物品颜色；有图标时也会作为图标染色。Alpha 为0时会使用默认颜色。")]
        public Color tint;
    }

    [Header("Runtime")]
    [Tooltip("网格背包服务引用。为空时运行时会自动在场景中查找 GridInventoryService。")]
    [SerializeField] private GridInventoryService gridInventoryService;
    [Tooltip("游戏启动时是否默认显示背包界面。关闭后仍可用 Toggle Key 打开。")]
    [SerializeField] private bool startOpen;
    [Tooltip("等待外部容器在运行时完成绑定后再初始化。场景中预先摆好的搜索容器 UI 建议开启，避免 Awake 时按错误容器重建布局。")]
    [SerializeField] private bool deferInitializationUntilBound;
    [Tooltip("是否显示原型调试用的物品生成栏。正式游戏建议关闭，物品应通过拾取、掉落或奖励进入背包。")]
    [SerializeField] private bool showSpawnTools;
    [Tooltip("是否允许点击空格直接生成当前调试物品。仅在 Show Spawn Tools 开启时生效，正式游戏建议关闭。")]
    [SerializeField] private bool allowSpawnFromEmptyCells;
    [Tooltip("点击其他容器按钮时，是否在旁边打开容器窗口，而不是切换当前窗口。")]
    [SerializeField] private bool openOtherContainersAsSidePanels = true;
    [Tooltip("旁边打开的容器窗口相对当前背包面板的偏移。X 越大越靠右，Y 越大越靠上。")]
    [SerializeField] private Vector2 sidePanelOffset = new(260f, 0f);
    [Tooltip("是否允许拖拽标题栏移动背包窗口。主背包和旁边打开的口袋窗口都会生效。")]
    [SerializeField] private bool allowPanelDragging = true;
    [Tooltip("运行时使用更紧凑的背包布局，会自动套用较小的格子、按钮和间距；如果想完全手动调尺寸，可以关闭它。")]
    [SerializeField] private bool useCompactLayout = true;
    [Header("Screen Scaling")]
    [Tooltip("是否按参考分辨率自适应缩放UI。开启后会把 Canvas Scaler 设置为 Scale With Screen Size。")]
    [SerializeField] private bool scaleWithScreenSize = true;
    [Tooltip("UI参考分辨率。16:9项目通常用 1920x1080；移动端竖屏可改为 1080x1920。")]
    [SerializeField] private Vector2 referenceResolution = new(1920f, 1080f);
    [Tooltip("宽高缩放权重。0 表示主要匹配宽度，1 表示主要匹配高度，0.5 表示宽高均衡。")]
    [Range(0f, 1f)]
    [SerializeField] private float screenMatchMode = 0.5f;
    [Header("Layout Mode")]
    [Tooltip("是否运行时自动搭建UI结构。开启时脚本会自动创建面板、工具栏、网格和物品层；关闭后需要手动绑定下方 Override 引用。")]
    [SerializeField] private bool buildRuntimeLayout = true;
    [Tooltip("严格手动模式。开启且 Build Runtime Layout 关闭时，手动绑定不完整就报错，不会回退创建默认UI。")]
    [SerializeField] private bool strictManualMode = true;
    [Tooltip("是否自动根据网格宽度对齐工具栏、动作栏和网格位置。想在 Inspector 手动拖位置时建议关闭。")]
    [SerializeField] private bool autoAlignToolbarWithGrid = true;
    [Tooltip("网格尺寸变化时是否自动调整面板背景尺寸。开启后面板大小会随背包行列数、格子尺寸变化。")]
    [SerializeField] private bool autoResizePanelBackground = true;
    [Tooltip("手动绑定或预制体模式下，是否仍根据容器行列数自动调整网格及各显示层尺寸。")]
    [SerializeField] private bool autoResizeManualGridToContainer = true;
    [Tooltip("工具栏保底宽度。切到口袋这类小网格时，面板和工具栏不会小于这个宽度，避免按钮挤出背景。")]
    [SerializeField] private float minToolbarWidth = 380f;
    [Tooltip("面板背景右侧和底部的额外留白。X 控制右侧，Y 控制底部。")]
    [SerializeField] private Vector2 panelBackgroundPadding = new(28f, 28f);
    [Header("Background")]
    [Tooltip("面板背景图。为空时使用 Panel Background Color 纯色。")]
    [SerializeField] private Sprite panelBackgroundSprite;
    [Tooltip("网格区域背景图。为空时使用 Grid Background Color 纯色。")]
    [SerializeField] private Sprite gridBackgroundSprite;
    [Tooltip("面板背景颜色。只有 Panel Background Sprite 为空时直接生效。")]
    [SerializeField] private Color panelBackgroundColor = new(0.06f, 0.06f, 0.06f, 0.92f);
    [Tooltip("网格背景颜色。只有 Grid Background Sprite 为空时直接生效。")]
    [SerializeField] private Color gridBackgroundColor = new(0.1f, 0.1f, 0.1f, 0.75f);
    [Header("Inventory")]
    [Tooltip("默认展示的容器ID。要和 GridInventoryService 的 Container Id 一致；为空时自动取第一个容器。")]
    [SerializeField] private string currentContainerId = "main_bag";
    [Tooltip("背包界面开关按键。默认 Tab。")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [Header("Item Use")]
    [Tooltip("场景中的消耗品使用服务。为空时运行时只查找已有组件，不会自动创建。")]
    [SerializeField] private ConsumableItemUseService itemUseService;
    [Tooltip("编辑器中可调整的使用状态文字，例如“正在使用罐头 50%”。")]
    [SerializeField] private Text itemUseStatusText;
    [Tooltip("编辑器中可调整的使用进度填充图片。Image Type 应设置为 Filled。")]
    [SerializeField] private Image itemUseProgressFill;
    [Header("Sizing")]
    [Tooltip("单个背包格子的像素尺寸。数值越大背包越大；常用范围 36-56。")]
    [SerializeField] private float cellSize = 44f;
    [Tooltip("格子之间的像素间距。数值越大格子越分散；常用范围 2-6。")]
    [SerializeField] private float cellSpacing = 3f;
    [Tooltip("面板相对自身锚点的坐标。左上锚点时，X 向右为正，Y 向下通常为负。")]
    [SerializeField] private Vector2 panelAnchoredPosition = new(28f, -28f);
    [Tooltip("面板内左侧边距，影响工具栏和网格距离面板左边的距离。")]
    [SerializeField] private float panelLeftPadding = 14f;
    [Tooltip("从面板顶部到第一行工具栏的距离。数值越大，工具栏和网格整体越往下。")]
    [SerializeField] private float toolbarTopOffset = 50f;
    [Tooltip("工具栏、生成栏、动作栏每一行的高度。")]
    [SerializeField] private float toolbarHeight = 34f;
    [Tooltip("工具栏、生成栏、动作栏之间的垂直间距。")]
    [SerializeField] private float toolbarSpacing = 4f;
    [Tooltip("动作栏底部到网格顶部之间的垂直间距。")]
    [SerializeField] private float gridTopSpacing = 6f;
    [Header("Debug Spawn")]
    [Tooltip("调试生成栏显示的物品ID列表。仅 Show Spawn Tools 开启时显示。")]
    [SerializeField] private List<string> spawnItemIds = new() { "food_canned", "water_bottle", "bandage", "cloth_raw" };
    [Tooltip("运行时是否轻量修正生成下拉框和预览物品的位置，避免它们互相重叠或挤出生成栏。手动摆好且不重叠时不会改动。")]
    [SerializeField] private bool autoAvoidSpawnToolOverlap = true;
    [Tooltip("生成下拉框与预览物品自动避让时的间距。")]
    [SerializeField] private float spawnToolSpacing = 8f;
    [Header("Colors")]
    [Tooltip("空格子的显示颜色。")]
    [SerializeField] private Color emptyCellColor = new(0.12f, 0.12f, 0.12f, 0.88f);
    [Tooltip("已被物品占用的格子底色。物品图片会覆盖在上方。")]
    [SerializeField] private Color occupiedCellColor = new(0.2f, 0.3f, 0.42f, 0.9f);
    [Tooltip("当前选中物品的高亮颜色。")]
    [SerializeField] private Color selectedItemColor = new(0.88f, 0.75f, 0.33f, 0.95f);
    [Tooltip("拖拽时，如果当前位置可以放置，占用格子的预览颜色。")]
    [SerializeField] private Color previewValidColor = new(0.25f, 0.85f, 0.4f, 0.45f);
    [Tooltip("拖拽同类物品到可合并目标时，目标物品本体的高亮颜色。")]
    [SerializeField] private Color mergeTargetColor = new(0.25f, 0.95f, 0.45f, 1f);
    [Tooltip("拖拽时，如果当前位置不能放置，占用格子的冲突预览颜色。")]
    [SerializeField] private Color previewInvalidColor = new(0.95f, 0.2f, 0.2f, 0.45f);
    [Header("Item Visuals")]
    [Tooltip("物品图标和颜色映射表。每一项的 Item Id 必须和物品定义ID一致。")]
    [SerializeField] private List<GridItemVisual> itemVisuals = new();
    [Header("Item Search Mask")]
    [Tooltip("尚未轮到搜索的物品蒙版颜色。")]
    [SerializeField] private Color unrevealedItemMaskColor = new(0.045f, 0.045f, 0.055f, 0.98f);
    [Tooltip("当前正在搜索的物品蒙版颜色。")]
    [SerializeField] private Color searchingItemMaskColor = new(0.13f, 0.18f, 0.22f, 0.98f);
    [Tooltip("可选的搜索蒙版图片。为空时使用纯色方块；换 UI 时可在这里指定自己的蒙版 Sprite。")]
    [SerializeField] private Sprite itemSearchMaskSprite;
    [Tooltip("尚未搜索物品上显示的文字。")]
    [SerializeField] private string unrevealedItemText = "?";
    [Tooltip("当前物品搜索文字格式。{0} 会替换为百分比，例如：搜索中\\n{0:P0}。")]
    [SerializeField] private string searchingItemTextFormat = "搜索中\n{0:P0}";
    [Tooltip("搜索蒙版文字颜色。")]
    [SerializeField] private Color itemSearchTextColor = Color.white;
    [Tooltip("搜索蒙版文字字号。")]
    [Range(8, 40)] [SerializeField] private int itemSearchTextFontSize = 14;
    [Header("Manual Layout Overrides")]
    [Tooltip("手动布局模式下的标题文本组件。Build Runtime Layout 关闭时使用。")]
    [SerializeField] private Text titleTextOverride;
    [Tooltip("手动布局模式下的容器按钮栏 Root。")]
    [SerializeField] private RectTransform toolbarRootOverride;
    [Tooltip("手动布局模式下的调试生成栏 Root。Show Spawn Tools 开启时使用。")]
    [SerializeField] private RectTransform spawnRootOverride;
    [Tooltip("手动布局模式下的动作按钮栏 Root，比如整理、旋转、删除、拆分按钮所在父节点。")]
    [SerializeField] private RectTransform actionRootOverride;
    [Tooltip("是否显示容器切换按钮栏。旁边打开的口袋窗口会自动关闭这个栏，只显示口袋内容。")]
    [SerializeField] private bool showContainerToolbar = true;
    [Tooltip("是否显示整理、旋转、删除、拆分动作栏。旁边打开的口袋窗口会自动关闭这个栏。")]
    [SerializeField] private bool showActionToolbar = true;
    [Tooltip("手动布局模式下的网格 Root，控制格子区域的位置和大小。")]
    [SerializeField] private RectTransform gridRootOverride;
    [Tooltip("手动布局模式下的工具区总 Root。例如把 Toolbar、Spawn、Action 放到一个 ToolRoot 下。")]
    [SerializeField] private RectTransform toolRootOverride;
    [Tooltip("是否让工具区总 Root 随网格高度自动下移，避免背包行数变多后工具区和网格重叠。想手动摆放时可关闭。")]
    [SerializeField] private bool autoRepositionToolRoot = true;
    [Tooltip("工具区总 Root 与网格底部的垂直间距。仅 Auto Reposition Tool Root 开启时生效。")]
    [SerializeField] private float toolRootSpacingFromGrid = 12f;
    [Tooltip("手动布局模式下面板背景 Image。用于替换面板底图或背景色。")]
    [SerializeField] private Image panelBackgroundOverride;
    [Tooltip("手动布局模式下网格背景 Image。用于替换网格区域底图或背景色。")]
    [SerializeField] private Image gridBackgroundOverride;
    [Tooltip("手动布局模式下的格子层 Root。脚本会在这里创建每个格子的底色和点击区域。")]
    [SerializeField] private RectTransform cellLayerRootOverride;
    [Tooltip("手动布局模式下的物品层 Root。脚本会在这里创建/刷新物品图标。")]
    [SerializeField] private RectTransform itemLayerRootOverride;
    [Tooltip("手动布局模式下的拖拽预览层 Root。拖拽幽灵和预览会显示在这里。")]
    [SerializeField] private RectTransform ghostRootOverride;
    [Tooltip("优先绑定你手动创建的按钮。开启后会尝试根据按钮名称/文本绑定功能；没找到整理按钮时仍会自动补一个整理按钮。")]
    [SerializeField] private bool preferExistingButtons = true;

    private Canvas canvas;
    private RectTransform panelRoot;
    private RectTransform toolbarRoot;
    private RectTransform spawnRoot;
    private RectTransform actionRoot;
    private RectTransform gridRoot;
    private RectTransform toolRoot;
    private RectTransform cellLayerRoot;
    private RectTransform itemLayerRoot;
    private RectTransform ghostRoot;
    private Text titleText;
    private Image panelBackgroundImage;
    private Image gridBackgroundImage;
    private string selectedSpawnItemId = "food_canned";
    private string selectedInstanceId;
    private string draggingInstanceId;
    private string draggingSpawnItemId;
    private GridItemRotation draggingRotation;
    private bool previewCanPlace;
    private int previewRow = -1;
    private int previewColumn = -1;
    private int previewWidth = 1;
    private int previewHeight = 1;
    private int previewMergeRow = -1;
    private int previewMergeColumn = -1;
    private int previewMergeWidth = 1;
    private int previewMergeHeight = 1;
    private string previewMergeInstanceId;
    private int dragHoverRow = -1;
    private int dragHoverColumn = -1;
    private int dragPointerOffsetRow;
    private int dragPointerOffsetColumn;
    private readonly Dictionary<string, GridInventoryUnityUIItem> itemViewByInstanceId = new();
    private readonly HashSet<string> displayedInstanceIds = new();
    private readonly List<string> staleItemIds = new();
    private readonly List<GameObject> toolbarButtons = new();
    private readonly List<GameObject> spawnButtons = new();
    private readonly List<GameObject> actionButtons = new();
    private Dropdown spawnDropdown;
    private GridInventoryUnityUISpawnItem spawnDragHandler;
    private Text spawnDragLabel;
    private Image spawnDragImage;
    private bool hasAutoSortActionButton;
    private Image[,] cellImages;
    private RectTransform previewGhostRect;
    private Image previewGhostImage;
    private Text previewGhostText;
    private RectTransform contextMenuRoot;
    private Button splitContextButton;
    private Button rotateContextButton;
    private Button closeButton;
    private InputField splitCountInput;
    private Text splitCountLabel;
    private string contextMenuInstanceId;
    private int contextMenuMaxSplitCount = 1;
    private readonly Dictionary<string, GridItemVisual> visualByItemId = new();
    private bool initialized;
    private bool hasExplicitRuntimeBinding;
    private string displayTitleOverride;
    private bool startVisibilityApplied;
    private bool isSideContainerView;
    private GridInventoryUnityUI ownerView;
    private string previewContainerId;
    private bool toolRootBaseCaptured;
    private bool editorItemViewsRegistered;
    private float toolRootBaseX;
    private float toolRootBaseZ;
    private readonly Dictionary<string, GridInventoryUnityUI> sideContainerViews = new();
    private static readonly List<GridInventoryUnityUI> ActiveViews = new();
    private static GridInventoryUnityUI activeDragOwner;
    private ItemSearchStateProvider itemSearchStateProvider;

    private bool UsesManualLayout => !buildRuntimeLayout;
    private bool UsesEditorToolbarLayout => UsesManualLayout && toolRoot != null && toolRoot.GetComponent<VerticalLayoutGroup>() != null;
    public string ContainerId => currentContainerId;
    public bool IsSideContainerView => isSideContainerView;

    private static readonly string[] ToolLayoutChildNames =
    {
        "ContainerToolbar",
        "SpawnToolbar",
        "ActionToolbar",
        "ToolbarRoot",
        "SpawnRoot",
        "ActionRoot"
    };

    private void Awake()
    {
        // 搜索容器 UI 需要在编辑模式保持可见，方便直接调整布局；
        // 进入游戏后则必须先隐藏，直到对应容器完成搜索并显式绑定。
        if (deferInitializationUntilBound && !hasExplicitRuntimeBinding && !startOpen)
        {
            gameObject.SetActive(false);
            return;
        }

        TryInitialize();
    }

    private void OnEnable()
    {
        if (!ActiveViews.Contains(this)) ActiveViews.Add(this);
    }

    private void OnDisable()
    {
        ActiveViews.Remove(this);
        if (activeDragOwner == this) activeDragOwner = null;
    }

    private void OnDestroy()
    {
        ActiveViews.Remove(this);
        if (ownerView != null && !string.IsNullOrWhiteSpace(currentContainerId))
        {
            ownerView.sideContainerViews.Remove(currentContainerId);
        }
        if (activeDragOwner == this) activeDragOwner = null;
    }

    private void Update()
    {
        if (!initialized && !TryInitialize()) return;

        if (!isSideContainerView && Input.GetKeyDown(toggleKey))
        {
            if (canvas != null)
            {
                canvas.enabled = !canvas.enabled;
                if (!canvas.enabled) HideContextMenu();
            }
        }

        if (canvas == null || !canvas.enabled) return;
        if (gridInventoryService == null) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isSideContainerView)
            {
                SetOpen(false);
                return;
            }
            selectedInstanceId = null;
            HideContextMenu();
        }
        else if (Input.GetMouseButtonDown(1) && !IsPointerOverUi())
        {
            selectedInstanceId = null;
            HideContextMenu();
        }

        if (!string.IsNullOrWhiteSpace(draggingInstanceId) || !string.IsNullOrWhiteSpace(draggingSpawnItemId))
        {
            UpdateDragPreview(Input.mousePosition);
            if (Input.GetKeyDown(KeyCode.R)) RotateDragPreview();
        }

        RefreshItems();
        RefreshGridColors();
        RefreshTitle();
        RefreshItemUseStatus();
        UpdateToolRootPosition();
    }

    private bool TryInitialize()
    {
        if (initialized) return true;
        if (deferInitializationUntilBound && !hasExplicitRuntimeBinding) return false;
        ResolvePrebuiltSideContainerState();
        if (gridInventoryService == null) gridInventoryService = FindObjectOfType<GridInventoryService>();
        if (gridInventoryService == null) return false;
        if (itemUseService == null) itemUseService = FindObjectOfType<ConsumableItemUseService>();
        gridInventoryService.EnsureInitialized();
        NormalizeToolbarSettings();
        ApplyCompactLayoutDefaults();
        BuildVisualMap();
        EnsureEventSystem();
        EnsureLayout();
        EnsureCloseButton();
        if (toolbarRoot == null || spawnRoot == null || actionRoot == null || gridRoot == null || itemLayerRoot == null || ghostRoot == null) return false;
        ResolveContainerId();
        RebuildStaticButtons();
        RebuildGrid();
        RefreshItems();
        EnsureCloseButton();
        initialized = true;
        ApplyStartVisibility();
        return true;
    }

    public void Bind(GridInventoryService service)
    {
        if (service == null) return;
        hasExplicitRuntimeBinding = true;
        var serviceChanged = gridInventoryService != service;
        gridInventoryService = service;
        gridInventoryService.EnsureInitialized();
        if (!initialized) return;

        if (serviceChanged)
        {
            ResolveContainerId();
            RebuildStaticButtons();
            RebuildGrid();
        }
        RefreshItems();
    }

    /// <summary>
    /// 供背包或搜索容器 UI 预制体绑定任意运行时容器。
    /// </summary>
    public void BindToContainer(GridInventoryService service, string containerId, bool sideContainer = true, string displayTitle = null)
    {
        if (service == null || string.IsNullOrWhiteSpace(containerId)) return;
        hasExplicitRuntimeBinding = true;
        displayTitleOverride = displayTitle;
        gridInventoryService = service;
        gridInventoryService.EnsureInitialized();
        currentContainerId = containerId;
        isSideContainerView = sideContainer;
        if (sideContainer)
        {
            openOtherContainersAsSidePanels = false;
            showContainerToolbar = false;
            showSpawnTools = false;
            allowSpawnFromEmptyCells = false;
        }

        if (!initialized)
        {
            TryInitialize();
            return;
        }

        selectedInstanceId = null;
        HideContextMenu();
        RebuildStaticButtons();
        RebuildGrid();
        RefreshItems();
        RefreshTitle();
    }

    /// <summary>
    /// 注入当前容器的逐物品搜索状态，使通用背包 UI 无需依赖具体的搜索容器实现。
    /// </summary>
    public void SetItemSearchStateProvider(ItemSearchStateProvider provider)
    {
        itemSearchStateProvider = provider;
    }

    public bool ShowContainerAsSidePanel(string containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId)) return false;
        if (!initialized && !TryInitialize()) return false;
        if (gridInventoryService == null || !gridInventoryService.TryGetContainerSize(containerId, out _, out _)) return false;
        SetOpen(true);
        return SetSideContainerVisible(containerId, true);
    }

    public bool ShowContainerAsSidePanel(string containerId, Vector2 panelOffset, float targetCellSize, float targetCellSpacing)
    {
        if (string.IsNullOrWhiteSpace(containerId)) return false;
        if (!initialized && !TryInitialize()) return false;
        if (gridInventoryService == null || !gridInventoryService.TryGetContainerSize(containerId, out _, out _)) return false;
        SetOpen(true);
        return SetSideContainerVisible(containerId, true, true, panelOffset, targetCellSize, targetCellSpacing);
    }

    public void HideContainerSidePanel(string containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId)) return;
        SetSideContainerVisible(containerId, false);
    }

    public bool IsContainerSidePanelOpen(string containerId)
    {
        return !string.IsNullOrWhiteSpace(containerId) &&
               sideContainerViews.TryGetValue(containerId, out var view) &&
               view != null &&
               view.IsOpen();
    }

    public void ApplyExternalContainerLayout(Vector2 panelOffset, float targetCellSize, float targetCellSpacing)
    {
        cellSize = Mathf.Clamp(targetCellSize, 20f, 96f);
        cellSpacing = Mathf.Clamp(targetCellSpacing, 0f, 16f);
        var rect = panelRoot != null ? panelRoot : transform as RectTransform;
        if (rect != null)
        {
            if (ownerView != null && ownerView.panelRoot != null)
            {
                rect.anchoredPosition = ownerView.panelRoot.anchoredPosition + panelOffset;
            }
            else
            {
                rect.anchoredPosition = panelOffset;
            }
        }

        if (!initialized) return;
        RebuildGrid();
        RefreshItems();
    }

    /// <summary>
    /// 背包隐藏时也同步场景 Item_* 节点的运行时交互绑定。
    /// </summary>
    public void RefreshEditorItemBindings(GridInventoryService service)
    {
        if (service == null) return;
        gridInventoryService = service;
        if (!TryInitialize()) return;
        editorItemViewsRegistered = false;
        RegisterEditorItemViews();
        RefreshItems();
        RefreshGridColors();
    }

    private void ConfigureSideContainerView(GridInventoryUnityUI source, GridInventoryService service, string containerId, bool openOnStart = true)
    {
        ownerView = source;
        isSideContainerView = true;
        gridInventoryService = service;
        currentContainerId = containerId;
        startOpen = openOnStart;
        showSpawnTools = false;
        allowSpawnFromEmptyCells = false;
        openOtherContainersAsSidePanels = false;
        allowPanelDragging = source.allowPanelDragging;
        showContainerToolbar = false;
        showActionToolbar = false;
        useCompactLayout = source.useCompactLayout;
        scaleWithScreenSize = source.scaleWithScreenSize;
        referenceResolution = source.referenceResolution;
        screenMatchMode = source.screenMatchMode;
        buildRuntimeLayout = true;
        strictManualMode = false;
        autoAlignToolbarWithGrid = true;
        // 外部物资容器必须按自身行列数收缩，不能继承主背包的固定大面板尺寸。
        autoResizePanelBackground = true;
        minToolbarWidth = Mathf.Min(source.minToolbarWidth, 180f);
        panelBackgroundPadding = source.panelBackgroundPadding;
        panelBackgroundSprite = source.panelBackgroundSprite;
        gridBackgroundSprite = source.gridBackgroundSprite;
        panelBackgroundColor = source.panelBackgroundColor;
        gridBackgroundColor = source.gridBackgroundColor;
        toggleKey = source.toggleKey;
        cellSize = source.cellSize;
        cellSpacing = source.cellSpacing;
        panelLeftPadding = source.panelLeftPadding;
        toolbarTopOffset = source.toolbarTopOffset;
        toolbarHeight = source.toolbarHeight;
        toolbarSpacing = source.toolbarSpacing;
        gridTopSpacing = source.gridTopSpacing;
        emptyCellColor = source.emptyCellColor;
        occupiedCellColor = source.occupiedCellColor;
        selectedItemColor = source.selectedItemColor;
        previewValidColor = source.previewValidColor;
        mergeTargetColor = source.mergeTargetColor;
        previewInvalidColor = source.previewInvalidColor;
        itemVisuals = new List<GridItemVisual>(source.itemVisuals);
        preferExistingButtons = false;
        autoRepositionToolRoot = false;
    }

    private void ResolvePrebuiltSideContainerState()
    {
        if (isSideContainerView) return;
        if (!name.StartsWith("InventoryUI_")) return;
        if (name == "InventoryUI_main_bag" || currentContainerId == "main_bag") return;
        isSideContainerView = true;
        openOtherContainersAsSidePanels = false;
        showContainerToolbar = false;
        showSpawnTools = false;
        showActionToolbar = false;
        if (gridRootOverride == null || toolbarRootOverride == null || spawnRootOverride == null || actionRootOverride == null || itemLayerRootOverride == null || ghostRootOverride == null)
        {
            buildRuntimeLayout = true;
            strictManualMode = false;
        }
    }

    public void SetOpen(bool open)
    {
        if (!initialized && !TryInitialize()) return;
        if (isSideContainerView && canvas != null && canvas.gameObject != gameObject)
        {
            if (panelRoot != null) panelRoot.gameObject.SetActive(open);
        }
        else if (canvas != null)
        {
            canvas.enabled = open;
        }
        if (open || isSideContainerView) return;
        foreach (var sideView in sideContainerViews.Values)
        {
            if (sideView != null) sideView.SetOpen(false);
        }
    }

    public bool IsOpen()
    {
        if (!initialized && !TryInitialize()) return false;
        if (isSideContainerView && canvas != null && canvas.gameObject != gameObject)
        {
            return panelRoot != null && panelRoot.gameObject.activeSelf;
        }
        return canvas != null && canvas.enabled;
    }

    public void ToggleOpen()
    {
        if (!initialized && !TryInitialize()) return;
        SetOpen(!IsOpen());
    }

    private void ApplyStartVisibility()
    {
        if (startVisibilityApplied) return;
        startVisibilityApplied = true;
        SetOpen(startOpen);
    }

    private void ApplyCompactLayoutDefaults()
    {
        // 编辑器已搭好的手动布局必须保留原尺寸，不能在运行时改成紧凑网格。
        if (!useCompactLayout || !buildRuntimeLayout) return;
        cellSize = 44f;
        cellSpacing = 3f;
        panelLeftPadding = 14f;
        toolbarTopOffset = 50f;
        toolbarHeight = 34f;
        toolbarSpacing = 4f;
        gridTopSpacing = 6f;
        minToolbarWidth = 380f;
        panelBackgroundPadding = new Vector2(28f, 28f);
    }

    private void BuildVisualMap()
    {
        visualByItemId.Clear();
        for (var i = 0; i < itemVisuals.Count; i++)
        {
            var visual = itemVisuals[i];
            if (string.IsNullOrWhiteSpace(visual.itemId)) continue;
            visualByItemId[visual.itemId] = visual;
        }
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        var eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private void EnsureLayout()
    {
        if (!buildRuntimeLayout)
        {
            if (TryBindManualLayout()) return;
            if (strictManualMode)
            {
                Debug.LogError("GridInventoryUnityUI: 手动布局绑定失败。请检查所有 Override Root 是否已正确挂载。", this);
                return;
            }
        }

        canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();
        EnsureCanvasScalerMode();

        panelRoot = GetComponent<RectTransform>();
        if (panelRoot == null || panelRoot == canvas.transform)
        {
            panelRoot = CreateRect("Panel", transform, new Vector2(860f, 760f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), panelAnchoredPosition);
        }
        else
        {
            panelRoot.sizeDelta = new Vector2(860f, 760f);
        }

        panelBackgroundImage = panelRoot.GetComponent<Image>();
        if (panelBackgroundImage == null) panelBackgroundImage = panelRoot.gameObject.AddComponent<Image>();
        ApplyBackgroundStyle(panelBackgroundImage, panelBackgroundSprite, panelBackgroundColor);

        titleText = GetOrCreateTitleText(panelRoot);
        EnsurePanelDragHandle();
        toolRoot = GetOrCreateRect("toolRoot", panelRoot, new Vector2(820f, toolbarHeight * 3f + toolbarSpacing * 2f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(panelLeftPadding, -toolbarTopOffset));
        ClearToolLayoutChildren(toolRoot);
        toolbarRoot = CreateRect("ContainerToolbar", toolRoot, new Vector2(820f, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
        spawnRoot = CreateRect("SpawnToolbar", toolRoot, new Vector2(820f, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -(toolbarHeight + toolbarSpacing)));
        actionRoot = CreateRect("ActionToolbar", toolRoot, new Vector2(820f, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -(toolbarHeight + toolbarSpacing) * 2f));

        gridRoot = GetOrCreateRect("GridRoot", panelRoot, new Vector2(820f, 560f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(panelLeftPadding, -(toolbarTopOffset + (toolbarHeight + toolbarSpacing) * 3f + gridTopSpacing)));
        gridBackgroundImage = gridRoot.GetComponent<Image>();
        if (gridBackgroundImage == null) gridBackgroundImage = gridRoot.gameObject.AddComponent<Image>();
        ApplyBackgroundStyle(gridBackgroundImage, gridBackgroundSprite, gridBackgroundColor);
        cellLayerRoot = GetOrCreateRect("CellLayer", panelRoot, gridRoot.sizeDelta, gridRoot.anchorMin, gridRoot.anchorMax, gridRoot.pivot, gridRoot.anchoredPosition);
        itemLayerRoot = GetOrCreateRect("ItemLayer", panelRoot, gridRoot.sizeDelta, gridRoot.anchorMin, gridRoot.anchorMax, gridRoot.pivot, gridRoot.anchoredPosition);
        ghostRoot = GetOrCreateRect("GhostLayer", panelRoot, gridRoot.sizeDelta, gridRoot.anchorMin, gridRoot.anchorMax, gridRoot.pivot, gridRoot.anchoredPosition);
        cellLayerRoot.gameObject.SetActive(true);
        itemLayerRoot.gameObject.SetActive(true);
        ghostRoot.gameObject.SetActive(true);
        ClearChildren(cellLayerRoot);
        ClearRuntimeItemChildren();
        ClearChildren(ghostRoot);
        ApplyInventoryLayerOrder();
    }

    private Text GetOrCreateTitleText(RectTransform root)
    {
        var existing = root.Find("TitleText");
        Text text = null;
        RectTransform rect;
        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            text = existing.GetComponent<Text>();
            rect = existing as RectTransform;
        }
        else
        {
            text = CreateText("TitleText", root, "Grid Inventory", 24, TextAnchor.MiddleLeft);
            rect = text.rectTransform;
        }

        if (text == null) text = existing.gameObject.AddComponent<Text>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(20f, -14f);
        rect.sizeDelta = new Vector2(-40f, 36f);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 24;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        return text;
    }

    private void ClearToolLayoutChildren(RectTransform root)
    {
        if (root == null) return;
        root.gameObject.SetActive(true);
        for (var i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i);
            if (!IsToolLayoutChildName(child.name)) continue;
            Destroy(child.gameObject);
        }
    }

    private bool IsToolLayoutChildName(string objectName)
    {
        for (var i = 0; i < ToolLayoutChildNames.Length; i++)
        {
            if (objectName == ToolLayoutChildNames[i]) return true;
        }
        return false;
    }

    private bool TryBindManualLayout()
    {
        if (gridRootOverride == null || toolRootOverride == null) return false;
        if (toolbarRootOverride == null || spawnRootOverride == null || actionRootOverride == null) return false;
        if (cellLayerRootOverride == null || itemLayerRootOverride == null || ghostRootOverride == null) return false;

        gridRoot = gridRootOverride;
        toolbarRoot = toolbarRootOverride;
        spawnRoot = spawnRootOverride;
        actionRoot = actionRootOverride;
        toolRoot = toolRootOverride;
        itemLayerRoot = itemLayerRootOverride;
        ghostRoot = ghostRootOverride;
        cellLayerRoot = cellLayerRootOverride != null ? cellLayerRootOverride : gridRoot;
        titleText = titleTextOverride;
        panelRoot = gridRoot.parent as RectTransform;
        panelBackgroundImage = panelBackgroundOverride != null ? panelBackgroundOverride : panelRoot != null ? panelRoot.GetComponent<Image>() : null;
        gridBackgroundImage = gridBackgroundOverride != null ? gridBackgroundOverride : gridRoot.GetComponent<Image>();
        if (panelBackgroundImage == null && panelRoot != null) panelBackgroundImage = panelRoot.gameObject.AddComponent<Image>();
        if (gridBackgroundImage == null && gridRoot != null) gridBackgroundImage = gridRoot.gameObject.AddComponent<Image>();
        if (panelBackgroundImage != null) ApplyBackgroundStyle(panelBackgroundImage, panelBackgroundSprite, panelBackgroundColor);
        if (gridBackgroundImage != null) ApplyBackgroundStyle(gridBackgroundImage, gridBackgroundSprite, gridBackgroundColor);
        EnsurePanelDragHandle();
        canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        EnsureCanvasScalerMode();
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        ApplyInventoryLayerOrder();
        RefreshEditorToolbarLayout();
        return true;
    }

    private void NormalizeToolbarSettings()
    {
        if (toolbarHeight >= 1f) return;

        toolbarHeight = DefaultToolbarHeight;
        if (toolbarSpacing <= 0f) toolbarSpacing = DefaultToolbarSpacing;
        Debug.LogWarning("GridInventoryUnityUI: 检测到零高度工具栏，已恢复为默认行高和间距。", this);
    }

    private void RefreshEditorToolbarLayout()
    {
        if (!UsesEditorToolbarLayout) return;

        ConfigureToolbarRowLayout(toolbarRoot);
        ConfigureToolbarRowLayout(spawnRoot);
        ConfigureToolbarRowLayout(actionRoot);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(toolRoot);
    }

    private void ConfigureToolbarRowLayout(RectTransform row)
    {
        if (row == null) return;

        var element = row.GetComponent<LayoutElement>();
        if (element == null) element = row.gameObject.AddComponent<LayoutElement>();
        element.minHeight = toolbarHeight;
        element.preferredHeight = toolbarHeight;
        element.flexibleHeight = 0f;

        var layout = row.GetComponent<HorizontalLayoutGroup>();
        if (layout == null) return;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    private void RefreshTitle()
    {
        if (titleText == null) return;
        var selectedText = string.IsNullOrWhiteSpace(selectedInstanceId) ? "未选择" : GetSelectedItemTitle(selectedInstanceId);
        if (isSideContainerView && !string.IsNullOrWhiteSpace(displayTitleOverride))
        {
            titleText.text = string.IsNullOrWhiteSpace(selectedInstanceId)
                ? displayTitleOverride
                : $"{displayTitleOverride}  {selectedText}";
            return;
        }
        titleText.text = $"背包  [{GetContainerTitle(currentContainerId)}]  {selectedText}";
    }

    private void RebuildStaticButtons()
    {
        if (gridInventoryService == null) return;
        if (toolbarRoot == null || spawnRoot == null || actionRoot == null) return;
        ClearButtonObjects(toolbarButtons);
        ClearButtonObjects(spawnButtons);
        ClearButtonObjects(actionButtons);
        spawnDropdown = null;
        spawnDragHandler = null;
        spawnDragLabel = null;
        spawnDragImage = null;
        hasAutoSortActionButton = false;

        if (toolbarRoot != null) toolbarRoot.gameObject.SetActive(showContainerToolbar);
        if (spawnRoot != null) spawnRoot.gameObject.SetActive(showSpawnTools);
        if (actionRoot != null) actionRoot.gameObject.SetActive(showActionToolbar);

        var toolbarHandled = !showContainerToolbar || preferExistingButtons && TryBindToolbarButtonsFromExisting();
        var spawnHandled = !showSpawnTools || preferExistingButtons && TryBindSpawnButtonsFromExisting() || CreateSpawnSelector();
        var actionHandled = !showActionToolbar || preferExistingButtons && TryBindActionButtonsFromExisting();

        if (!buildRuntimeLayout && strictManualMode)
        {
            if (showContainerToolbar && !toolbarHandled) Debug.LogWarning("GridInventoryUnityUI: 严格手动模式下未在 ToolbarRoot 绑定到任何容器按钮。", this);
            if (showSpawnTools && !spawnHandled) Debug.LogWarning("GridInventoryUnityUI: 严格手动模式下未在 SpawnRoot 绑定到任何生成按钮。", this);
            if (showActionToolbar && !actionHandled) Debug.LogWarning("GridInventoryUnityUI: 严格手动模式下未在 ActionRoot 绑定到任何动作按钮。", this);
            RefreshEditorToolbarLayout();
            return;
        }

        if (showContainerToolbar && !toolbarHandled)
        {
            var containerX = 0f;
            foreach (var containerId in gridInventoryService.PlayerContainerIds)
            {
                var capturedContainer = containerId;
                var button = CreateButton(toolbarRoot, containerId, new Vector2(120f, 34f), new Vector2(containerX, -2f), () =>
                {
                    SwitchToContainer(capturedContainer);
                });
                toolbarButtons.Add(button.gameObject);
                containerX += 126f;
            }
        }

        if (!showActionToolbar) return;

        var actionX = actionHandled ? GetNextActionButtonX() : 0f;
        if (!hasAutoSortActionButton)
        {
            actionButtons.Add(CreateAutoSortButton(actionX).gameObject);
            actionX += 92f;
        }

        if (actionHandled) return;

        var useButton = CreateButton(actionRoot, "使用", new Vector2(86f, 28f), new Vector2(actionX, -2f), () =>
        {
            ExecuteUseSelected();
        });
        actionButtons.Add(useButton.gameObject);
        actionX += 92f;

        var rotateButton = CreateButton(actionRoot, "旋转", new Vector2(86f, 28f), new Vector2(actionX, -2f), () =>
        {
            ExecuteRotateSelected();
        });
        actionButtons.Add(rotateButton.gameObject);
        actionX += 92f;

        var removeButton = CreateButton(actionRoot, "删除", new Vector2(86f, 28f), new Vector2(actionX, -2f), () =>
        {
            ExecuteRemoveSelected();
        });
        actionButtons.Add(removeButton.gameObject);
        actionX += 92f;

        var splitButton = CreateButton(actionRoot, "拆分", new Vector2(86f, 28f), new Vector2(actionX, -2f), () =>
        {
            ExecuteSplitOne();
        });
        actionButtons.Add(splitButton.gameObject);
        RefreshEditorToolbarLayout();
    }

    private Button CreateAutoSortButton(float x)
    {
        var button = CreateButton(actionRoot, "整理", new Vector2(86f, 28f), new Vector2(x, -2f), () =>
        {
            ExecuteAutoSort();
        });
        hasAutoSortActionButton = true;
        return button;
    }

    private float GetNextActionButtonX()
    {
        if (actionRoot == null) return 0f;
        var maxRight = 0f;
        for (var i = 0; i < actionRoot.childCount; i++)
        {
            var child = actionRoot.GetChild(i) as RectTransform;
            if (child == null) continue;
            maxRight = Mathf.Max(maxRight, child.anchoredPosition.x + child.sizeDelta.x + 6f);
        }
        return maxRight;
    }

    private void RebuildGrid()
    {
        if (gridInventoryService == null) return;
        if (gridRoot == null) return;
        if (string.IsNullOrWhiteSpace(currentContainerId)) return;
        if (!gridInventoryService.TryGetContainerSize(currentContainerId, out var rows, out var columns)) return;
        if (cellLayerRoot == null) cellLayerRoot = gridRoot;
        if (itemLayerRoot == null) return;
        if (ghostRoot == null) return;

        ClearChildren(cellLayerRoot);
        ClearRuntimeItemChildren();
        ClearChildren(ghostRoot);
        itemViewByInstanceId.Clear();
        editorItemViewsRegistered = false;
        HideContextMenu();
        previewGhostRect = null;
        previewGhostImage = null;
        previewGhostText = null;

        var contentWidth = columns * cellSize + Mathf.Max(0, columns - 1) * cellSpacing;
        var contentHeight = rows * cellSize + Mathf.Max(0, rows - 1) * cellSpacing;
        var usesManualGridSize = !buildRuntimeLayout && gridRootOverride != null && !autoResizeManualGridToContainer;
        if (!usesManualGridSize)
        {
            gridRoot.sizeDelta = new Vector2(contentWidth, contentHeight);
        }

        var width = usesManualGridSize ? gridRoot.sizeDelta.x : contentWidth;
        var height = usesManualGridSize ? gridRoot.sizeDelta.y : contentHeight;
        var toolbarWidth = GetToolbarRequiredWidth(width);
        if (autoResizePanelBackground && panelRoot != null)
        {
            var requiredWidth = panelLeftPadding + Mathf.Max(width, toolbarWidth) + panelBackgroundPadding.x;
            var requiredHeight = GetGridTopOffset() + height + panelBackgroundPadding.y;
            panelRoot.sizeDelta = new Vector2(requiredWidth, requiredHeight);
        }
        if (buildRuntimeLayout && autoAlignToolbarWithGrid) AlignToolbarAndGrid(toolbarWidth);
        SyncLayerToGrid(cellLayerRoot);
        SyncLayerToGrid(itemLayerRoot);
        SyncLayerToGrid(ghostRoot);
        UpdateToolRootPosition();
        ApplyInventoryLayerOrder();

        cellImages = new Image[rows, columns];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var cellRect = CreateRect($"Cell_{row}_{column}", cellLayerRoot, new Vector2(cellSize, cellSize), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(column * (cellSize + cellSpacing), -row * (cellSize + cellSpacing)));
                var image = cellRect.gameObject.AddComponent<Image>();
                image.color = emptyCellColor;
                var button = cellRect.gameObject.AddComponent<Button>();
                var capturedRow = row;
                var capturedColumn = column;
                button.onClick.AddListener(() => OnCellClicked(capturedRow, capturedColumn));
                cellImages[row, column] = image;
            }
        }
    }

    private void RefreshGridColors()
    {
        if (cellImages == null) return;
        if (!gridInventoryService.TryGetContainerSize(currentContainerId, out var rows, out var columns)) return;

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var occupied = gridInventoryService.IsCellOccupied(currentContainerId, row, column);
                var color = occupied ? occupiedCellColor : emptyCellColor;
                if (!string.IsNullOrWhiteSpace(selectedInstanceId) && gridInventoryService.TryGetCellOccupantId(currentContainerId, row, column, out var occupantId) && occupantId == selectedInstanceId)
                {
                    color = selectedItemColor;
                }
                var dragOwner = activeDragOwner != null ? activeDragOwner : this;
                if (dragOwner.IsDraggingItem() && dragOwner.previewContainerId == currentContainerId && dragOwner.IsCellInsidePreviewFootprint(row, column))
                {
                    color = dragOwner.previewCanPlace ? previewValidColor : previewInvalidColor;
                }
                if (dragOwner.IsDraggingItem() && dragOwner.previewContainerId == currentContainerId && dragOwner.IsCellInsideMergePreviewFootprint(row, column))
                {
                    color = previewValidColor;
                }
                cellImages[row, column].color = color;
            }
        }
    }

    private void RefreshItems()
    {
        if (string.IsNullOrWhiteSpace(currentContainerId)) return;
        if (!gridInventoryService.TryGetContainerSize(currentContainerId, out _, out _)) return;

        RegisterEditorItemViews();

        displayedInstanceIds.Clear();
        foreach (var instance in gridInventoryService.ItemInstances.Values)
        {
            if (instance.containerId != currentContainerId || instance.originRow < 0 || instance.originColumn < 0) continue;
            if (!gridInventoryService.TryGetItemDefinition(instance.itemId, out var definition)) continue;
            if (!GridPlacementSolver.TryGetFootprint(definition, instance.rotation, out var width, out var height)) continue;

            if (!itemViewByInstanceId.TryGetValue(instance.instanceId, out var view))
            {
                var itemRect = CreateRect($"Item_{instance.instanceId}", itemLayerRoot, new Vector2(100f, 100f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
                var image = itemRect.gameObject.AddComponent<Image>();
                var button = itemRect.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                var label = CreateText("Label", itemRect, string.Empty, 20, TextAnchor.MiddleCenter);
                label.color = Color.white;
                label.alignment = TextAnchor.LowerRight;
                var labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = new Vector2(4f, 2f);
                labelRect.offsetMax = new Vector2(-4f, -2f);
                view = itemRect.gameObject.AddComponent<GridInventoryUnityUIItem>();
                view.Bind(this, instance.instanceId, image, label);
                itemViewByInstanceId[instance.instanceId] = view;
            }

            view.SetBounds(instance.originRow, instance.originColumn, width, height, cellSize, cellSpacing);
            if (!TryGetVisual(instance.itemId, out var icon, out var tint)) tint = new Color(0.31f, 0.48f, 0.72f, 0.95f);
            var dragOwner = activeDragOwner != null ? activeDragOwner : this;
            var mergeHighlighted = dragOwner.previewContainerId == currentContainerId && instance.instanceId == dragOwner.previewMergeInstanceId;
            view.SetVisual(instance.stackCount, definition.maxStack, instance.instanceId == selectedInstanceId, mergeHighlighted, selectedItemColor, mergeTargetColor, icon, tint);
            // 搜索容器中的物品在被逐个识别前显示蒙版；普通背包不会提供该状态。
            if (itemSearchStateProvider != null && itemSearchStateProvider(currentContainerId, instance.instanceId, out var revealed, out var searching, out var searchProgress))
            {
                view.SetSearchState(
                    revealed,
                    searching,
                    searchProgress,
                    unrevealedItemMaskColor,
                    searchingItemMaskColor,
                    itemSearchMaskSprite,
                    unrevealedItemText,
                    searchingItemTextFormat,
                    itemSearchTextColor,
                    itemSearchTextFontSize);
            }
            else view.SetSearchState(true, false, 1f, unrevealedItemMaskColor, searchingItemMaskColor, itemSearchMaskSprite, unrevealedItemText, searchingItemTextFormat, itemSearchTextColor, itemSearchTextFontSize);
            view.SetDragging(instance.instanceId == draggingInstanceId);
            displayedInstanceIds.Add(instance.instanceId);
        }

        staleItemIds.Clear();
        foreach (var instanceId in itemViewByInstanceId.Keys)
        {
            if (!displayedInstanceIds.Contains(instanceId)) staleItemIds.Add(instanceId);
        }
        for (var i = 0; i < staleItemIds.Count; i++)
        {
            var staleId = staleItemIds[i];
            if (itemViewByInstanceId.TryGetValue(staleId, out var staleView))
            {
                if (staleView.GetComponent<GridInventoryEditorItem>() != null)
                {
                    staleView.gameObject.SetActive(false);
                }
                else
                {
                    Destroy(staleView.gameObject);
                }
            }
            itemViewByInstanceId.Remove(staleId);
            if (selectedInstanceId == staleId) selectedInstanceId = null;
            if (contextMenuInstanceId == staleId) HideContextMenu();
        }
    }

    /// <summary>
    /// 编辑器物品节点已有自己的 Image/Button/Label，运行时只绑定交互逻辑，不重新创建节点。
    /// </summary>
    private void RegisterEditorItemViews()
    {
        if (editorItemViewsRegistered) return;
        if (itemLayerRoot == null) return;
        var editorItems = itemLayerRoot.GetComponentsInChildren<GridInventoryEditorItem>(true);
        for (var i = 0; i < editorItems.Length; i++)
        {
            var editorItem = editorItems[i];
            if (editorItem == null || editorItem.ContainerId != currentContainerId) continue;
            var instanceId = editorItem.RuntimeInstanceId;
            if (string.IsNullOrWhiteSpace(instanceId) || itemViewByInstanceId.ContainsKey(instanceId)) continue;

            var view = editorItem.GetComponent<GridInventoryUnityUIItem>();
            if (view == null) view = editorItem.gameObject.AddComponent<GridInventoryUnityUIItem>();
            var image = editorItem.GetComponent<Image>();
            if (image == null) image = editorItem.gameObject.AddComponent<Image>();
            var label = editorItem.GetComponentInChildren<Text>(true);
            if (label == null)
            {
                label = CreateText("Label", editorItem.transform, string.Empty, 20, TextAnchor.LowerRight);
                label.color = Color.white;
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(4f, 2f);
                label.rectTransform.offsetMax = new Vector2(-4f, -2f);
            }

            view.Bind(this, instanceId, image, label);
            itemViewByInstanceId[instanceId] = view;
        }
        editorItemViewsRegistered = true;
    }

    public void SnapEditorItemToGrid(GridInventoryEditorItem editorItem)
    {
        if (editorItem == null || itemLayerRoot == null) return;
        var rect = editorItem.GetComponent<RectTransform>();
        if (rect == null) return;
        var step = Mathf.Max(1f, cellSize + cellSpacing);
        var row = Mathf.Max(0, Mathf.RoundToInt(-rect.anchoredPosition.y / step));
        var column = Mathf.Max(0, Mathf.RoundToInt(rect.anchoredPosition.x / step));
        editorItem.SetGridPosition(row, column);
        rect.anchoredPosition = new Vector2(column * step, -row * step);
    }

    private void RotateDragPreview()
    {
        var draggingItemId = GetDraggingItemId();
        if (string.IsNullOrWhiteSpace(draggingItemId)) return;
        if (!gridInventoryService.TryGetItemDefinition(draggingItemId, out var definition)) return;
        if (!definition.canRotate) return;
        draggingRotation = draggingRotation == GridItemRotation.Deg0 ? GridItemRotation.Deg90 : GridItemRotation.Deg0;
    }

    private void UpdateDragPreview(Vector2 screenPosition)
    {
        if (previewGhostRect == null) EnsurePreviewGhost();
        var draggingItemId = GetDraggingItemId();
        if (string.IsNullOrWhiteSpace(draggingItemId)) return;
        if (!gridInventoryService.TryGetItemDefinition(draggingItemId, out var definition)) return;
        if (!string.IsNullOrWhiteSpace(draggingInstanceId) && gridInventoryService.TryGetItemInstance(draggingInstanceId, out var draggingInstance))
        {
            previewGhostText.text = $"{draggingInstance.itemId}\nx{draggingInstance.stackCount}";
        }
        else
        {
            previewGhostText.text = $"{draggingItemId}\nx1";
        }

        var hasCell = TryGetDropCellByScreenPosition(screenPosition, out var dropView, out var row, out var column);
        previewContainerId = hasCell && dropView != null ? dropView.currentContainerId : null;
        dragHoverRow = hasCell ? row : -1;
        dragHoverColumn = hasCell ? column : -1;
        previewMergeRow = -1;
        previewMergeColumn = -1;
        previewMergeWidth = 1;
        previewMergeHeight = 1;
        previewMergeInstanceId = null;
        var targetContainerId = !string.IsNullOrWhiteSpace(previewContainerId) ? previewContainerId : currentContainerId;
        if (hasCell && TryGetMergeTargetFootprint(targetContainerId, row, column, out var mergeInstanceId, out var mergeRow, out var mergeColumn, out var mergeWidth, out var mergeHeight))
        {
            previewMergeInstanceId = mergeInstanceId;
            previewMergeRow = mergeRow;
            previewMergeColumn = mergeColumn;
            previewMergeWidth = mergeWidth;
            previewMergeHeight = mergeHeight;
            previewCanPlace = false;
            previewRow = -1;
            previewColumn = -1;
            previewWidth = 1;
            previewHeight = 1;
            previewGhostRect.sizeDelta = new Vector2(cellSize, cellSize);
            previewGhostImage.color = previewValidColor;
            previewGhostRect.position = screenPosition;
            return;
        }

        if (TryResolveDragPlacement(definition, targetContainerId, hasCell, row, column, out var resolvedRotation, out var targetRow, out var targetColumn, out var width, out var height, out var canPlace))
        {
            draggingRotation = resolvedRotation;
            previewCanPlace = canPlace;
        }
        else
        {
            targetRow = -1;
            targetColumn = -1;
            previewCanPlace = false;
        }

        previewGhostRect.sizeDelta = new Vector2(width * cellSize + Mathf.Max(0, width - 1) * cellSpacing, height * cellSize + Mathf.Max(0, height - 1) * cellSpacing);
        previewRow = hasCell ? targetRow : -1;
        previewColumn = hasCell ? targetColumn : -1;
        previewWidth = width;
        previewHeight = height;
        previewGhostImage.color = previewCanPlace || previewMergeInstanceId != null ? previewValidColor : previewInvalidColor;
        previewGhostRect.position = screenPosition;
    }

    private bool TryResolveDragPlacement(GridItemDefinition definition, string containerId, bool hasCell, int row, int column, out GridItemRotation resolvedRotation, out int targetRow, out int targetColumn, out int width, out int height, out bool canPlace)
    {
        resolvedRotation = draggingRotation;
        targetRow = -1;
        targetColumn = -1;
        width = 1;
        height = 1;
        canPlace = false;

        if (!TryFindBestDragPlacement(definition, containerId, draggingRotation, hasCell, row, column, out targetRow, out targetColumn, out width, out height, out canPlace)) return false;
        if (canPlace) return true;
        if (!hasCell || !definition.canRotate) return true;

        var alternateRotation = draggingRotation == GridItemRotation.Deg0 ? GridItemRotation.Deg90 : GridItemRotation.Deg0;
        if (!TryFindBestDragPlacement(definition, containerId, alternateRotation, true, row, column, out var alternateRow, out var alternateColumn, out var alternateWidth, out var alternateHeight, out var alternateCanPlace)) return true;
        if (!alternateCanPlace) return true;

        resolvedRotation = alternateRotation;
        targetRow = alternateRow;
        targetColumn = alternateColumn;
        width = alternateWidth;
        height = alternateHeight;
        canPlace = true;
        return true;
    }

    private bool TryFindBestDragPlacement(GridItemDefinition definition, string containerId, GridItemRotation rotation, bool hasCell, int row, int column, out int targetRow, out int targetColumn, out int width, out int height, out bool canPlace)
    {
        targetRow = -1;
        targetColumn = -1;
        width = 1;
        height = 1;
        canPlace = false;
        if (!GridPlacementSolver.TryGetFootprint(definition, rotation, out width, out height)) return false;
        if (!hasCell) return true;

        var rowOffset = Mathf.Clamp(dragPointerOffsetRow, 0, Mathf.Max(0, height - 1));
        var columnOffset = Mathf.Clamp(dragPointerOffsetColumn, 0, Mathf.Max(0, width - 1));
        var preferredRow = row - rowOffset;
        var preferredColumn = column - columnOffset;
        targetRow = preferredRow;
        targetColumn = preferredColumn;

        if (CanDragPlaceAt(containerId, targetRow, targetColumn, rotation))
        {
            canPlace = true;
            return true;
        }

        var bestScore = int.MaxValue;
        var foundFit = false;

        for (var originRow = row - height; originRow <= row + 1; originRow++)
        {
            for (var originColumn = column - width; originColumn <= column + 1; originColumn++)
            {
                if (originRow == preferredRow && originColumn == preferredColumn) continue;
                if (!CanDragPlaceAt(containerId, originRow, originColumn, rotation)) continue;

                var score = Mathf.Abs(originRow - preferredRow) + Mathf.Abs(originColumn - preferredColumn);
                if (score >= bestScore) continue;
                bestScore = score;
                targetRow = originRow;
                targetColumn = originColumn;
                foundFit = true;
            }
        }

        canPlace = foundFit;
        return true;
    }

    private bool CanDragPlaceAt(string containerId, int row, int column, GridItemRotation rotation)
    {
        if (!string.IsNullOrWhiteSpace(draggingInstanceId))
        {
            return gridInventoryService.CanPlace(draggingInstanceId, containerId, row, column, rotation);
        }
        if (!string.IsNullOrWhiteSpace(draggingSpawnItemId))
        {
            return gridInventoryService.CanSpawn(draggingSpawnItemId, containerId, row, column, rotation);
        }
        return false;
    }

    private bool IsDraggingItem()
    {
        return !string.IsNullOrWhiteSpace(draggingInstanceId) || !string.IsNullOrWhiteSpace(draggingSpawnItemId);
    }

    private bool IsCellInsidePreviewFootprint(int row, int column)
    {
        if (previewRow < 0 || previewColumn < 0) return false;
        return row >= previewRow && row < previewRow + previewHeight &&
               column >= previewColumn && column < previewColumn + previewWidth;
    }

    private bool IsCellInsideMergePreviewFootprint(int row, int column)
    {
        if (previewMergeRow < 0 || previewMergeColumn < 0) return false;
        return row >= previewMergeRow && row < previewMergeRow + previewMergeHeight &&
               column >= previewMergeColumn && column < previewMergeColumn + previewMergeWidth;
    }

    private void EnsurePreviewGhost()
    {
        previewGhostRect = CreateRect("PreviewGhost", ghostRoot, new Vector2(cellSize, cellSize), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero);
        previewGhostImage = previewGhostRect.gameObject.AddComponent<Image>();
        previewGhostImage.raycastTarget = false;
        previewGhostText = CreateText("GhostLabel", previewGhostRect, string.Empty, 20, TextAnchor.MiddleCenter);
        previewGhostText.raycastTarget = false;
        previewGhostText.color = Color.white;
        previewGhostRect.gameObject.SetActive(false);
    }

    private void EnsurePanelDragHandle()
    {
        if (!allowPanelDragging || titleText == null || panelRoot == null) return;
        titleText.raycastTarget = true;
        var dragHandle = titleText.GetComponent<GridInventoryUnityUIDragHandle>();
        if (dragHandle == null) dragHandle = titleText.gameObject.AddComponent<GridInventoryUnityUIDragHandle>();
        dragHandle.Bind(panelRoot, canvas != null ? canvas : GetComponentInParent<Canvas>());
    }

    private void EnsureCloseButton()
    {
        if (!isSideContainerView || panelRoot == null) return;
        if (closeButton != null)
        {
            closeButton.transform.SetAsLastSibling();
            return;
        }

        var existing = panelRoot.Find("Btn_Close");
        if (existing != null) closeButton = existing.GetComponent<Button>();
        if (closeButton == null)
        {
            closeButton = CreateButton(panelRoot, "×", new Vector2(30f, 30f), Vector2.zero, () => SetOpen(false));
            closeButton.gameObject.name = "Btn_Close";
        }

        var rect = closeButton.transform as RectTransform;
        if (rect == null) return;
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.sizeDelta = new Vector2(30f, 30f);
        rect.anchoredPosition = new Vector2(-8f, -8f);
        closeButton.transform.SetAsLastSibling();
    }

    private bool TryGetCellByScreenPosition(Vector2 screenPosition, out int row, out int column)
    {
        row = -1;
        column = -1;
        if (!gridInventoryService.TryGetContainerSize(currentContainerId, out var rows, out var columns)) return false;
        var hitRoot = cellLayerRoot != null ? cellLayerRoot : gridRoot;
        if (hitRoot == null) return false;
        var eventCamera = GetCanvasEventCamera();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(hitRoot, screenPosition, eventCamera, out var localPoint)) return false;

        var rect = hitRoot.rect;
        var xFromLeft = localPoint.x - rect.xMin;
        var yFromTop = rect.yMax - localPoint.y;
        if (xFromLeft < 0f || yFromTop < 0f) return false;

        var step = cellSize + cellSpacing;
        var computedColumn = Mathf.FloorToInt(xFromLeft / step);
        var computedRow = Mathf.FloorToInt(yFromTop / step);
        if (computedRow < 0 || computedColumn < 0 || computedRow >= rows || computedColumn >= columns) return false;

        row = computedRow;
        column = computedColumn;
        return true;
    }

    private bool TryGetDropCellByScreenPosition(Vector2 screenPosition, out GridInventoryUnityUI view, out int row, out int column)
    {
        view = null;
        row = -1;
        column = -1;
        for (var i = ActiveViews.Count - 1; i >= 0; i--)
        {
            var candidate = ActiveViews[i];
            if (candidate == null || candidate.gridInventoryService != gridInventoryService) continue;
            if (!candidate.gameObject.activeInHierarchy) continue;
            if (candidate.canvas != null && !candidate.canvas.enabled) continue;
            if (!candidate.TryGetCellByScreenPosition(screenPosition, out row, out column)) continue;
            view = candidate;
            return true;
        }
        return false;
    }

    private void AlignToolbarAndGrid(float gridWidth)
    {
        if (UsesManualLayout) return;
        if (toolbarRoot == null || spawnRoot == null || actionRoot == null || gridRoot == null) return;
        var rowIndex = 0;
        if (toolRoot != null)
        {
            toolRoot.anchoredPosition = new Vector2(panelLeftPadding, -toolbarTopOffset);
            toolRoot.sizeDelta = new Vector2(gridWidth, GetToolbarAreaHeight());
        }
        var firstRowY = -toolbarTopOffset;
        if (showContainerToolbar)
        {
            toolbarRoot.anchoredPosition = toolRoot != null ? new Vector2(0f, -rowIndex * (toolbarHeight + toolbarSpacing)) : new Vector2(panelLeftPadding, firstRowY - rowIndex * (toolbarHeight + toolbarSpacing));
            rowIndex++;
        }
        if (showSpawnTools)
        {
            spawnRoot.anchoredPosition = toolRoot != null ? new Vector2(0f, -rowIndex * (toolbarHeight + toolbarSpacing)) : new Vector2(panelLeftPadding, firstRowY - rowIndex * (toolbarHeight + toolbarSpacing));
            rowIndex++;
        }
        if (showActionToolbar)
        {
            actionRoot.anchoredPosition = toolRoot != null ? new Vector2(0f, -rowIndex * (toolbarHeight + toolbarSpacing)) : new Vector2(panelLeftPadding, firstRowY - rowIndex * (toolbarHeight + toolbarSpacing));
        }
        gridRoot.anchoredPosition = new Vector2(panelLeftPadding, -GetGridTopOffset());
        toolbarRoot.sizeDelta = new Vector2(gridWidth, toolbarHeight);
        spawnRoot.sizeDelta = new Vector2(gridWidth, toolbarHeight);
        actionRoot.sizeDelta = new Vector2(gridWidth, toolbarHeight);
        SyncLayerToGrid(cellLayerRoot);
        SyncLayerToGrid(itemLayerRoot);
        SyncLayerToGrid(ghostRoot);
    }

    private float GetToolbarRequiredWidth(float fallbackWidth)
    {
        var requiredWidth = Mathf.Max(fallbackWidth, minToolbarWidth);
        if (showContainerToolbar) requiredWidth = Mathf.Max(requiredWidth, GetChildrenRightEdge(toolbarRoot));
        if (showActionToolbar) requiredWidth = Mathf.Max(requiredWidth, GetChildrenRightEdge(actionRoot));
        if (showSpawnTools) requiredWidth = Mathf.Max(requiredWidth, GetChildrenRightEdge(spawnRoot));
        return requiredWidth;
    }

    private int GetVisibleToolbarRowCount()
    {
        var rows = 0;
        if (showContainerToolbar) rows++;
        if (showSpawnTools) rows++;
        if (showActionToolbar) rows++;
        return rows;
    }

    private float GetToolbarAreaHeight()
    {
        var rows = GetVisibleToolbarRowCount();
        if (rows <= 0) return 0f;
        return rows * toolbarHeight + Mathf.Max(0, rows - 1) * toolbarSpacing;
    }

    private float GetGridTopOffset()
    {
        var toolbarAreaHeight = GetToolbarAreaHeight();
        return toolbarTopOffset + toolbarAreaHeight + (toolbarAreaHeight > 0f ? gridTopSpacing : 0f);
    }

    private float GetChildrenRightEdge(RectTransform root)
    {
        if (root == null) return 0f;
        var rightEdge = root.sizeDelta.x;
        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i) as RectTransform;
            if (child == null || !child.gameObject.activeSelf) continue;
            rightEdge = Mathf.Max(rightEdge, child.anchoredPosition.x + child.sizeDelta.x + 6f);
        }
        return rightEdge;
    }

    private void SyncLayerToGrid(RectTransform layer)
    {
        if (layer == null || gridRoot == null) return;
        layer.anchorMin = gridRoot.anchorMin;
        layer.anchorMax = gridRoot.anchorMax;
        layer.pivot = gridRoot.pivot;
        layer.anchoredPosition = gridRoot.anchoredPosition;
        layer.sizeDelta = gridRoot.sizeDelta;
    }

    private void ApplyInventoryLayerOrder()
    {
        if (gridRoot != null) gridRoot.SetAsFirstSibling();
        if (cellLayerRoot != null) cellLayerRoot.SetAsLastSibling();
        if (itemLayerRoot != null) itemLayerRoot.SetAsLastSibling();
        if (ghostRoot != null) ghostRoot.SetAsLastSibling();
        if (toolRoot != null) toolRoot.SetAsLastSibling();
    }

    private void UpdateToolRootPosition()
    {
        if (UsesManualLayout) return;
        if (!autoRepositionToolRoot || toolRoot == null || gridRoot == null) return;
        var gridCorners = new Vector3[4];
        gridRoot.GetWorldCorners(gridCorners);
        var bottomLeft = gridCorners[0];
        var bottomRight = gridCorners[3];
        var targetY = (bottomLeft.y + bottomRight.y) * 0.5f - toolRootSpacingFromGrid;
        if (!toolRootBaseCaptured)
        {
            toolRootBaseX = toolRoot.position.x;
            toolRootBaseZ = toolRoot.position.z;
            toolRootBaseCaptured = true;
        }
        toolRoot.position = new Vector3(toolRootBaseX, targetY, toolRootBaseZ);
    }

    private void EnsureCanvasScalerMode()
    {
        var scalerHost = canvas != null ? canvas.gameObject : gameObject;
        var scaler = scalerHost.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = scalerHost.AddComponent<CanvasScaler>();
        if (scaleWithScreenSize)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = screenMatchMode;
            return;
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
    }

    private Camera GetCanvasEventCamera()
    {
        if (canvas == null) return null;
        return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }

    private void ApplyBackgroundStyle(Image image, Sprite sprite, Color color)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.color = sprite == null ? color : Color.white;
        image.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
    }

    private void TryCalculateDragPointerOffset(RectTransform itemRect, Vector2 screenPosition, int itemWidth, int itemHeight, out int rowOffset, out int columnOffset)
    {
        rowOffset = 0;
        columnOffset = 0;
        if (itemRect == null) return;
        var eventCamera = GetCanvasEventCamera();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(itemRect, screenPosition, eventCamera, out var localPoint)) return;
        var rect = itemRect.rect;
        var xFromLeft = localPoint.x - rect.xMin;
        var yFromTop = rect.yMax - localPoint.y;
        var step = Mathf.Max(1f, cellSize + cellSpacing);
        columnOffset = Mathf.Clamp(Mathf.FloorToInt(xFromLeft / step), 0, Mathf.Max(0, itemWidth - 1));
        rowOffset = Mathf.Clamp(Mathf.FloorToInt(yFromTop / step), 0, Mathf.Max(0, itemHeight - 1));
    }

    private void ResolveContainerId()
    {
        if (!string.IsNullOrWhiteSpace(currentContainerId)) return;
        foreach (var containerId in gridInventoryService.PlayerContainerIds)
        {
            currentContainerId = containerId;
            break;
        }
    }

    private void OnCellClicked(int row, int column)
    {
        if (!string.IsNullOrWhiteSpace(draggingInstanceId) || !string.IsNullOrWhiteSpace(draggingSpawnItemId)) return;
        HideContextMenu();
        if (gridInventoryService.TryGetCellOccupantId(currentContainerId, row, column, out var occupantId))
        {
            if (!CanInteractWithItem(occupantId)) return;
            if (selectedInstanceId == occupantId)
            {
                selectedInstanceId = null;
                return;
            }
            selectedInstanceId = occupantId;
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedInstanceId) && gridInventoryService.TryGetItemInstance(selectedInstanceId, out var selected))
        {
            if (TryMergeSelectedIntoCell(row, column))
            {
                selectedInstanceId = null;
                return;
            }
            if (gridInventoryService.TryPlace(selectedInstanceId, currentContainerId, row, column, selected.rotation))
            {
                selectedInstanceId = null;
            }
            return;
        }

        if (!showSpawnTools || !allowSpawnFromEmptyCells || string.IsNullOrWhiteSpace(selectedSpawnItemId))
        {
            selectedInstanceId = null;
            return;
        }
        if (TryMergeSpawnIntoCell(selectedSpawnItemId, currentContainerId, row, column)) return;
        gridInventoryService.TrySpawnToCell(selectedSpawnItemId, currentContainerId, row, column);
    }

    public void HandleItemClick(string instanceId)
    {
        if (!CanInteractWithItem(instanceId)) return;
        HideContextMenu();
        selectedInstanceId = selectedInstanceId == instanceId ? null : instanceId;
    }

    public void HandleItemRightClick(string instanceId, Vector2 screenPosition)
    {
        if (!CanInteractWithItem(instanceId)) return;
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        if (!gridInventoryService.TryGetItemInstance(instanceId, out var instance)) return;
        if (!gridInventoryService.TryGetItemDefinition(instance.itemId, out var definition)) return;

        selectedInstanceId = instanceId;
        if (instance.stackCount <= 1 && !definition.canRotate)
        {
            HideContextMenu();
            return;
        }

        ShowContextMenu(instanceId, screenPosition);
    }

    public void HandleItemBeginDrag(string instanceId, Vector2 screenPosition, RectTransform itemRect)
    {
        if (!CanInteractWithItem(instanceId)) return;
        if (!gridInventoryService.TryGetItemInstance(instanceId, out var instance)) return;
        HideContextMenu();
        selectedInstanceId = instanceId;
        draggingInstanceId = instanceId;
        draggingSpawnItemId = null;
        activeDragOwner = this;
        draggingRotation = instance.rotation;
        dragPointerOffsetRow = 0;
        dragPointerOffsetColumn = 0;
        if (gridInventoryService.TryGetItemDefinition(instance.itemId, out var definition) &&
            GridPlacementSolver.TryGetFootprint(definition, instance.rotation, out var width, out var height))
        {
            TryCalculateDragPointerOffset(itemRect, screenPosition, width, height, out dragPointerOffsetRow, out dragPointerOffsetColumn);
        }
        EnsurePreviewGhost();
        previewGhostRect.gameObject.SetActive(true);
        UpdateDragPreview(screenPosition);
    }

    public void HandleItemDrag(Vector2 screenPosition)
    {
        if (string.IsNullOrWhiteSpace(draggingInstanceId)) return;
        UpdateDragPreview(screenPosition);
    }

    public void HandleItemEndDrag()
    {
        if (string.IsNullOrWhiteSpace(draggingInstanceId)) return;
        var placed = false;
        var targetContainerId = !string.IsNullOrWhiteSpace(previewContainerId) ? previewContainerId : currentContainerId;
        if (dragHoverRow >= 0 && dragHoverColumn >= 0 && TryMergeByInstanceAtCell(draggingInstanceId, targetContainerId, dragHoverRow, dragHoverColumn))
        {
            placed = true;
            if (gridInventoryService.TryGetItemInstance(draggingInstanceId, out _))
            {
                var remainderPlaced = false;
                if (previewCanPlace && previewRow >= 0 && previewColumn >= 0)
                {
                    remainderPlaced = gridInventoryService.TryPlace(draggingInstanceId, targetContainerId, previewRow, previewColumn, draggingRotation);
                }
                if (!remainderPlaced)
                {
                    TryPlaceDraggingRemainderNear(draggingInstanceId, targetContainerId, dragHoverRow, dragHoverColumn);
                }
            }
        }
        else if (previewRow >= 0 && previewColumn >= 0 && previewCanPlace)
        {
            placed = gridInventoryService.TryPlace(draggingInstanceId, targetContainerId, previewRow, previewColumn, draggingRotation);
        }

        if (placed) selectedInstanceId = null;
        draggingInstanceId = null;
        draggingSpawnItemId = null;
        dragPointerOffsetRow = 0;
        dragPointerOffsetColumn = 0;
        dragHoverRow = -1;
        dragHoverColumn = -1;
        previewCanPlace = false;
        previewRow = -1;
        previewColumn = -1;
        previewWidth = 1;
        previewHeight = 1;
        previewMergeRow = -1;
        previewMergeColumn = -1;
        previewMergeWidth = 1;
        previewMergeHeight = 1;
        previewMergeInstanceId = null;
        previewContainerId = null;
        if (activeDragOwner == this) activeDragOwner = null;
        if (previewGhostRect != null) previewGhostRect.gameObject.SetActive(false);
    }

    public void HandleSpawnClick(string itemId)
    {
        if (!showSpawnTools) return;
        selectedSpawnItemId = itemId;
    }

    public string GetSpawnItemTitle(string itemId)
    {
        return GetItemTitle(itemId);
    }

    public bool TryGetSpawnItemVisual(string itemId, out Sprite icon, out Color tint)
    {
        return TryGetVisual(itemId, out icon, out tint);
    }

    public void HandleSpawnBeginDrag(string itemId, Vector2 screenPosition)
    {
        if (!showSpawnTools) return;
        if (string.IsNullOrWhiteSpace(itemId)) return;
        selectedSpawnItemId = itemId;
        draggingSpawnItemId = itemId;
        draggingInstanceId = null;
        activeDragOwner = this;
        draggingRotation = GridItemRotation.Deg0;
        dragPointerOffsetRow = 0;
        dragPointerOffsetColumn = 0;
        EnsurePreviewGhost();
        previewGhostRect.gameObject.SetActive(true);
        UpdateDragPreview(screenPosition);
    }

    public void HandleSpawnDrag(Vector2 screenPosition)
    {
        if (!showSpawnTools) return;
        if (string.IsNullOrWhiteSpace(draggingSpawnItemId)) return;
        UpdateDragPreview(screenPosition);
    }

    public void HandleSpawnEndDrag()
    {
        if (!showSpawnTools) return;
        if (string.IsNullOrWhiteSpace(draggingSpawnItemId)) return;
        if (previewRow >= 0 && previewColumn >= 0)
        {
            var targetContainerId = !string.IsNullOrWhiteSpace(previewContainerId) ? previewContainerId : currentContainerId;
            if (!TryMergeSpawnIntoCell(draggingSpawnItemId, targetContainerId, previewRow, previewColumn) && previewCanPlace)
            {
                gridInventoryService.TrySpawnToCell(draggingSpawnItemId, targetContainerId, previewRow, previewColumn, draggingRotation);
            }
        }

        draggingSpawnItemId = null;
        draggingInstanceId = null;
        dragPointerOffsetRow = 0;
        dragPointerOffsetColumn = 0;
        dragHoverRow = -1;
        dragHoverColumn = -1;
        previewCanPlace = false;
        previewRow = -1;
        previewColumn = -1;
        previewWidth = 1;
        previewHeight = 1;
        previewMergeRow = -1;
        previewMergeColumn = -1;
        previewMergeWidth = 1;
        previewMergeHeight = 1;
        previewMergeInstanceId = null;
        previewContainerId = null;
        if (activeDragOwner == this) activeDragOwner = null;
        if (previewGhostRect != null) previewGhostRect.gameObject.SetActive(false);
    }

    private string GetDraggingItemId()
    {
        if (!string.IsNullOrWhiteSpace(draggingInstanceId) && gridInventoryService.TryGetItemInstance(draggingInstanceId, out var instance))
        {
            return instance.itemId;
        }
        return draggingSpawnItemId;
    }

    private bool TryMergeSelectedIntoCell(int row, int column)
    {
        if (string.IsNullOrWhiteSpace(selectedInstanceId)) return false;
        return TryMergeByInstanceAtCell(selectedInstanceId, currentContainerId, row, column);
    }

    private bool TryMergeSpawnIntoCell(string itemId, string containerId, int row, int column)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return false;
        if (!gridInventoryService.TryGetCellOccupantId(containerId, row, column, out var targetId)) return false;
        if (!gridInventoryService.TryGetItemInstance(targetId, out var targetInstance)) return false;
        if (targetInstance.itemId != itemId) return false;
        if (!gridInventoryService.TryCreateItemInstance(itemId, 1, out var sourceId)) return false;
        if (gridInventoryService.TryMergeStacks(sourceId, targetId)) return true;
        gridInventoryService.TryRemoveInstance(sourceId);
        return false;
    }

    private bool TryMergeByInstanceAtCell(string sourceInstanceId, string containerId, int row, int column)
    {
        if (string.IsNullOrWhiteSpace(sourceInstanceId)) return false;
        if (!gridInventoryService.TryGetCellOccupantId(containerId, row, column, out var targetId)) return false;
        return gridInventoryService.TryMergeStacks(sourceInstanceId, targetId);
    }

    private bool TryGetMergeTargetFootprint(string containerId, int row, int column, out string instanceId, out int originRow, out int originColumn, out int width, out int height)
    {
        instanceId = null;
        originRow = -1;
        originColumn = -1;
        width = 1;
        height = 1;

        var sourceItemId = GetDraggingItemId();
        if (string.IsNullOrWhiteSpace(sourceItemId)) return false;
        if (!gridInventoryService.TryGetCellOccupantId(containerId, row, column, out var targetId)) return false;
        if (!string.IsNullOrWhiteSpace(draggingInstanceId) && targetId == draggingInstanceId) return false;
        if (!gridInventoryService.TryGetItemInstance(targetId, out var target)) return false;
        if (target.itemId != sourceItemId) return false;
        if (!gridInventoryService.TryGetItemDefinition(target.itemId, out var definition)) return false;

        var stackLimit = definition.maxStack > 0 ? definition.maxStack : int.MaxValue;
        if (target.stackCount >= stackLimit) return false;
        if (!GridPlacementSolver.TryGetFootprint(definition, target.rotation, out width, out height)) return false;

        instanceId = targetId;
        originRow = target.originRow;
        originColumn = target.originColumn;
        return originRow >= 0 && originColumn >= 0;
    }

    private bool TryPlaceDraggingRemainderNear(string instanceId, string containerId, int preferredRow, int preferredColumn)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return false;
        if (!gridInventoryService.TryGetItemInstance(instanceId, out var instance)) return false;
        if (!gridInventoryService.TryGetItemDefinition(instance.itemId, out var definition)) return false;
        if (!gridInventoryService.TryGetContainerSize(containerId, out var rows, out var columns)) return false;

        var rotations = new List<GridItemRotation> { draggingRotation };
        var alternateRotation = draggingRotation == GridItemRotation.Deg0 ? GridItemRotation.Deg90 : GridItemRotation.Deg0;
        if (definition.canRotate && !rotations.Contains(alternateRotation)) rotations.Add(alternateRotation);

        var bestScore = int.MaxValue;
        var bestRow = -1;
        var bestColumn = -1;
        var bestRotation = draggingRotation;

        for (var i = 0; i < rotations.Count; i++)
        {
            var rotation = rotations[i];
            if (!GridPlacementSolver.TryGetFootprint(definition, rotation, out var width, out var height)) continue;
            for (var row = 0; row <= rows - height; row++)
            {
                for (var column = 0; column <= columns - width; column++)
                {
                    if (!gridInventoryService.CanPlace(instanceId, containerId, row, column, rotation)) continue;
                    var score = Mathf.Abs(row - preferredRow) + Mathf.Abs(column - preferredColumn);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    bestRow = row;
                    bestColumn = column;
                    bestRotation = rotation;
                }
            }
        }

        return bestRow >= 0 && gridInventoryService.TryPlace(instanceId, containerId, bestRow, bestColumn, bestRotation);
    }

    /// <summary>
    /// 优先读取 Inspector 中配置的图标；未配置时再从 JSON 定义中的 Resources 路径加载。
    /// </summary>
    private bool TryGetVisual(string itemId, out Sprite icon, out Color tint)
    {
        icon = null;
        tint = new Color(0.31f, 0.48f, 0.72f, 0.95f);
        if (visualByItemId.TryGetValue(itemId, out var visual))
        {
            icon = visual.icon;
            tint = visual.tint.a <= 0f ? Color.white : visual.tint;
            if (icon != null) return true;
        }

        if (!gridInventoryService.TryGetItemDefinition(itemId, out var definition) || string.IsNullOrWhiteSpace(definition.iconResourcePath)) return false;
        icon = Resources.Load<Sprite>(definition.iconResourcePath);
        if (icon == null) return false;
        tint = Color.white;
        return true;
    }

    private string GetShortId(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return "None";
        return instanceId.Length <= 8 ? instanceId : instanceId.Substring(0, 8);
    }

    private string GetSelectedItemTitle(string instanceId)
    {
        if (!gridInventoryService.TryGetItemInstance(instanceId, out var instance)) return GetShortId(instanceId);
        return $"{GetItemTitle(instance.itemId)} x{instance.stackCount}";
    }

    private string GetContainerTitle(string containerId)
    {
        return containerId switch
        {
            "main_bag" => "主背包",
            "min_bag" => "口袋",
            "pocket" => "口袋",
            _ => containerId
        };
    }

    private string GetItemTitle(string itemId)
    {
        if (gridInventoryService != null &&
            gridInventoryService.TryGetItemDefinition(itemId, out var definition) &&
            !string.IsNullOrWhiteSpace(definition.displayName)) return definition.displayName;
        return itemId switch
        {
            "food_canned" => "罐头",
            "water_bottle" => "瓶装水",
            "bandage" => "绷带",
            "cloth_raw" => "布料",
            _ => itemId
        };
    }

    private bool TryBindToolbarButtonsFromExisting()
    {
        var buttons = toolbarRoot.GetComponentsInChildren<Button>(true);
        if (buttons == null || buttons.Length == 0) return false;
        var boundCount = 0;
        foreach (var button in buttons)
        {
            var containerId = FindContainerIdForButton(button);
            if (string.IsNullOrWhiteSpace(containerId)) continue;
            var capturedContainer = containerId;
            button.onClick.AddListener(() => SwitchToContainer(capturedContainer));
            boundCount++;
        }
        return boundCount > 0;
    }

    private bool TryBindSpawnButtonsFromExisting()
    {
        if (TryBindSpawnSelectorFromExisting()) return true;

        var buttons = spawnRoot.GetComponentsInChildren<Button>(true);
        if (buttons == null || buttons.Length == 0) return false;
        var boundCount = 0;
        var sequentialIndex = 0;
        foreach (var button in buttons)
        {
            var itemId = FindSpawnItemIdForButton(button);
            if (string.IsNullOrWhiteSpace(itemId) && sequentialIndex < spawnItemIds.Count)
            {
                itemId = spawnItemIds[sequentialIndex];
                sequentialIndex++;
            }
            if (string.IsNullOrWhiteSpace(itemId)) continue;
            if (!gridInventoryService.TryGetItemDefinition(itemId, out _)) continue;
            var handler = button.GetComponent<GridInventoryUnityUISpawnItem>();
            if (handler == null) handler = button.gameObject.AddComponent<GridInventoryUnityUISpawnItem>();
            handler.Bind(this, itemId);
            boundCount++;
        }
        return boundCount > 0;
    }

    private bool TryBindSpawnSelectorFromExisting()
    {
        if (spawnRoot == null || gridInventoryService == null) return false;
        var dropdown = spawnRoot.GetComponentInChildren<Dropdown>(true);
        if (dropdown == null) return false;

        var validItemIds = GetValidSpawnItemIds();
        if (validItemIds.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(selectedSpawnItemId) || !validItemIds.Contains(selectedSpawnItemId))
        {
            selectedSpawnItemId = validItemIds[0];
        }

        spawnDropdown = dropdown;
        spawnDropdown.onValueChanged.RemoveAllListeners();
        spawnDropdown.ClearOptions();
        spawnDropdown.AddOptions(validItemIds.Select(GetItemTitle).ToList());
        spawnDropdown.value = Mathf.Max(0, validItemIds.IndexOf(selectedSpawnItemId));
        spawnDropdown.RefreshShownValue();
        spawnDropdown.onValueChanged.AddListener(index =>
        {
            if (index < 0 || index >= validItemIds.Count) return;
            selectedSpawnItemId = validItemIds[index];
            UpdateSpawnDragEntryVisual();
        });

        spawnDragHandler = spawnRoot.GetComponentsInChildren<GridInventoryUnityUISpawnItem>(true)
            .FirstOrDefault(x => x.GetComponent<Dropdown>() == null);
        if (spawnDragHandler == null)
        {
            var buttons = spawnRoot.GetComponentsInChildren<Button>(true);
            var dragButton = buttons.FirstOrDefault(x => x.GetComponent<Dropdown>() == null);
            if (dragButton != null) spawnDragHandler = dragButton.gameObject.AddComponent<GridInventoryUnityUISpawnItem>();
        }

        if (spawnDragHandler != null)
        {
            spawnDragLabel = spawnDragHandler.GetComponentInChildren<Text>(true);
            spawnDragImage = spawnDragHandler.GetComponent<Image>();
            var selector = spawnDropdown.GetComponent<GridInventoryUnityUISpawnSelector>();
            if (selector == null) selector = spawnDropdown.gameObject.AddComponent<GridInventoryUnityUISpawnSelector>();
            selector.Bind(this, spawnDragHandler, validItemIds);
            UpdateSpawnDragEntryVisual();
            AutoAlignSpawnSelectorControls();
        }

        return true;
    }

    /// <summary>
    /// 未搜索完成的物品只允许观看蒙版，禁止拖拽、旋转、拆分和丢弃。
    /// </summary>
    private bool CanInteractWithItem(string instanceId)
    {
        return itemSearchStateProvider == null ||
               !itemSearchStateProvider(currentContainerId, instanceId, out var revealed, out _, out _) ||
               revealed;
    }

    private bool TryBindActionButtonsFromExisting()
    {
        var buttons = actionRoot.GetComponentsInChildren<Button>(true);
        if (buttons == null || buttons.Length == 0) return false;
        var boundCount = 0;
        foreach (var button in buttons)
        {
            var token = BuildButtonToken(button);
            if (string.IsNullOrWhiteSpace(token)) continue;
            if (IsAutoSortToken(token))
            {
                button.onClick.AddListener(ExecuteAutoSort);
                hasAutoSortActionButton = true;
                boundCount++;
                continue;
            }
            if (IsRotateToken(token))
            {
                button.onClick.AddListener(ExecuteRotateSelected);
                boundCount++;
                continue;
            }
            if (IsUseToken(token))
            {
                button.onClick.AddListener(ExecuteUseSelected);
                boundCount++;
                continue;
            }
            if (IsRemoveToken(token))
            {
                button.onClick.AddListener(ExecuteRemoveSelected);
                boundCount++;
                continue;
            }
            if (IsSplitToken(token))
            {
                button.onClick.AddListener(ExecuteSplitOne);
                boundCount++;
            }
        }
        return boundCount > 0;
    }

    private string FindContainerIdForButton(Button button)
    {
        if (button == null) return null;
        var token = BuildButtonToken(button);
        foreach (var containerId in gridInventoryService.PlayerContainerIds)
        {
            if (string.IsNullOrWhiteSpace(containerId)) continue;
            var normalized = NormalizeToken(containerId);
            if (token == normalized) return containerId;
            if (token.Contains(normalized)) return containerId;
        }
        return null;
    }

    private string FindSpawnItemIdForButton(Button button)
    {
        if (button == null) return null;
        var token = BuildButtonToken(button);
        for (var i = 0; i < spawnItemIds.Count; i++)
        {
            var itemId = spawnItemIds[i];
            if (string.IsNullOrWhiteSpace(itemId)) continue;
            var normalized = NormalizeToken(itemId);
            if (token == normalized) return itemId;
            if (token.Contains(normalized)) return itemId;
        }
        return null;
    }

    private string BuildButtonToken(Button button)
    {
        if (button == null) return string.Empty;
        var textComponent = button.GetComponentInChildren<Text>(true);
        var label = textComponent != null ? textComponent.text : string.Empty;
        return NormalizeToken($"{button.gameObject.name} {label}");
    }

    private string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
    }

    private bool IsAutoSortToken(string token)
    {
        return token.Contains("autosort") || token.Contains("sort") || token.Contains("整理");
    }

    private bool IsRotateToken(string token)
    {
        return token.Contains("rotate") || token.Contains("旋转");
    }

    private bool IsUseToken(string token)
    {
        return token.Contains("use") || token.Contains("consume") || token.Contains("使用");
    }

    private bool IsRemoveToken(string token)
    {
        return token.Contains("remove") || token.Contains("delete") || token.Contains("删除");
    }

    private bool IsSplitToken(string token)
    {
        return token.Contains("split") || token.Contains("拆分");
    }

    private void SwitchToContainer(string containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId)) return;
        if (openOtherContainersAsSidePanels && !isSideContainerView && containerId != currentContainerId)
        {
            ToggleSideContainerView(containerId);
            return;
        }
        currentContainerId = containerId;
        selectedInstanceId = null;
        HideContextMenu();
        RebuildGrid();
        RefreshItems();
    }

    private void ToggleSideContainerView(string containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId) || gridInventoryService == null) return;
        var shouldOpen = !sideContainerViews.TryGetValue(containerId, out var current) ||
                         current == null ||
                         !current.IsOpen();
        SetSideContainerVisible(containerId, shouldOpen);
    }

    private bool SetSideContainerVisible(
        string containerId,
        bool visible,
        bool useLayoutOverride = false,
        Vector2 panelOffset = default,
        float targetCellSize = 0f,
        float targetCellSpacing = 0f)
    {
        if (string.IsNullOrWhiteSpace(containerId) || gridInventoryService == null) return false;
        if (!gridInventoryService.TryGetContainerSize(containerId, out _, out _)) return false;
        if (sideContainerViews.TryGetValue(containerId, out var existing) && existing != null)
        {
            if (useLayoutOverride) existing.ApplyExternalContainerLayout(panelOffset, targetCellSize, targetCellSpacing);
            existing.SetOpen(visible);
            return true;
        }

        if (TryFindSceneSideContainerView(containerId, out var sceneView))
        {
            sceneView.ConfigureSideContainerView(this, gridInventoryService, containerId, false);
            sideContainerViews[containerId] = sceneView;
            if (useLayoutOverride) sceneView.ApplyExternalContainerLayout(panelOffset, targetCellSize, targetCellSpacing);
            sceneView.SetOpen(visible);
            return true;
        }

        if (!visible) return true;
        var parent = panelRoot != null && panelRoot.parent != null ? panelRoot.parent : transform.parent;
        if (parent == null) parent = transform;
        var sideObject = new GameObject($"InventoryUI_{containerId}");
        sideObject.SetActive(false);
        sideObject.transform.SetParent(parent, false);
        var sideRect = sideObject.AddComponent<RectTransform>();
        if (panelRoot != null)
        {
            sideRect.anchorMin = panelRoot.anchorMin;
            sideRect.anchorMax = panelRoot.anchorMax;
            sideRect.pivot = panelRoot.pivot;
            sideRect.sizeDelta = panelRoot.sizeDelta;
            var offset = useLayoutOverride ? panelOffset : sidePanelOffset * (sideContainerViews.Count + 1);
            sideRect.anchoredPosition = panelRoot.anchoredPosition + offset;
        }
        else
        {
            sideRect.anchorMin = new Vector2(0f, 1f);
            sideRect.anchorMax = new Vector2(0f, 1f);
            sideRect.pivot = new Vector2(0f, 1f);
            sideRect.sizeDelta = new Vector2(380f, 520f);
            sideRect.anchoredPosition = useLayoutOverride
                ? panelOffset
                : sidePanelOffset * (sideContainerViews.Count + 1);
        }

        var sideView = sideObject.AddComponent<GridInventoryUnityUI>();
        sideView.ConfigureSideContainerView(this, gridInventoryService, containerId);
        if (useLayoutOverride) sideView.ApplyExternalContainerLayout(panelOffset, targetCellSize, targetCellSpacing);
        sideContainerViews[containerId] = sideView;
        sideObject.SetActive(true);
        return true;
    }

    private bool TryFindSceneSideContainerView(string containerId, out GridInventoryUnityUI sideView)
    {
        sideView = null;
        var expectedName = $"InventoryUI_{containerId}";
        var parent = panelRoot != null && panelRoot.parent != null ? panelRoot.parent : transform.parent;
        if (parent == null) parent = transform.root;
        var candidates = parent.GetComponentsInChildren<GridInventoryUnityUI>(true);
        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (candidate == null || candidate == this) continue;
            if (candidate.name != expectedName) continue;
            sideView = candidate;
            return true;
        }

        return false;
    }

    private void ExecuteAutoSort()
    {
        if (string.IsNullOrWhiteSpace(currentContainerId)) return;
        gridInventoryService.AutoSort(currentContainerId);
        selectedInstanceId = null;
        HideContextMenu();
        RefreshItems();
        RefreshGridColors();
    }

    private void ExecuteRotateSelected()
    {
        if (string.IsNullOrWhiteSpace(selectedInstanceId)) return;
        gridInventoryService.TryRotate(selectedInstanceId);
    }

    /// <summary>请求使用当前选中的玩家背包物品；具体计时、效果和扣除由消耗品服务统一处理。</summary>
    private void ExecuteUseSelected()
    {
        if (string.IsNullOrWhiteSpace(selectedInstanceId) || !CanInteractWithItem(selectedInstanceId)) return;
        if (itemUseService == null) itemUseService = FindObjectOfType<ConsumableItemUseService>();
        if (itemUseService == null)
        {
            if (itemUseStatusText != null) itemUseStatusText.text = "未找到消耗品使用服务";
            return;
        }

        itemUseService.TryBeginUse(selectedInstanceId, out _);
        RefreshItemUseStatus();
    }

    /// <summary>把场景中已有的状态文字和 Filled Image 同步为当前使用进度。</summary>
    private void RefreshItemUseStatus()
    {
        if (itemUseService == null)
        {
            if (itemUseProgressFill != null) itemUseProgressFill.fillAmount = 0f;
            return;
        }

        if (itemUseStatusText != null) itemUseStatusText.text = itemUseService.StatusMessage;
        if (itemUseProgressFill != null)
        {
            var progress = itemUseService.IsUsing ? itemUseService.Progress : 0f;
            itemUseProgressFill.fillAmount = progress;

            // 同时收缩 RectTransform，保证没有指定 Sprite 时进度条也会按比例改变宽度。
            var fillRect = itemUseProgressFill.rectTransform;
            fillRect.anchorMax = new Vector2(progress, fillRect.anchorMax.y);
        }
    }

    private void ExecuteRemoveSelected()
    {
        if (string.IsNullOrWhiteSpace(selectedInstanceId)) return;
        if (gridInventoryService.TryRemoveInstance(selectedInstanceId)) selectedInstanceId = null;
    }

    private void ExecuteSplitOne()
    {
        if (string.IsNullOrWhiteSpace(selectedInstanceId)) return;
        gridInventoryService.TrySplitStack(selectedInstanceId, 1, out _);
        HideContextMenu();
    }

    private void ExecuteContextSplitOne()
    {
        if (string.IsNullOrWhiteSpace(contextMenuInstanceId)) return;
        selectedInstanceId = contextMenuInstanceId;
        if (!gridInventoryService.TryGetItemInstance(contextMenuInstanceId, out var instance))
        {
            HideContextMenu();
            return;
        }

        var splitCount = GetContextSplitCount(Mathf.Max(1, instance.stackCount - 1));
        gridInventoryService.TrySplitStack(contextMenuInstanceId, splitCount, out _);
        HideContextMenu();
    }

    private void ExecuteContextRotate()
    {
        if (string.IsNullOrWhiteSpace(contextMenuInstanceId)) return;
        selectedInstanceId = contextMenuInstanceId;
        gridInventoryService.TryRotate(contextMenuInstanceId);
        HideContextMenu();
    }

    private void ShowContextMenu(string instanceId, Vector2 screenPosition)
    {
        EnsureContextMenu();
        contextMenuInstanceId = instanceId;
        contextMenuMaxSplitCount = 1;
        var canSplit = false;
        var canRotate = false;
        if (gridInventoryService.TryGetItemInstance(instanceId, out var instance))
        {
            canSplit = instance.stackCount > 1;
            contextMenuMaxSplitCount = Mathf.Max(1, instance.stackCount - 1);
            if (gridInventoryService.TryGetItemDefinition(instance.itemId, out var definition))
            {
                canRotate = definition.canRotate;
            }
        }
        if (splitCountInput != null) splitCountInput.text = "1";
        SetContextMenuSplitVisible(canSplit);
        if (rotateContextButton != null) rotateContextButton.gameObject.SetActive(canRotate);
        contextMenuRoot.sizeDelta = new Vector2(172f, canSplit && canRotate ? 112f : 76f);
        contextMenuRoot.position = screenPosition;
        contextMenuRoot.gameObject.SetActive(true);
        contextMenuRoot.SetAsLastSibling();
    }

    private void HideContextMenu()
    {
        contextMenuInstanceId = null;
        if (contextMenuRoot != null) contextMenuRoot.gameObject.SetActive(false);
    }

    private void EnsureContextMenu()
    {
        if (contextMenuRoot != null) return;
        var parent = canvas != null ? canvas.transform : transform;
        contextMenuRoot = CreateRect("ItemContextMenu", parent, new Vector2(172f, 112f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero);
        var image = contextMenuRoot.gameObject.AddComponent<Image>();
        image.color = new Color(0.06f, 0.06f, 0.06f, 0.96f);
        splitCountLabel = CreateText("SplitCountLabel", contextMenuRoot, "数量", 16, TextAnchor.MiddleLeft);
        splitCountLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
        splitCountLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
        splitCountLabel.rectTransform.pivot = new Vector2(0f, 1f);
        splitCountLabel.rectTransform.anchoredPosition = new Vector2(8f, -8f);
        splitCountLabel.rectTransform.sizeDelta = new Vector2(42f, 28f);

        splitCountInput = CreateInputField(contextMenuRoot, "1", new Vector2(108f, 28f), new Vector2(56f, -8f));
        splitContextButton = CreateButton(contextMenuRoot, "拆分", new Vector2(156f, 30f), new Vector2(8f, -42f), ExecuteContextSplitOne);
        rotateContextButton = CreateButton(contextMenuRoot, "旋转", new Vector2(156f, 30f), new Vector2(8f, -78f), ExecuteContextRotate);
        contextMenuRoot.gameObject.SetActive(false);
    }

    private void SetContextMenuSplitVisible(bool visible)
    {
        if (splitCountLabel != null) splitCountLabel.gameObject.SetActive(visible);
        if (splitCountInput != null) splitCountInput.gameObject.SetActive(visible);
        if (splitContextButton != null) splitContextButton.gameObject.SetActive(visible);
        if (rotateContextButton == null) return;
        rotateContextButton.GetComponent<RectTransform>().anchoredPosition = visible ? new Vector2(8f, -78f) : new Vector2(8f, -8f);
    }

    private int GetContextSplitCount(int maxCount)
    {
        var value = 1;
        if (splitCountInput != null) int.TryParse(splitCountInput.text, out value);
        value = Mathf.Clamp(value, 1, Mathf.Max(1, maxCount));
        if (splitCountInput != null) splitCountInput.text = value.ToString();
        return value;
    }

    private void NormalizeContextSplitInput(string _)
    {
        GetContextSplitCount(contextMenuMaxSplitCount);
    }

    private bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private RectTransform CreateRect(string objectName, Transform parent, Vector2 size, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition)
    {
        var obj = new GameObject(objectName);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        return rect;
    }

    private RectTransform GetOrCreateRect(string objectName, Transform parent, Vector2 size, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition)
    {
        RectTransform rect = null;
        var existing = parent != null ? parent.Find(objectName) : null;
        if (existing != null)
        {
            rect = existing as RectTransform;
            existing.gameObject.SetActive(true);
        }
        if (rect == null)
        {
            rect = CreateRect(objectName, parent, size, anchorMin, anchorMax, pivot, anchoredPosition);
        }

        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        return rect;
    }

    private Text CreateText(string objectName, Transform parent, string text, int fontSize, TextAnchor alignment)
    {
        var obj = new GameObject(objectName);
        obj.transform.SetParent(parent, false);
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var uiText = obj.AddComponent<Text>();
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiText.text = text;
        uiText.fontSize = fontSize;
        uiText.alignment = alignment;
        uiText.color = Color.white;
        uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
        uiText.verticalOverflow = VerticalWrapMode.Truncate;
        return uiText;
    }

    private Button CreateButton(Transform parent, string text, Vector2 size, Vector2 anchoredPosition, UnityEngine.Events.UnityAction action)
    {
        var rect = CreateRect($"Btn_{text}", parent, size, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);
        var button = rect.gameObject.AddComponent<Button>();
        button.onClick.AddListener(action);
        var label = CreateText("Text", rect, text, 18, TextAnchor.MiddleCenter);
        label.color = Color.white;
        return button;
    }

    private InputField CreateInputField(Transform parent, string text, Vector2 size, Vector2 anchoredPosition)
    {
        var rect = CreateRect("Input_SplitCount", parent, size, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.12f, 0.12f, 0.12f, 0.98f);
        var input = rect.gameObject.AddComponent<InputField>();
        input.contentType = InputField.ContentType.IntegerNumber;
        input.lineType = InputField.LineType.SingleLine;

        var textComponent = CreateText("Text", rect, text, 18, TextAnchor.MiddleCenter);
        textComponent.color = Color.white;
        textComponent.supportRichText = false;
        var textRect = textComponent.rectTransform;
        textRect.offsetMin = new Vector2(6f, 2f);
        textRect.offsetMax = new Vector2(-6f, -2f);

        var placeholder = CreateText("Placeholder", rect, "1", 18, TextAnchor.MiddleCenter);
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        var placeholderRect = placeholder.rectTransform;
        placeholderRect.offsetMin = new Vector2(6f, 2f);
        placeholderRect.offsetMax = new Vector2(-6f, -2f);

        input.textComponent = textComponent;
        input.placeholder = placeholder;
        input.text = text;
        input.onEndEdit.AddListener(NormalizeContextSplitInput);
        return input;
    }

    private GameObject CreateSpawnEntry(Transform parent, string itemId, Vector2 size, Vector2 anchoredPosition)
    {
        var rect = CreateRect($"Spawn_{itemId}", parent, size, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.16f, 0.16f, 0.16f, 0.95f);
        var label = CreateText("Text", rect, itemId, 16, TextAnchor.MiddleCenter);
        label.color = Color.white;
        if (TryGetVisual(itemId, out var icon, out var tint) && icon != null)
        {
            image.sprite = icon;
            image.type = Image.Type.Simple;
            image.color = tint;
        }

        var handler = rect.gameObject.AddComponent<GridInventoryUnityUISpawnItem>();
        handler.Bind(this, itemId);
        return rect.gameObject;
    }

    private bool CreateSpawnSelector()
    {
        if (spawnRoot == null || gridInventoryService == null) return false;
        var validItemIds = GetValidSpawnItemIds();
        if (validItemIds.Count == 0) return false;

        if (string.IsNullOrWhiteSpace(selectedSpawnItemId) || !validItemIds.Contains(selectedSpawnItemId))
        {
            selectedSpawnItemId = validItemIds[0];
        }

        var dropdownRect = CreateRect("SpawnItemDropdown", spawnRoot, new Vector2(420f, 32f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -1f));
        spawnButtons.Add(dropdownRect.gameObject);
        var dropdownImage = dropdownRect.gameObject.AddComponent<Image>();
        dropdownImage.color = new Color(0.14f, 0.14f, 0.14f, 0.98f);
        spawnDropdown = dropdownRect.gameObject.AddComponent<Dropdown>();
        spawnDropdown.targetGraphic = dropdownImage;
        spawnDropdown.captionText = CreateText("Label", dropdownRect, selectedSpawnItemId, 16, TextAnchor.MiddleLeft);
        spawnDropdown.captionText.rectTransform.offsetMin = new Vector2(10f, 0f);
        spawnDropdown.captionText.rectTransform.offsetMax = new Vector2(-28f, 0f);

        var arrow = CreateText("Arrow", dropdownRect, "v", 18, TextAnchor.MiddleCenter);
        arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
        arrow.rectTransform.anchorMax = new Vector2(1f, 1f);
        arrow.rectTransform.sizeDelta = new Vector2(26f, 0f);
        arrow.rectTransform.anchoredPosition = new Vector2(-13f, 0f);

        var template = CreateDropdownTemplate(dropdownRect);
        spawnDropdown.template = template;
        spawnDropdown.itemText = template.GetComponentInChildren<Toggle>(true).GetComponentInChildren<Text>(true);
        spawnDropdown.ClearOptions();
        spawnDropdown.AddOptions(validItemIds.Select(GetItemTitle).ToList());
        spawnDropdown.value = Mathf.Max(0, validItemIds.IndexOf(selectedSpawnItemId));
        spawnDropdown.RefreshShownValue();
        spawnDropdown.onValueChanged.AddListener(index =>
        {
            if (index < 0 || index >= validItemIds.Count) return;
            selectedSpawnItemId = validItemIds[index];
            UpdateSpawnDragEntryVisual();
        });

        var dragEntry = CreateSpawnEntry(spawnRoot, selectedSpawnItemId, new Vector2(260f, 32f), new Vector2(432f, -1f));
        dragEntry.name = "SpawnSelectedItem";
        spawnDragHandler = dragEntry.GetComponent<GridInventoryUnityUISpawnItem>();
        spawnDragLabel = dragEntry.GetComponentInChildren<Text>(true);
        spawnDragImage = dragEntry.GetComponent<Image>();
        spawnButtons.Add(dragEntry);
        var selector = spawnDropdown.GetComponent<GridInventoryUnityUISpawnSelector>();
        if (selector == null) selector = spawnDropdown.gameObject.AddComponent<GridInventoryUnityUISpawnSelector>();
        selector.Bind(this, spawnDragHandler, validItemIds);
        UpdateSpawnDragEntryVisual();
        AutoAlignSpawnSelectorControls();
        return true;
    }

    private void AutoAlignSpawnSelectorControls()
    {
        if (!autoAvoidSpawnToolOverlap || spawnRoot == null || spawnDropdown == null || spawnDragHandler == null) return;
        var dropdownRect = spawnDropdown.transform as RectTransform;
        var dragRect = spawnDragHandler.transform as RectTransform;
        if (dropdownRect == null || dragRect == null) return;
        if (dropdownRect.parent != spawnRoot || dragRect.parent != spawnRoot) return;

        var parentWidth = Mathf.Max(spawnRoot.rect.width, spawnRoot.sizeDelta.x, gridRoot != null ? gridRoot.sizeDelta.x : 0f, minToolbarWidth);
        if (parentWidth <= 1f) return;

        var dropdownBounds = GetTopLeftRect(dropdownRect);
        var dragBounds = GetTopLeftRect(dragRect);
        var overlaps = dropdownBounds.Overlaps(dragBounds);
        var dragOutRight = dragBounds.xMax > parentWidth;
        var dropdownOutRight = dropdownBounds.xMax > parentWidth;
        if (!overlaps && !dragOutRight && !dropdownOutRight) return;

        dragRect.anchorMin = new Vector2(0f, 1f);
        dragRect.anchorMax = new Vector2(0f, 1f);
        dragRect.pivot = new Vector2(0f, 1f);

        var spacing = Mathf.Max(0f, spawnToolSpacing);
        var rightSideX = dropdownRect.anchoredPosition.x + dropdownRect.sizeDelta.x + spacing;
        if (!dropdownOutRight && rightSideX + dragRect.sizeDelta.x <= parentWidth)
        {
            dragRect.anchoredPosition = new Vector2(rightSideX, dropdownRect.anchoredPosition.y);
            return;
        }

        dragRect.anchoredPosition = new Vector2(dropdownRect.anchoredPosition.x, dropdownRect.anchoredPosition.y - dropdownRect.sizeDelta.y - spacing);
    }

    private Rect GetTopLeftRect(RectTransform rect)
    {
        var size = rect.sizeDelta;
        var x = rect.anchoredPosition.x - size.x * rect.pivot.x;
        var yTop = rect.anchoredPosition.y + size.y * (1f - rect.pivot.y);
        return new Rect(x, yTop - size.y, size.x, size.y);
    }

    private List<string> GetValidSpawnItemIds()
    {
        var validItemIds = new List<string>();
        for (var i = 0; i < spawnItemIds.Count; i++)
        {
            var itemId = spawnItemIds[i];
            if (string.IsNullOrWhiteSpace(itemId)) continue;
            if (!gridInventoryService.TryGetItemDefinition(itemId, out _)) continue;
            if (validItemIds.Contains(itemId)) continue;
            validItemIds.Add(itemId);
        }

        return validItemIds;
    }

    private RectTransform CreateDropdownTemplate(RectTransform dropdownRect)
    {
        var template = CreateRect("Template", dropdownRect, new Vector2(420f, 240f), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -34f));
        var templateImage = template.gameObject.AddComponent<Image>();
        templateImage.color = new Color(0.08f, 0.08f, 0.08f, 0.98f);
        var scrollRect = template.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        var viewport = CreateRect("Viewport", template, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0f, 1f), Vector2.zero);
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        var mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = Color.white;

        var content = CreateRect("Content", viewport, new Vector2(420f, 32f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector2.zero);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        scrollRect.viewport = viewport;
        scrollRect.content = content;

        var item = CreateRect("Item", content, new Vector2(420f, 30f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector2.zero);
        var itemToggle = item.gameObject.AddComponent<Toggle>();
        var itemBackground = item.gameObject.AddComponent<Image>();
        itemBackground.color = new Color(0.18f, 0.18f, 0.18f, 0.95f);
        itemToggle.targetGraphic = itemBackground;
        var itemLabel = CreateText("Item Label", item, "Option", 15, TextAnchor.MiddleLeft);
        itemLabel.rectTransform.offsetMin = new Vector2(10f, 0f);
        itemLabel.rectTransform.offsetMax = new Vector2(-10f, 0f);

        template.gameObject.SetActive(false);
        return template;
    }

    private void UpdateSpawnDragEntryVisual()
    {
        if (string.IsNullOrWhiteSpace(selectedSpawnItemId)) return;
        if (spawnDragHandler != null) spawnDragHandler.Bind(this, selectedSpawnItemId);
        if (spawnDragLabel != null) spawnDragLabel.text = GetItemTitle(selectedSpawnItemId);
        if (spawnDragImage == null) return;

        if (TryGetVisual(selectedSpawnItemId, out var icon, out var tint) && icon != null)
        {
            spawnDragImage.sprite = icon;
            spawnDragImage.type = Image.Type.Simple;
            spawnDragImage.color = tint;
            return;
        }

        spawnDragImage.sprite = null;
        spawnDragImage.type = Image.Type.Simple;
        spawnDragImage.color = new Color(0.16f, 0.16f, 0.16f, 0.95f);
    }

    private void ClearChildren(RectTransform root)
    {
        if (root == null) return;
        for (var i = root.childCount - 1; i >= 0; i--)
        {
            Destroy(root.GetChild(i).gameObject);
        }
    }

    private void ClearRuntimeItemChildren()
    {
        if (itemLayerRoot == null) return;
        for (var i = itemLayerRoot.childCount - 1; i >= 0; i--)
        {
            var child = itemLayerRoot.GetChild(i);
            if (child.GetComponent<GridInventoryEditorItem>() != null) continue;
            Destroy(child.gameObject);
        }
    }

    private void ClearButtonObjects(List<GameObject> objects)
    {
        for (var i = 0; i < objects.Count; i++)
        {
            Destroy(objects[i]);
        }
        objects.Clear();
    }
}

public class GridInventoryUnityUIDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private RectTransform target;
    private Canvas canvas;

    public void Bind(RectTransform targetRect, Canvas ownerCanvas)
    {
        target = targetRect;
        canvas = ownerCanvas;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (target != null) target.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (target == null) return;
        var scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        target.anchoredPosition += eventData.delta / scale;
    }
}

public class GridInventoryUnityUIItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    private GridInventoryUnityUI controller;
    private string instanceId;
    private Image image;
    private Text label;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private bool revealed = true;

    public void Bind(GridInventoryUnityUI uiController, string id, Image targetImage, Text targetLabel)
    {
        controller = uiController;
        instanceId = id;
        image = targetImage;
        label = targetLabel;
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void SetBounds(int row, int column, int width, int height, float cellSize, float spacing)
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        rectTransform.anchoredPosition = new Vector2(column * (cellSize + spacing), -row * (cellSize + spacing));
        rectTransform.sizeDelta = new Vector2(width * cellSize + Mathf.Max(0, width - 1) * spacing, height * cellSize + Mathf.Max(0, height - 1) * spacing);
    }

    public void SetVisual(int stackCount, int maxStack, bool selected, bool mergeHighlighted, Color selectedColor, Color mergeColor, Sprite icon, Color tint)
    {
        if (image != null)
        {
            image.sprite = icon;
            image.type = Image.Type.Simple;
            image.color = mergeHighlighted ? mergeColor : selected ? selectedColor : tint;
        }
        if (label != null)
        {
            label.alignment = TextAnchor.LowerRight;
            if (maxStack > 1) label.text = $"{stackCount}/{maxStack}";
            else label.text = string.Empty;
        }
    }

    public void SetDragging(bool dragging)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = dragging ? 0.35f : 1f;
    }

    /// <summary>
    /// 刷新单个格子物品的搜索蒙版、搜索中文字及填充进度。
    /// </summary>
    public void SetSearchState(
        bool isRevealed,
        bool searching,
        float progress,
        Color unrevealedColor,
        Color searchingColor,
        Sprite maskSprite,
        string unrevealedText,
        string searchingTextFormat,
        Color textColor,
        int textFontSize)
    {
        revealed = isRevealed;
        if (revealed) return;

        if (image != null)
        {
            image.sprite = maskSprite;
            image.color = searching ? searchingColor : unrevealedColor;
        }
        if (label != null)
        {
            label.alignment = TextAnchor.MiddleCenter;
            label.color = textColor;
            label.fontSize = Mathf.Clamp(textFontSize, 8, 40);
            label.text = searching
                ? FormatSearchText(searchingTextFormat, progress)
                : string.IsNullOrEmpty(unrevealedText) ? "?" : unrevealedText;
        }
    }

    /// <summary>
    /// 将搜索进度传入可配置格式；格式错误时使用安全的默认文案，避免 UI 刷新中断。
    /// </summary>
    private static string FormatSearchText(string format, float progress)
    {
        if (string.IsNullOrWhiteSpace(format)) return $"搜索中\n{Mathf.Clamp01(progress):P0}";
        try
        {
            return string.Format(format, Mathf.Clamp01(progress));
        }
        catch (System.FormatException)
        {
            return $"搜索中\n{Mathf.Clamp01(progress):P0}";
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!revealed) return;
        if (rectTransform != null) rectTransform.SetAsLastSibling();
        controller?.HandleItemBeginDrag(instanceId, eventData.position, rectTransform);
        if (canvasGroup != null) canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        controller?.HandleItemDrag(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        controller?.HandleItemEndDrag();
        if (canvasGroup != null) canvasGroup.blocksRaycasts = true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!revealed) return;
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            controller?.HandleItemRightClick(instanceId, eventData.position);
            return;
        }

        controller?.HandleItemClick(instanceId);
    }
}

public class GridInventoryUnityUISpawnItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    private GridInventoryUnityUI controller;
    private string itemId;

    public void Bind(GridInventoryUnityUI uiController, string id)
    {
        controller = uiController;
        itemId = id;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        controller?.HandleSpawnBeginDrag(itemId, eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        controller?.HandleSpawnDrag(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        controller?.HandleSpawnEndDrag();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        controller?.HandleSpawnClick(itemId);
    }
}

[ExecuteAlways]
[RequireComponent(typeof(Dropdown))]
public sealed class GridInventoryUnityUISpawnSelector : MonoBehaviour
{
    [SerializeField] private GridInventoryUnityUI inventoryUi;
    [SerializeField] private GridInventoryUnityUISpawnItem spawnItem;
    [SerializeField] private Image previewImage;
    [SerializeField] private Text previewLabel;
    [SerializeField] private List<string> itemIds = new List<string>();

    private Dropdown dropdown;
    private int lastValue = -1;

    public void Bind(GridInventoryUnityUI ui, GridInventoryUnityUISpawnItem item, IReadOnlyList<string> ids)
    {
        inventoryUi = ui;
        spawnItem = item;
        previewImage = item != null ? item.GetComponent<Image>() : previewImage;
        previewLabel = item != null ? item.GetComponentInChildren<Text>(true) : previewLabel;
        itemIds.Clear();
        if (ids != null) itemIds.AddRange(ids);
        EnsureDropdown();
        ApplySelection();
    }

    private void OnEnable()
    {
        EnsureDropdown();
        ApplySelection();
    }

    private void OnValidate()
    {
        EnsureDropdown();
        ApplySelection();
    }

    private void Update()
    {
        EnsureDropdown();
        if (dropdown == null || dropdown.value == lastValue) return;
        ApplySelection();
    }

    private void EnsureDropdown()
    {
        if (dropdown == null) dropdown = GetComponent<Dropdown>();
    }

    private void ApplySelection()
    {
        if (dropdown == null || inventoryUi == null || itemIds.Count == 0) return;
        var index = Mathf.Clamp(dropdown.value, 0, itemIds.Count - 1);
        lastValue = index;
        var itemId = itemIds[index];

        inventoryUi.HandleSpawnClick(itemId);
        if (spawnItem != null) spawnItem.Bind(inventoryUi, itemId);
        if (previewLabel != null) previewLabel.text = inventoryUi.GetSpawnItemTitle(itemId);
        if (previewImage == null) return;

        if (inventoryUi.TryGetSpawnItemVisual(itemId, out var icon, out var tint) && icon != null)
        {
            previewImage.sprite = icon;
            previewImage.type = Image.Type.Simple;
            previewImage.color = tint;
            return;
        }

        previewImage.sprite = null;
        previewImage.type = Image.Type.Simple;
        previewImage.color = new Color(0.16f, 0.16f, 0.16f, 0.95f);
    }
}
