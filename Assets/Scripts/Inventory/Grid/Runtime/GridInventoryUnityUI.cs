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
        public string itemId;
        public Sprite icon;
        public Color tint;
    }

    [Tooltip("网格背包服务引用。")]
    [SerializeField] private GridInventoryService gridInventoryService;
    [Tooltip("是否运行时自动搭建UI结构。关闭后可手动拖拽绑定下方Root引用。")]
    [SerializeField] private bool buildRuntimeLayout = true;
    [Tooltip("自动布局模式下是否自动对齐工具栏与网格。关闭后可直接在Inspector调整各子节点位置。")]
    [SerializeField] private bool autoAlignToolbarWithGrid = true;
    [Tooltip("网格尺寸变化时是否自动调整面板背景尺寸。")]
    [SerializeField] private bool autoResizePanelBackground = true;
    [Tooltip("面板背景右/下额外留白。")]
    [SerializeField] private Vector2 panelBackgroundPadding = new(40f, 40f);
    [Tooltip("面板背景图。为空时使用纯色。")]
    [SerializeField] private Sprite panelBackgroundSprite;
    [Tooltip("网格背景图。为空时使用纯色。")]
    [SerializeField] private Sprite gridBackgroundSprite;
    [Tooltip("面板背景颜色。")]
    [SerializeField] private Color panelBackgroundColor = new(0.06f, 0.06f, 0.06f, 0.92f);
    [Tooltip("网格背景颜色。")]
    [SerializeField] private Color gridBackgroundColor = new(0.1f, 0.1f, 0.1f, 0.75f);
    [Tooltip("默认展示容器。为空时自动取第一个容器。")]
    [SerializeField] private string currentContainerId = "main_bag";
    [Tooltip("UI开关按键。")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [Tooltip("格子尺寸。")]
    [SerializeField] private float cellSize = 56f;
    [Tooltip("格子间距。")]
    [SerializeField] private float cellSpacing = 4f;
    [Tooltip("面板锚点位置。")]
    [SerializeField] private Vector2 panelAnchoredPosition = new(40f, -40f);
    [Tooltip("面板内左侧边距。")]
    [SerializeField] private float panelLeftPadding = 20f;
    [Tooltip("标题到工具栏起始Y偏移。")]
    [SerializeField] private float toolbarTopOffset = 62f;
    [Tooltip("工具栏行高。")]
    [SerializeField] private float toolbarHeight = 44f;
    [Tooltip("工具栏间距。")]
    [SerializeField] private float toolbarSpacing = 6f;
    [Tooltip("动作栏到网格间距。")]
    [SerializeField] private float gridTopSpacing = 10f;
    [Tooltip("可生成物品ID列表。")]
    [SerializeField] private List<string> spawnItemIds = new() { "food_canned", "water_bottle", "bandage", "cloth_raw" };
    [Tooltip("空格颜色。")]
    [SerializeField] private Color emptyCellColor = new(0.12f, 0.12f, 0.12f, 0.88f);
    [Tooltip("占用格颜色。")]
    [SerializeField] private Color occupiedCellColor = new(0.2f, 0.3f, 0.42f, 0.9f);
    [Tooltip("选中物品颜色。")]
    [SerializeField] private Color selectedItemColor = new(0.88f, 0.75f, 0.33f, 0.95f);
    [Tooltip("拖拽可放置预览颜色。")]
    [SerializeField] private Color previewValidColor = new(0.25f, 0.85f, 0.4f, 0.45f);
    [Tooltip("拖拽冲突预览颜色。")]
    [SerializeField] private Color previewInvalidColor = new(0.95f, 0.2f, 0.2f, 0.45f);
    [Tooltip("物品图标映射。")]
    [SerializeField] private List<GridItemVisual> itemVisuals = new();
    [Tooltip("手动布局模式下的标题文本。")]
    [SerializeField] private Text titleTextOverride;
    [Tooltip("手动布局模式下的容器栏Root。")]
    [SerializeField] private RectTransform toolbarRootOverride;
    [Tooltip("手动布局模式下的资源栏Root。")]
    [SerializeField] private RectTransform spawnRootOverride;
    [Tooltip("手动布局模式下的动作栏Root。")]
    [SerializeField] private RectTransform actionRootOverride;
    [Tooltip("手动布局模式下的网格Root。")]
    [SerializeField] private RectTransform gridRootOverride;
    [Tooltip("手动布局模式下的工具区总Root（例如ToolRoot）。")]
    [SerializeField] private RectTransform toolRootOverride;
    [Tooltip("是否让工具区总Root随网格高度自动下移，避免背包变大后重叠。")]
    [SerializeField] private bool autoRepositionToolRoot = true;
    [Tooltip("工具区总Root与网格底部的垂直间距。")]
    [SerializeField] private float toolRootSpacingFromGrid = 12f;
    [Tooltip("手动布局模式下面板背景Image。可用于直接替换美术资源。")]
    [SerializeField] private Image panelBackgroundOverride;
    [Tooltip("手动布局模式下网格背景Image。可用于直接替换美术资源。")]
    [SerializeField] private Image gridBackgroundOverride;
    [Tooltip("手动布局模式下的格子层Root。")]
    [SerializeField] private RectTransform cellLayerRootOverride;
    [Tooltip("手动布局模式下的物品层Root。")]
    [SerializeField] private RectTransform itemLayerRootOverride;
    [Tooltip("手动布局模式下的预览层Root。")]
    [SerializeField] private RectTransform ghostRootOverride;

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
    private int dragPointerOffsetRow;
    private int dragPointerOffsetColumn;
    private readonly Dictionary<string, GridInventoryUnityUIItem> itemViewByInstanceId = new();
    private readonly List<GameObject> toolbarButtons = new();
    private readonly List<GameObject> spawnButtons = new();
    private Image[,] cellImages;
    private RectTransform previewGhostRect;
    private Image previewGhostImage;
    private Text previewGhostText;
    private readonly Dictionary<string, GridItemVisual> visualByItemId = new();
    private bool initialized;
    private bool toolRootBaseCaptured;
    private float toolRootBaseX;
    private float toolRootBaseZ;

    private void Awake()
    {
        TryInitialize();
    }

    private void Update()
    {
        if (!initialized && !TryInitialize()) return;

        if (Input.GetKeyDown(toggleKey))
        {
            if (canvas != null) canvas.enabled = !canvas.enabled;
        }

        if (canvas == null || !canvas.enabled) return;
        if (gridInventoryService == null) return;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            selectedInstanceId = null;
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
        BuildVisualMap();
        EnsureEventSystem();
        EnsureLayout();
        if (toolbarRoot == null || spawnRoot == null || actionRoot == null || gridRoot == null || itemLayerRoot == null || ghostRoot == null) return false;
        ResolveContainerId();
        RebuildStaticButtons();
        RebuildGrid();
        RefreshItems();
        initialized = true;
        return true;
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
        if (!buildRuntimeLayout && TryBindManualLayout()) return;

        canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        EnsureCanvasScalerMode();

        panelRoot = CreateRect("Panel", transform, new Vector2(860f, 760f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), panelAnchoredPosition);
        panelBackgroundImage = panelRoot.gameObject.AddComponent<Image>();
        ApplyBackgroundStyle(panelBackgroundImage, panelBackgroundSprite, panelBackgroundColor);

        titleText = CreateText("Title", panelRoot, "Grid Inventory", 24, TextAnchor.MiddleLeft);
        var titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(20f, -14f);
        titleRect.sizeDelta = new Vector2(-40f, 36f);

        toolbarRoot = CreateRect("ContainerToolbar", panelRoot, new Vector2(820f, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(panelLeftPadding, -toolbarTopOffset));
        spawnRoot = CreateRect("SpawnToolbar", panelRoot, new Vector2(820f, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(panelLeftPadding, -(toolbarTopOffset + toolbarHeight + toolbarSpacing)));
        actionRoot = CreateRect("ActionToolbar", panelRoot, new Vector2(820f, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(panelLeftPadding, -(toolbarTopOffset + (toolbarHeight + toolbarSpacing) * 2f)));

        gridRoot = CreateRect("Grid", panelRoot, new Vector2(820f, 560f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(panelLeftPadding, -(toolbarTopOffset + (toolbarHeight + toolbarSpacing) * 3f + gridTopSpacing)));
        gridBackgroundImage = gridRoot.gameObject.AddComponent<Image>();
        ApplyBackgroundStyle(gridBackgroundImage, gridBackgroundSprite, gridBackgroundColor);
        cellLayerRoot = CreateRect("Cells", gridRoot, gridRoot.sizeDelta, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
        itemLayerRoot = CreateRect("Items", gridRoot, gridRoot.sizeDelta, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
        ghostRoot = CreateRect("Ghost", gridRoot, gridRoot.sizeDelta, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
        cellLayerRoot.SetAsFirstSibling();
        itemLayerRoot.SetAsLastSibling();
        ghostRoot.SetAsLastSibling();
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
        var selectedText = string.IsNullOrWhiteSpace(selectedInstanceId) ? "None" : GetShortId(selectedInstanceId);
        titleText.text = $"Grid Inventory  [{currentContainerId}]  Selected:{selectedText}";
    }

    private void RebuildStaticButtons()
    {
        if (gridInventoryService == null) return;
        if (toolbarRoot == null || spawnRoot == null || actionRoot == null) return;
        ClearButtonObjects(toolbarButtons);
        ClearButtonObjects(spawnButtons);

        var containerX = 0f;
        foreach (var containerId in gridInventoryService.ContainerIds)
        {
            var capturedContainer = containerId;
            var button = CreateButton(toolbarRoot, containerId, new Vector2(120f, 34f), new Vector2(containerX, -2f), () =>
            {
                currentContainerId = capturedContainer;
                selectedInstanceId = null;
                RebuildGrid();
                RefreshItems();
            });
            toolbarButtons.Add(button.gameObject);
            containerX += 126f;
        }

        var spawnX = 0f;
        for (var i = 0; i < spawnItemIds.Count; i++)
        {
            var itemId = spawnItemIds[i];
            if (string.IsNullOrWhiteSpace(itemId)) continue;
            var entry = CreateSpawnEntry(spawnRoot, itemId, new Vector2(150f, 34f), new Vector2(spawnX, -2f));
            spawnButtons.Add(entry);
            spawnX += 156f;
        }

        CreateButton(actionRoot, "Auto Sort", new Vector2(130f, 34f), new Vector2(0f, -2f), () =>
        {
            if (string.IsNullOrWhiteSpace(currentContainerId)) return;
            gridInventoryService.AutoSort(currentContainerId);
        });

        CreateButton(actionRoot, "Rotate", new Vector2(130f, 34f), new Vector2(136f, -2f), () =>
        {
            if (string.IsNullOrWhiteSpace(selectedInstanceId)) return;
            gridInventoryService.TryRotate(selectedInstanceId);
        });

        CreateButton(actionRoot, "Remove", new Vector2(130f, 34f), new Vector2(272f, -2f), () =>
        {
            if (string.IsNullOrWhiteSpace(selectedInstanceId)) return;
            if (gridInventoryService.TryRemoveInstance(selectedInstanceId)) selectedInstanceId = null;
        });

        CreateButton(actionRoot, "Split 1", new Vector2(130f, 34f), new Vector2(408f, -2f), () =>
        {
            if (string.IsNullOrWhiteSpace(selectedInstanceId)) return;
            gridInventoryService.TrySplitStack(selectedInstanceId, 1, out _);
        });
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
        previewGhostRect = null;
        previewGhostImage = null;
        previewGhostText = null;

        var width = columns * cellSize + Mathf.Max(0, columns - 1) * cellSpacing;
        var height = rows * cellSize + Mathf.Max(0, rows - 1) * cellSpacing;
        gridRoot.sizeDelta = new Vector2(width, height);
        if (autoResizePanelBackground && panelRoot != null)
        {
            var requiredWidth = panelLeftPadding + width + panelBackgroundPadding.x;
            var requiredHeight = toolbarTopOffset + 3f * toolbarHeight + 2f * toolbarSpacing + gridTopSpacing + height + panelBackgroundPadding.y;
            panelRoot.sizeDelta = new Vector2(requiredWidth, requiredHeight);
        }
        if (buildRuntimeLayout && autoAlignToolbarWithGrid) AlignToolbarAndGrid(width);
        cellLayerRoot.sizeDelta = gridRoot.sizeDelta;
        itemLayerRoot.sizeDelta = gridRoot.sizeDelta;
        ghostRoot.sizeDelta = gridRoot.sizeDelta;
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
            view.SetVisual(instance.stackCount, definition.maxStack, instance.instanceId == selectedInstanceId, selectedItemColor, icon, tint);
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
        if (!GridPlacementSolver.TryGetFootprint(definition, draggingRotation, out var width, out var height)) return;

        previewGhostRect.sizeDelta = new Vector2(width * cellSize + Mathf.Max(0, width - 1) * cellSpacing, height * cellSize + Mathf.Max(0, height - 1) * cellSpacing);
        if (!string.IsNullOrWhiteSpace(draggingInstanceId) && gridInventoryService.TryGetItemInstance(draggingInstanceId, out var draggingInstance))
        {
            previewGhostText.text = $"{draggingInstance.itemId}\nx{draggingInstance.stackCount}";
        }
        else
        {
            previewGhostText.text = $"{draggingItemId}\nx1";
        }

        var hasCell = TryGetCellByScreenPosition(screenPosition, out var row, out var column);
        var targetRow = hasCell ? row - dragPointerOffsetRow : -1;
        var targetColumn = hasCell ? column - dragPointerOffsetColumn : -1;
        if (hasCell && !string.IsNullOrWhiteSpace(draggingInstanceId))
        {
            previewCanPlace = gridInventoryService.CanPlace(draggingInstanceId, currentContainerId, targetRow, targetColumn, draggingRotation);
        }
        else if (hasCell && !string.IsNullOrWhiteSpace(draggingSpawnItemId))
        {
            previewCanPlace = gridInventoryService.CanSpawn(draggingSpawnItemId, currentContainerId, targetRow, targetColumn, draggingRotation);
        }
        else
        {
            previewCanPlace = false;
        }
        previewRow = hasCell ? targetRow : -1;
        previewColumn = hasCell ? targetColumn : -1;
        previewGhostImage.color = previewCanPlace ? previewValidColor : previewInvalidColor;
        previewGhostRect.position = screenPosition;
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

    private void AlignToolbarAndGrid(float gridWidth)
    {
        if (toolbarRoot == null || spawnRoot == null || actionRoot == null || gridRoot == null) return;
        var firstRowY = -toolbarTopOffset;
        toolbarRoot.anchoredPosition = new Vector2(panelLeftPadding, firstRowY);
        spawnRoot.anchoredPosition = new Vector2(panelLeftPadding, firstRowY - toolbarHeight - toolbarSpacing);
        actionRoot.anchoredPosition = new Vector2(panelLeftPadding, firstRowY - (toolbarHeight + toolbarSpacing) * 2f);
        gridRoot.anchoredPosition = new Vector2(panelLeftPadding, firstRowY - (toolbarHeight + toolbarSpacing) * 3f - gridTopSpacing);
        toolbarRoot.sizeDelta = new Vector2(gridWidth, toolbarHeight);
        spawnRoot.sizeDelta = new Vector2(gridWidth, toolbarHeight);
        actionRoot.sizeDelta = new Vector2(gridWidth, toolbarHeight);
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

        if (string.IsNullOrWhiteSpace(selectedSpawnItemId))
        {
            selectedInstanceId = null;
            return;
        }
        if (TryMergeSpawnIntoCell(selectedSpawnItemId, row, column)) return;
        gridInventoryService.TrySpawnToCell(selectedSpawnItemId, currentContainerId, row, column);
    }

    public void HandleItemClick(string instanceId)
    {
        selectedInstanceId = selectedInstanceId == instanceId ? null : instanceId;
    }

    public void HandleItemBeginDrag(string instanceId, Vector2 screenPosition, RectTransform itemRect)
    {
        if (!gridInventoryService.TryGetItemInstance(instanceId, out var instance)) return;
        selectedInstanceId = instanceId;
        draggingInstanceId = instanceId;
        draggingSpawnItemId = null;
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
        if (previewRow >= 0 && previewColumn >= 0)
        {
            if (TryMergeByInstanceAtCell(draggingInstanceId, previewRow, previewColumn))
            {
                placed = true;
            }
            else if (previewCanPlace)
            {
                placed = gridInventoryService.TryPlace(draggingInstanceId, currentContainerId, previewRow, previewColumn, draggingRotation);
            }
        }

        if (placed) selectedInstanceId = null;
        draggingInstanceId = null;
        draggingSpawnItemId = null;
        dragPointerOffsetRow = 0;
        dragPointerOffsetColumn = 0;
        previewCanPlace = false;
        previewRow = -1;
        previewColumn = -1;
        if (previewGhostRect != null) previewGhostRect.gameObject.SetActive(false);
    }

    public void HandleSpawnClick(string itemId)
    {
        selectedSpawnItemId = itemId;
    }

    public void HandleSpawnBeginDrag(string itemId, Vector2 screenPosition)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return;
        selectedSpawnItemId = itemId;
        draggingSpawnItemId = itemId;
        draggingInstanceId = null;
        draggingRotation = GridItemRotation.Deg0;
        dragPointerOffsetRow = 0;
        dragPointerOffsetColumn = 0;
        EnsurePreviewGhost();
        previewGhostRect.gameObject.SetActive(true);
        UpdateDragPreview(screenPosition);
    }

    public void HandleSpawnDrag(Vector2 screenPosition)
    {
        if (string.IsNullOrWhiteSpace(draggingSpawnItemId)) return;
        UpdateDragPreview(screenPosition);
    }

    public void HandleSpawnEndDrag()
    {
        if (string.IsNullOrWhiteSpace(draggingSpawnItemId)) return;
        if (previewRow >= 0 && previewColumn >= 0)
        {
            if (!TryMergeSpawnIntoCell(draggingSpawnItemId, previewRow, previewColumn) && previewCanPlace)
            {
                gridInventoryService.TrySpawnToCell(draggingSpawnItemId, currentContainerId, previewRow, previewColumn, draggingRotation);
            }
        }

        draggingSpawnItemId = null;
        draggingInstanceId = null;
        dragPointerOffsetRow = 0;
        dragPointerOffsetColumn = 0;
        previewCanPlace = false;
        previewRow = -1;
        previewColumn = -1;
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
        return TryMergeByInstanceAtCell(selectedInstanceId, row, column);
    }

    private bool TryMergeSpawnIntoCell(string itemId, int row, int column)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return false;
        if (!gridInventoryService.TryGetCellOccupantId(currentContainerId, row, column, out var targetId)) return false;
        if (!gridInventoryService.TryGetItemInstance(targetId, out var targetInstance)) return false;
        if (targetInstance.itemId != itemId) return false;
        if (!gridInventoryService.TryCreateItemInstance(itemId, 1, out var sourceId)) return false;
        if (gridInventoryService.TryMergeStacks(sourceId, targetId)) return true;
        gridInventoryService.TryRemoveInstance(sourceId);
        return false;
    }

    private bool TryMergeByInstanceAtCell(string sourceInstanceId, int row, int column)
    {
        if (string.IsNullOrWhiteSpace(sourceInstanceId)) return false;
        if (!gridInventoryService.TryGetCellOccupantId(currentContainerId, row, column, out var targetId)) return false;
        return gridInventoryService.TryMergeStacks(sourceInstanceId, targetId);
    }

    private bool TryGetVisual(string itemId, out Sprite icon, out Color tint)
    {
        icon = null;
        tint = new Color(0.31f, 0.48f, 0.72f, 0.95f);
        if (!visualByItemId.TryGetValue(itemId, out var visual)) return false;
        icon = visual.icon;
        tint = visual.tint.a <= 0f ? tint : visual.tint;
        return true;
    }

    private string GetShortId(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return "None";
        return instanceId.Length <= 8 ? instanceId : instanceId.Substring(0, 8);
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
            image.type = Image.Type.Sliced;
            image.color = tint;
        }

        var handler = rect.gameObject.AddComponent<GridInventoryUnityUISpawnItem>();
        handler.Bind(this, itemId);
        return rect.gameObject;
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

    public void SetVisual(int stackCount, int maxStack, bool selected, Color selectedColor, Sprite icon, Color tint)
    {
        if (image != null)
        {
            image.sprite = icon;
            image.type = icon != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = selected ? selectedColor : tint;
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
