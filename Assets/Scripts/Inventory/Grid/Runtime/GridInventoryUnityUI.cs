using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class GridInventoryUnityUI : MonoBehaviour
{
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
    private InputField splitCountInput;
    private Text splitCountLabel;
    private string contextMenuInstanceId;
    private int contextMenuMaxSplitCount = 1;
    private readonly Dictionary<string, GridItemVisual> visualByItemId = new();
    private bool initialized;
    private bool startVisibilityApplied;
    private bool isSideContainerView;
    private GridInventoryUnityUI ownerView;
    private string previewContainerId;
    private bool toolRootBaseCaptured;
    private float toolRootBaseX;
    private float toolRootBaseZ;
    private readonly Dictionary<string, GridInventoryUnityUI> sideContainerViews = new();
    private static readonly List<GridInventoryUnityUI> ActiveViews = new();
    private static GridInventoryUnityUI activeDragOwner;

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
        UpdateToolRootPosition();
    }

    private bool TryInitialize()
    {
        if (initialized) return true;
        if (gridInventoryService == null) gridInventoryService = FindObjectOfType<GridInventoryService>();
        if (gridInventoryService == null) return false;
        gridInventoryService.EnsureInitialized();
        ApplyCompactLayoutDefaults();
        BuildVisualMap();
        EnsureEventSystem();
        EnsureLayout();
        if (toolbarRoot == null || spawnRoot == null || actionRoot == null || gridRoot == null || itemLayerRoot == null || ghostRoot == null) return false;
        ResolveContainerId();
        RebuildStaticButtons();
        RebuildGrid();
        RefreshItems();
        initialized = true;
        ApplyStartVisibility();
        return true;
    }

    public void Bind(GridInventoryService service)
    {
        if (service == null) return;
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

    private void ConfigureSideContainerView(GridInventoryUnityUI source, GridInventoryService service, string containerId)
    {
        ownerView = source;
        isSideContainerView = true;
        gridInventoryService = service;
        currentContainerId = containerId;
        startOpen = true;
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
        autoResizePanelBackground = source.autoResizePanelBackground;
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

    public void SetOpen(bool open)
    {
        if (!initialized && !TryInitialize()) return;
        if (canvas != null) canvas.enabled = open;
    }

    public void ToggleOpen()
    {
        if (!initialized && !TryInitialize()) return;
        if (canvas != null) canvas.enabled = !canvas.enabled;
    }

    private void ApplyStartVisibility()
    {
        if (startVisibilityApplied) return;
        startVisibilityApplied = true;
        if (canvas != null) canvas.enabled = startOpen;
    }

    private void ApplyCompactLayoutDefaults()
    {
        if (!useCompactLayout) return;
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
        ClearChildren(itemLayerRoot);
        ClearChildren(ghostRoot);
        gridRoot.SetAsFirstSibling();
        cellLayerRoot.SetAsLastSibling();
        itemLayerRoot.SetAsLastSibling();
        ghostRoot.SetAsLastSibling();
        if (toolRoot != null) toolRoot.SetAsLastSibling();
        if (titleText != null) titleText.transform.SetAsLastSibling();
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
        if (gridRootOverride == null || toolbarRootOverride == null || spawnRootOverride == null || actionRootOverride == null) return false;
        if (itemLayerRootOverride == null || ghostRootOverride == null) return false;

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
        itemLayerRoot.SetAsLastSibling();
        ghostRoot.SetAsLastSibling();
        return true;
    }

    private void RefreshTitle()
    {
        if (titleText == null) return;
        var selectedText = string.IsNullOrWhiteSpace(selectedInstanceId) ? "未选择" : GetSelectedItemTitle(selectedInstanceId);
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
            if (showActionToolbar && !hasAutoSortActionButton)
            {
                actionButtons.Add(CreateAutoSortButton(GetNextActionButtonX()).gameObject);
            }
            return;
        }

        if (showContainerToolbar && !toolbarHandled)
        {
            var containerX = 0f;
            foreach (var containerId in gridInventoryService.ContainerIds)
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
        ClearChildren(itemLayerRoot);
        ClearChildren(ghostRoot);
        itemViewByInstanceId.Clear();
        HideContextMenu();
        previewGhostRect = null;
        previewGhostImage = null;
        previewGhostText = null;

        var width = columns * cellSize + Mathf.Max(0, columns - 1) * cellSpacing;
        var height = rows * cellSize + Mathf.Max(0, rows - 1) * cellSpacing;
        var toolbarWidth = GetToolbarRequiredWidth(width);
        gridRoot.sizeDelta = new Vector2(width, height);
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

        var displayed = new HashSet<string>();
        var instances = gridInventoryService.ItemInstances.Values
            .Where(x => x.containerId == currentContainerId && x.originRow >= 0 && x.originColumn >= 0)
            .ToList();

        for (var i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
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
            view.SetDragging(instance.instanceId == draggingInstanceId);
            displayed.Add(instance.instanceId);
        }

        var staleIds = itemViewByInstanceId.Keys.Where(id => !displayed.Contains(id)).ToList();
        for (var i = 0; i < staleIds.Count; i++)
        {
            var staleId = staleIds[i];
            if (itemViewByInstanceId.TryGetValue(staleId, out var staleView))
            {
                Destroy(staleView.gameObject);
            }
            itemViewByInstanceId.Remove(staleId);
            if (selectedInstanceId == staleId) selectedInstanceId = null;
            if (contextMenuInstanceId == staleId) HideContextMenu();
        }
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
        if (hasCell && TryGetMergeTargetFootprint(targetContainerId, row, column, out var mergeInstanceId, out var mergeRow, out var mergeColumn, out var mergeWidth, out var mergeHeight))
        {
            previewMergeInstanceId = mergeInstanceId;
            previewMergeRow = mergeRow;
            previewMergeColumn = mergeColumn;
            previewMergeWidth = mergeWidth;
            previewMergeHeight = mergeHeight;
        }
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

    private void UpdateToolRootPosition()
    {
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
        foreach (var containerId in gridInventoryService.ContainerIds)
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
        HideContextMenu();
        selectedInstanceId = selectedInstanceId == instanceId ? null : instanceId;
    }

    public void HandleItemRightClick(string instanceId, Vector2 screenPosition)
    {
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

    private bool TryGetVisual(string itemId, out Sprite icon, out Color tint)
    {
        icon = null;
        tint = new Color(0.31f, 0.48f, 0.72f, 0.95f);
        if (!visualByItemId.TryGetValue(itemId, out var visual)) return false;
        icon = visual.icon;
        tint = visual.tint.a <= 0f ? Color.white : visual.tint;
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
        }

        return true;
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
        foreach (var containerId in gridInventoryService.ContainerIds)
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
        if (sideContainerViews.TryGetValue(containerId, out var existing) && existing != null)
        {
            Destroy(existing.gameObject);
            sideContainerViews.Remove(containerId);
            return;
        }

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
            sideRect.anchoredPosition = panelRoot.anchoredPosition + sidePanelOffset * (sideContainerViews.Count + 1);
        }
        else
        {
            sideRect.anchorMin = new Vector2(0f, 1f);
            sideRect.anchorMax = new Vector2(0f, 1f);
            sideRect.pivot = new Vector2(0f, 1f);
            sideRect.sizeDelta = new Vector2(380f, 520f);
            sideRect.anchoredPosition = sidePanelOffset * (sideContainerViews.Count + 1);
        }

        var sideView = sideObject.AddComponent<GridInventoryUnityUI>();
        sideView.ConfigureSideContainerView(this, gridInventoryService, containerId);
        sideContainerViews[containerId] = sideView;
        sideObject.SetActive(true);
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
        return true;
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
            if (maxStack > 1) label.text = $"{stackCount}/{maxStack}";
            else label.text = string.Empty;
        }
    }

    public void SetDragging(bool dragging)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = dragging ? 0.35f : 1f;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
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
