using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class GridInventoryEditorLayoutBuilder
{
    private struct ContainerViewLayout
    {
        public Text titleText;
        public RectTransform toolRoot;
        public RectTransform toolbarRoot;
        public RectTransform spawnRoot;
        public RectTransform actionRoot;
        public RectTransform gridRoot;
        public RectTransform cellLayerRoot;
        public RectTransform itemLayerRoot;
        public RectTransform ghostRoot;
        public Image panelBackground;
        public Image gridBackground;
    }

    [MenuItem("Tools/Inventory/Sync Container Views")]
    public static void SyncContainerViewsMenu()
    {
        var service = Object.FindObjectOfType<GridInventoryService>(true);
        SyncContainerViews(service);
    }

    [MenuItem("Tools/Inventory/Create Editor Item Nodes")]
    public static void CreateEditorItemNodesMenu()
    {
        var service = Object.FindObjectOfType<GridInventoryService>(true);
        var ui = FindMainInventoryUi();
        if (service == null || ui == null)
        {
            Debug.LogError("GridInventoryEditorLayoutBuilder: GridInventoryService or main GridInventoryUnityUI not found.");
            return;
        }

        BuildMainContainerView(service, ui);
        var containerId = new SerializedObject(ui).FindProperty("currentContainerId").stringValue;
        var itemLayer = ui.transform.Find("ItemLayer") as RectTransform;
        if (itemLayer != null) BuildEditorItems(itemLayer, service, ui, containerId, true);
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        EditorSceneManager.SaveScene(ui.gameObject.scene);
    }

    [MenuItem("Tools/Inventory/Reset Default Editor Item Nodes")]
    public static void ResetDefaultEditorItemNodesMenu()
    {
        var service = Object.FindObjectOfType<GridInventoryService>(true);
        var ui = FindMainInventoryUi();
        if (service == null || ui == null)
        {
            Debug.LogError("GridInventoryEditorLayoutBuilder: GridInventoryService or main GridInventoryUnityUI not found.");
            return;
        }

        BuildMainContainerView(service, ui);
        var source = new SerializedObject(ui);
        var containerId = source.FindProperty("currentContainerId").stringValue;
        var itemLayer = ui.transform.Find("ItemLayer") as RectTransform;
        if (itemLayer == null) return;

        for (var i = itemLayer.childCount - 1; i >= 0; i--)
        {
            if (itemLayer.GetChild(i).GetComponent<GridInventoryEditorItem>() != null)
                Undo.DestroyObjectImmediate(itemLayer.GetChild(i).gameObject);
        }

        CreateDefaultEditorItem(itemLayer, "food_canned", 3, 0, 0, GridItemRotation.Deg0);
        CreateDefaultEditorItem(itemLayer, "water_bottle", 1, 0, 1, GridItemRotation.Deg0);
        CreateDefaultEditorItem(itemLayer, "bandage", 2, 0, 2, GridItemRotation.Deg0);
        CreateDefaultEditorItem(itemLayer, "cloth_raw", 1, 0, 3, GridItemRotation.Deg0);
        BuildEditorItems(itemLayer, service, ui, containerId, false);

        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        EditorSceneManager.SaveScene(ui.gameObject.scene);
    }

    public static void SyncContainerViews(GridInventoryService service)
    {
        var ui = FindMainInventoryUi();
        if (service == null)
        {
            Debug.LogError("GridInventoryEditorLayoutBuilder: GridInventoryService not found in the current scene.");
            return;
        }

        if (ui == null)
        {
            Debug.LogError("GridInventoryEditorLayoutBuilder: main GridInventoryUnityUI not found in the current scene.");
            return;
        }

        BuildMainContainerView(service, ui);
        BuildSideContainerViews(service, ui);
        EditorUtility.SetDirty(service);
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        EditorSceneManager.SaveScene(ui.gameObject.scene);
        Debug.Log("GridInventoryEditorLayoutBuilder: synced editor-visible inventory UI cells.");
    }

    public static GridInventoryUnityUI FindMainInventoryUi()
    {
        var all = Object.FindObjectsOfType<GridInventoryUnityUI>(true);
        GridInventoryUnityUI fallback = null;
        for (var i = 0; i < all.Length; i++)
        {
            var ui = all[i];
            if (ui == null) continue;
            if (fallback == null && !ui.name.StartsWith("InventoryUI_", System.StringComparison.Ordinal)) fallback = ui;

            var serializedObject = new SerializedObject(ui);
            if (serializedObject.FindProperty("currentContainerId").stringValue == "main_bag") return ui;
        }

        for (var i = 0; i < all.Length; i++)
        {
            var ui = all[i];
            if (ui == null) continue;
            var serializedObject = new SerializedObject(ui);
            if (serializedObject.FindProperty("openOtherContainersAsSidePanels").boolValue) return ui;
        }

        return fallback != null ? fallback : all.FirstOrDefault();
    }

    private static void BuildMainContainerView(GridInventoryService service, GridInventoryUnityUI sourceUi)
    {
        var sourceObject = new SerializedObject(sourceUi);
        var currentContainerId = sourceObject.FindProperty("currentContainerId").stringValue;
        if (string.IsNullOrWhiteSpace(currentContainerId)) currentContainerId = "main_bag";

        var sourceRect = sourceUi.GetComponent<RectTransform>();
        if (sourceRect == null) return;

        var layout = EnsureContainerViewLayout(sourceUi, sourceRect, service, currentContainerId, true);
        BuildEditorItems(layout.itemLayerRoot, service, sourceUi, currentContainerId, false);
        ConfigureMainContainerView(sourceUi, service, currentContainerId, layout);
        sourceUi.gameObject.SetActive(true);
        EditorUtility.SetDirty(sourceUi);
        EditorUtility.SetDirty(sourceRect);
    }

    private static void BuildSideContainerViews(GridInventoryService service, GridInventoryUnityUI sourceUi)
    {
        var sourceObject = new SerializedObject(sourceUi);
        var currentContainerId = sourceObject.FindProperty("currentContainerId").stringValue;
        var sidePanelOffset = sourceObject.FindProperty("sidePanelOffset").vector2Value;
        var containerIds = ReadContainerIds(service);
        var sourceRect = sourceUi.GetComponent<RectTransform>();
        var parent = sourceRect != null && sourceRect.parent != null ? sourceRect.parent : sourceUi.transform.parent;
        if (parent == null) parent = sourceUi.transform;

        var sideIndex = 0;
        foreach (var containerId in containerIds)
        {
            if (string.IsNullOrWhiteSpace(containerId) || containerId == currentContainerId) continue;
            var sideObjectName = $"InventoryUI_{containerId}";
            var existing = FindDirectChild(parent, sideObjectName);
            var isNew = existing == null;
            var sideObject = isNew ? new GameObject(sideObjectName) : existing.gameObject;
            if (isNew)
            {
                Undo.RegisterCreatedObjectUndo(sideObject, $"Create {sideObjectName}");
                sideObject.transform.SetParent(parent, false);
            }

            var sideRect = sideObject.GetComponent<RectTransform>();
            if (sideRect == null) sideRect = sideObject.AddComponent<RectTransform>();
            EnsureSideCanvas(sideObject, sourceUi);
            if (isNew && sourceRect != null)
            {
                sideRect.anchorMin = sourceRect.anchorMin;
                sideRect.anchorMax = sourceRect.anchorMax;
                sideRect.pivot = sourceRect.pivot;
                sideRect.sizeDelta = sourceRect.sizeDelta;
                sideRect.anchoredPosition = sourceRect.anchoredPosition + sidePanelOffset * (sideIndex + 1);
                sideRect.localScale = sourceRect.localScale;
            }

            var sideUi = sideObject.GetComponent<GridInventoryUnityUI>();
            if (sideUi == null) sideUi = sideObject.AddComponent<GridInventoryUnityUI>();
            var layout = EnsureContainerViewLayout(sourceUi, sideRect, service, containerId, false);
            BuildEditorItems(layout.itemLayerRoot, service, sourceUi, containerId, false);
            ConfigureSideContainerView(sourceUi, service, sideUi, containerId, layout);
            sideObject.SetActive(true);

            EditorUtility.SetDirty(sideObject);
            EditorUtility.SetDirty(sideRect);
            EditorUtility.SetDirty(sideUi);
            sideIndex++;
        }
    }

    private static ContainerViewLayout EnsureContainerViewLayout(GridInventoryUnityUI sourceUi, RectTransform root, GridInventoryService service, string containerId, bool keepToolbarsVisible)
    {
        var source = new SerializedObject(sourceUi);
        var cellSize = source.FindProperty("cellSize").floatValue;
        var cellSpacing = source.FindProperty("cellSpacing").floatValue;
        var panelLeftPadding = source.FindProperty("panelLeftPadding").floatValue;
        var toolbarTopOffset = source.FindProperty("toolbarTopOffset").floatValue;
        var toolbarHeightProperty = source.FindProperty("toolbarHeight");
        var toolbarSpacingProperty = source.FindProperty("toolbarSpacing");
        var toolbarHeight = toolbarHeightProperty.floatValue;
        var toolbarSpacing = toolbarSpacingProperty.floatValue;
        if (toolbarHeight < 1f)
        {
            // Legacy scenes can have zeroed toolbar settings. A zero-height row makes
            // every child of a VerticalLayoutGroup occupy the same point.
            toolbarHeight = 34f;
            toolbarHeightProperty.floatValue = toolbarHeight;
            if (toolbarSpacing <= 0f)
            {
                toolbarSpacing = 4f;
                toolbarSpacingProperty.floatValue = toolbarSpacing;
            }
            source.ApplyModifiedPropertiesWithoutUndo();
        }
        var gridTopSpacing = source.FindProperty("gridTopSpacing").floatValue;
        var minToolbarWidth = source.FindProperty("minToolbarWidth").floatValue;
        var panelPadding = source.FindProperty("panelBackgroundPadding").vector2Value;
        var emptyCellColor = source.FindProperty("emptyCellColor").colorValue;
        var panelColor = source.FindProperty("panelBackgroundColor").colorValue;
        var gridColor = source.FindProperty("gridBackgroundColor").colorValue;
        var panelSprite = source.FindProperty("panelBackgroundSprite").objectReferenceValue as Sprite;
        var gridSprite = source.FindProperty("gridBackgroundSprite").objectReferenceValue as Sprite;

        if (!TryReadContainerSize(service, containerId, out var rows, out var columns))
        {
            rows = 4;
            columns = 4;
        }

        var gridSize = new Vector2(columns * cellSize + Mathf.Max(0, columns - 1) * cellSpacing, rows * cellSize + Mathf.Max(0, rows - 1) * cellSpacing);
        var visibleToolbarRows = keepToolbarsVisible ? 3 : 0;
        var toolHeight = visibleToolbarRows > 0 ? toolbarHeight * visibleToolbarRows + toolbarSpacing * (visibleToolbarRows - 1) : 0f;
        var toolbarWidth = Mathf.Max(gridSize.x, minToolbarWidth);
        var contentWidth = Mathf.Max(gridSize.x, toolbarWidth);
        var gridX = panelLeftPadding + Mathf.Max(0f, contentWidth - gridSize.x) * 0.5f;
        var toolX = panelLeftPadding + Mathf.Max(0f, contentWidth - toolbarWidth) * 0.5f;

        var panelBackground = root.GetComponent<Image>();
        if (panelBackground == null) panelBackground = root.gameObject.AddComponent<Image>();
        panelBackground.sprite = panelSprite;
        panelBackground.type = panelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        panelBackground.color = panelColor;

            var nestedTitle = FindDirectChild(FindDirectChild(root, "toolRoot"), "TitleText");
            var titleAtRoot = FindDirectChild(root, "TitleText");
            if (titleAtRoot == null && nestedTitle != null)
            {
                nestedTitle.SetParent(root, false);
                titleAtRoot = nestedTitle;
            }

            // Older manually-edited scenes may contain a legacy TitleText inside toolRoot.
            // Preserve the normal root-level title and remove only duplicate title objects.
            for (var childIndex = root.childCount - 1; childIndex >= 0; childIndex--)
            {
                var child = root.GetChild(childIndex);
                if (child != titleAtRoot && child.name == "TitleText")
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }
        var titleText = EnsureTextChild(root, "TitleText", $"背包  [{containerId}]  未选择", 18, TextAnchor.MiddleLeft);
        var titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(12f, -8f);
        titleRect.sizeDelta = new Vector2(-24f, 24f);

        var toolRoot = EnsureRectChild(root, "toolRoot", new Vector2(toolbarWidth, Mathf.Max(toolbarHeight, toolHeight)), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(toolX, -toolbarTopOffset));
        var toolbarRoot = EnsureRectChild(toolRoot, "ContainerToolbar", new Vector2(toolbarWidth, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
        var spawnRoot = EnsureRectChild(toolRoot, "SpawnToolbar", new Vector2(toolbarWidth, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), keepToolbarsVisible ? new Vector2(0f, -(toolbarHeight + toolbarSpacing)) : Vector2.zero);
        var actionRoot = EnsureRectChild(toolRoot, "ActionToolbar", new Vector2(toolbarWidth, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), keepToolbarsVisible ? new Vector2(0f, -(toolbarHeight + toolbarSpacing) * 2f) : Vector2.zero);
        toolbarRoot.SetSiblingIndex(0);
        spawnRoot.SetSiblingIndex(1);
        actionRoot.SetSiblingIndex(2);

        ConfigureToolRootLayoutGroup(toolRoot, toolbarRoot, spawnRoot, actionRoot, toolbarHeight, toolbarSpacing, keepToolbarsVisible);

        toolbarRoot.gameObject.SetActive(keepToolbarsVisible);
        spawnRoot.gameObject.SetActive(keepToolbarsVisible);
        actionRoot.gameObject.SetActive(keepToolbarsVisible);

        var gridTop = toolbarTopOffset + toolHeight + (toolHeight > 0f ? gridTopSpacing : 0f);
        var gridPosition = new Vector2(gridX, -gridTop);
        var gridRoot = EnsureRectChild(root, "GridRoot", gridSize, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), gridPosition);
        var gridBackground = gridRoot.GetComponent<Image>();
        if (gridBackground == null) gridBackground = gridRoot.gameObject.AddComponent<Image>();
        gridBackground.sprite = gridSprite;
        gridBackground.type = gridSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        gridBackground.color = gridColor;

        var cellLayerRoot = EnsureRectChild(root, "CellLayer", gridSize, gridRoot.anchorMin, gridRoot.anchorMax, gridRoot.pivot, gridPosition);
        var itemLayerRoot = EnsureRectChild(root, "ItemLayer", gridSize, gridRoot.anchorMin, gridRoot.anchorMax, gridRoot.pivot, gridPosition);
        var ghostRoot = EnsureRectChild(root, "GhostLayer", gridSize, gridRoot.anchorMin, gridRoot.anchorMax, gridRoot.pivot, gridPosition);
        BuildEditorCells(cellLayerRoot, rows, columns, cellSize, cellSpacing, emptyCellColor);

        gridRoot.SetAsFirstSibling();
        cellLayerRoot.SetAsLastSibling();
        itemLayerRoot.SetAsLastSibling();
        ghostRoot.SetAsLastSibling();
        toolRoot.SetAsLastSibling();

        var panelWidth = panelLeftPadding + contentWidth + panelPadding.x;
        var panelHeight = gridTop + gridSize.y + panelPadding.y;
        root.sizeDelta = new Vector2(panelWidth, panelHeight);

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(panelBackground);
        EditorUtility.SetDirty(gridBackground);

        return new ContainerViewLayout
        {
            titleText = titleText,
            toolRoot = toolRoot,
            toolbarRoot = toolbarRoot,
            spawnRoot = spawnRoot,
            actionRoot = actionRoot,
            gridRoot = gridRoot,
            cellLayerRoot = cellLayerRoot,
            itemLayerRoot = itemLayerRoot,
            ghostRoot = ghostRoot,
            panelBackground = panelBackground,
            gridBackground = gridBackground
        };
    }

    private static void ConfigureMainContainerView(GridInventoryUnityUI ui, GridInventoryService service, string containerId, ContainerViewLayout layout)
    {
        var target = new SerializedObject(ui);
        target.FindProperty("gridInventoryService").objectReferenceValue = service;
        target.FindProperty("currentContainerId").stringValue = containerId;
        target.FindProperty("buildRuntimeLayout").boolValue = false;
        target.FindProperty("strictManualMode").boolValue = true;
        target.FindProperty("autoAlignToolbarWithGrid").boolValue = false;
        target.FindProperty("autoRepositionToolRoot").boolValue = false;
        target.FindProperty("preferExistingButtons").boolValue = true;
        AssignManualLayout(target, layout);
        target.ApplyModifiedProperties();
    }

    private static void ConfigureSideContainerView(GridInventoryUnityUI sourceUi, GridInventoryService service, GridInventoryUnityUI sideUi, string containerId, ContainerViewLayout layout)
    {
        var source = new SerializedObject(sourceUi);
        var target = new SerializedObject(sideUi);
        target.FindProperty("gridInventoryService").objectReferenceValue = service;
        target.FindProperty("startOpen").boolValue = false;
        target.FindProperty("showSpawnTools").boolValue = false;
        target.FindProperty("allowSpawnFromEmptyCells").boolValue = false;
        target.FindProperty("openOtherContainersAsSidePanels").boolValue = false;
        target.FindProperty("allowPanelDragging").boolValue = source.FindProperty("allowPanelDragging").boolValue;
        target.FindProperty("useCompactLayout").boolValue = source.FindProperty("useCompactLayout").boolValue;
        target.FindProperty("scaleWithScreenSize").boolValue = source.FindProperty("scaleWithScreenSize").boolValue;
        target.FindProperty("referenceResolution").vector2Value = source.FindProperty("referenceResolution").vector2Value;
        target.FindProperty("screenMatchMode").floatValue = source.FindProperty("screenMatchMode").floatValue;
        target.FindProperty("buildRuntimeLayout").boolValue = false;
        target.FindProperty("strictManualMode").boolValue = true;
        target.FindProperty("autoAlignToolbarWithGrid").boolValue = true;
        target.FindProperty("autoResizePanelBackground").boolValue = source.FindProperty("autoResizePanelBackground").boolValue;
        target.FindProperty("minToolbarWidth").floatValue = Mathf.Min(source.FindProperty("minToolbarWidth").floatValue, 180f);
        target.FindProperty("panelBackgroundPadding").vector2Value = source.FindProperty("panelBackgroundPadding").vector2Value;
        target.FindProperty("panelBackgroundSprite").objectReferenceValue = source.FindProperty("panelBackgroundSprite").objectReferenceValue;
        target.FindProperty("gridBackgroundSprite").objectReferenceValue = source.FindProperty("gridBackgroundSprite").objectReferenceValue;
        target.FindProperty("panelBackgroundColor").colorValue = source.FindProperty("panelBackgroundColor").colorValue;
        target.FindProperty("gridBackgroundColor").colorValue = source.FindProperty("gridBackgroundColor").colorValue;
        target.FindProperty("currentContainerId").stringValue = containerId;
        target.FindProperty("toggleKey").enumValueIndex = source.FindProperty("toggleKey").enumValueIndex;
        target.FindProperty("cellSize").floatValue = source.FindProperty("cellSize").floatValue;
        target.FindProperty("cellSpacing").floatValue = source.FindProperty("cellSpacing").floatValue;
        target.FindProperty("panelAnchoredPosition").vector2Value = source.FindProperty("panelAnchoredPosition").vector2Value;
        target.FindProperty("panelLeftPadding").floatValue = source.FindProperty("panelLeftPadding").floatValue;
        target.FindProperty("toolbarTopOffset").floatValue = source.FindProperty("toolbarTopOffset").floatValue;
        target.FindProperty("toolbarHeight").floatValue = source.FindProperty("toolbarHeight").floatValue;
        target.FindProperty("toolbarSpacing").floatValue = source.FindProperty("toolbarSpacing").floatValue;
        target.FindProperty("gridTopSpacing").floatValue = source.FindProperty("gridTopSpacing").floatValue;
        CopyProperty(source.FindProperty("emptyCellColor"), target.FindProperty("emptyCellColor"));
        CopyProperty(source.FindProperty("occupiedCellColor"), target.FindProperty("occupiedCellColor"));
        CopyProperty(source.FindProperty("selectedItemColor"), target.FindProperty("selectedItemColor"));
        CopyProperty(source.FindProperty("previewValidColor"), target.FindProperty("previewValidColor"));
        CopyProperty(source.FindProperty("mergeTargetColor"), target.FindProperty("mergeTargetColor"));
        CopyProperty(source.FindProperty("previewInvalidColor"), target.FindProperty("previewInvalidColor"));
        CopyArray(source.FindProperty("itemVisuals"), target.FindProperty("itemVisuals"));
        target.FindProperty("showContainerToolbar").boolValue = false;
        target.FindProperty("showActionToolbar").boolValue = false;
        target.FindProperty("autoRepositionToolRoot").boolValue = false;
        target.FindProperty("preferExistingButtons").boolValue = false;
        AssignManualLayout(target, layout);
        target.ApplyModifiedProperties();
    }

    private static void AssignManualLayout(SerializedObject target, ContainerViewLayout layout)
    {
        target.FindProperty("titleTextOverride").objectReferenceValue = layout.titleText;
        target.FindProperty("toolRootOverride").objectReferenceValue = layout.toolRoot;
        target.FindProperty("toolbarRootOverride").objectReferenceValue = layout.toolbarRoot;
        target.FindProperty("spawnRootOverride").objectReferenceValue = layout.spawnRoot;
        target.FindProperty("actionRootOverride").objectReferenceValue = layout.actionRoot;
        target.FindProperty("gridRootOverride").objectReferenceValue = layout.gridRoot;
        target.FindProperty("cellLayerRootOverride").objectReferenceValue = layout.cellLayerRoot;
        target.FindProperty("itemLayerRootOverride").objectReferenceValue = layout.itemLayerRoot;
        target.FindProperty("ghostRootOverride").objectReferenceValue = layout.ghostRoot;
        target.FindProperty("panelBackgroundOverride").objectReferenceValue = layout.panelBackground;
        target.FindProperty("gridBackgroundOverride").objectReferenceValue = layout.gridBackground;
    }

    private static void BuildEditorCells(RectTransform cellLayerRoot, int rows, int columns, float cellSize, float cellSpacing, Color emptyCellColor)
    {
        for (var i = cellLayerRoot.childCount - 1; i >= 0; i--)
        {
            var child = cellLayerRoot.GetChild(i);
            if (child.name.StartsWith("Cell_", System.StringComparison.Ordinal)) Undo.DestroyObjectImmediate(child.gameObject);
        }

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var cell = EnsureRectChild(cellLayerRoot, $"Cell_{row}_{column}", new Vector2(cellSize, cellSize), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(column * (cellSize + cellSpacing), -row * (cellSize + cellSpacing)));
                var image = cell.GetComponent<Image>();
                if (image == null) image = cell.gameObject.AddComponent<Image>();
                image.color = emptyCellColor;
                if (cell.GetComponent<Button>() == null) cell.gameObject.AddComponent<Button>();
                EditorUtility.SetDirty(cell);
                EditorUtility.SetDirty(image);
            }
        }
    }

    private static void ConfigureToolRootLayoutGroup(RectTransform toolRoot, RectTransform toolbarRoot, RectTransform spawnRoot, RectTransform actionRoot, float toolbarHeight, float toolbarSpacing, bool keepToolbarsVisible)
    {
        var layoutGroup = toolRoot.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup == null) layoutGroup = Undo.AddComponent<VerticalLayoutGroup>(toolRoot.gameObject);

        layoutGroup.padding = new RectOffset(0, 0, 0, 0);
        layoutGroup.spacing = toolbarSpacing;
        layoutGroup.childAlignment = TextAnchor.UpperLeft;
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true;
        layoutGroup.childForceExpandWidth = false;
        layoutGroup.childForceExpandHeight = false;

        ConfigureToolbarLayoutElement(toolbarRoot, toolbarHeight);
        ConfigureToolbarLayoutElement(spawnRoot, toolbarHeight);
        ConfigureToolbarLayoutElement(actionRoot, toolbarHeight);
        ConfigureToolbarRowLayout(toolbarRoot);
        ConfigureToolbarRowLayout(spawnRoot);
        ConfigureToolbarRowLayout(actionRoot);

        var rowCount = keepToolbarsVisible ? 3 : 0;
        var totalHeight = rowCount > 0 ? rowCount * toolbarHeight + Mathf.Max(0, rowCount - 1) * toolbarSpacing : toolbarHeight;
        toolRoot.sizeDelta = new Vector2(toolRoot.sizeDelta.x, totalHeight);
        LayoutRebuilder.ForceRebuildLayoutImmediate(toolRoot);
    }

    private static void ConfigureToolbarLayoutElement(RectTransform toolbarRoot, float toolbarHeight)
    {
        var element = toolbarRoot.GetComponent<LayoutElement>();
        if (element == null) element = toolbarRoot.gameObject.AddComponent<LayoutElement>();
        element.minHeight = toolbarHeight;
        element.preferredHeight = toolbarHeight;
        element.flexibleHeight = 0f;
        element.minWidth = 0f;
        element.flexibleWidth = 1f;
    }

    private static void ConfigureToolbarRowLayout(RectTransform toolbarRoot)
    {
        var layout = toolbarRoot.GetComponent<HorizontalLayoutGroup>();
        if (layout == null) layout = Undo.AddComponent<HorizontalLayoutGroup>(toolbarRoot.gameObject);
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    private static void BuildEditorItems(RectTransform itemLayerRoot, GridInventoryService service, GridInventoryUnityUI sourceUi, string containerId, bool createMissingItems)
    {
        if (itemLayerRoot == null || service == null || sourceUi == null) return;
        var source = new SerializedObject(sourceUi);
        var serviceObject = new SerializedObject(service);
        var definitions = serviceObject.FindProperty("itemDefinitions");
        var cellSize = source.FindProperty("cellSize").floatValue;
        var cellSpacing = source.FindProperty("cellSpacing").floatValue;
        var editorItems = itemLayerRoot.GetComponentsInChildren<GridInventoryEditorItem>(true).ToList();

        if (createMissingItems)
        {
            for (var i = 0; i < definitions.arraySize; i++)
            {
                var definition = definitions.GetArrayElementAtIndex(i);
                var itemId = definition.FindPropertyRelative("itemId").stringValue;
                if (string.IsNullOrWhiteSpace(itemId) || editorItems.Any(x => x != null && x.ItemId == itemId)) continue;
                if (!TryReadContainerSize(service, containerId, out var rows, out var columns)) break;

                var width = Mathf.Max(1, definition.FindPropertyRelative("width").intValue);
                var height = Mathf.Max(1, definition.FindPropertyRelative("height").intValue);
                if (!TryFindOpenCell(editorItems, serviceObject, containerId, rows, columns, width, height, out var row, out var column)) continue;

                var itemRoot = EnsureRectChild(itemLayerRoot, $"Item_{itemId}", Vector2.one, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
                var editorItem = itemRoot.GetComponent<GridInventoryEditorItem>();
                if (editorItem == null) editorItem = itemRoot.gameObject.AddComponent<GridInventoryEditorItem>();
                editorItem.Configure(containerId, itemId, 1, row, column, GridItemRotation.Deg0);
                editorItems.Add(editorItem);
            }
        }

        for (var i = 0; i < editorItems.Count; i++)
        {
            var editorItem = editorItems[i];
            if (editorItem == null || editorItem.ContainerId != containerId) continue;
            if (!TryFindItemDefinition(serviceObject, editorItem.ItemId, out var definition)) continue;
            UpdateEditorItemNode(editorItem, definition, source, cellSize, cellSpacing);
        }
    }

    private static void CreateDefaultEditorItem(RectTransform itemLayerRoot, string itemId, int stackCount, int row, int column, GridItemRotation rotation)
    {
        var itemRoot = EnsureRectChild(itemLayerRoot, $"Item_{itemId}", Vector2.one, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
        var editorItem = itemRoot.GetComponent<GridInventoryEditorItem>();
        if (editorItem == null) editorItem = itemRoot.gameObject.AddComponent<GridInventoryEditorItem>();
        editorItem.Configure("main_bag", itemId, stackCount, row, column, rotation);
    }

    private static void UpdateEditorItemNode(GridInventoryEditorItem editorItem, SerializedProperty definition, SerializedObject sourceUi, float cellSize, float cellSpacing)
    {
        var width = Mathf.Max(1, definition.FindPropertyRelative("width").intValue);
        var height = Mathf.Max(1, definition.FindPropertyRelative("height").intValue);
        if (editorItem.Rotation == GridItemRotation.Deg90)
        {
            var temp = width;
            width = height;
            height = temp;
        }

        var rect = editorItem.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(editorItem.Column * (cellSize + cellSpacing), -editorItem.Row * (cellSize + cellSpacing));
        rect.sizeDelta = new Vector2(width * cellSize + Mathf.Max(0, width - 1) * cellSpacing, height * cellSize + Mathf.Max(0, height - 1) * cellSpacing);

        var image = editorItem.GetComponent<Image>();
        if (image == null) image = editorItem.gameObject.AddComponent<Image>();
        if (editorItem.GetComponent<Button>() == null) editorItem.gameObject.AddComponent<Button>();
        var icon = FindVisualIcon(sourceUi, editorItem.ItemId, out var tint);
        image.sprite = icon;
        image.type = Image.Type.Simple;
        image.color = tint;

        var label = editorItem.GetComponentInChildren<Text>(true);
        if (label == null) label = EnsureTextChild(rect, "Label", string.Empty, 20, TextAnchor.LowerRight);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(4f, 2f);
        label.rectTransform.offsetMax = new Vector2(-4f, -2f);
        var maxStack = Mathf.Max(1, definition.FindPropertyRelative("maxStack").intValue);
        label.text = maxStack > 1 ? $"{editorItem.StackCount}/{maxStack}" : string.Empty;
        label.color = Color.white;

        EditorUtility.SetDirty(editorItem);
        EditorUtility.SetDirty(rect);
        EditorUtility.SetDirty(image);
        EditorUtility.SetDirty(label);
    }

    private static bool TryReadContainerSize(GridInventoryService service, string containerId, out int rows, out int columns)
    {
        rows = 0;
        columns = 0;
        var serializedObject = new SerializedObject(service);
        var containers = serializedObject.FindProperty("containerDefinitions");
        for (var i = 0; i < containers.arraySize; i++)
        {
            var element = containers.GetArrayElementAtIndex(i);
            if (element.FindPropertyRelative("containerId").stringValue != containerId) continue;
            rows = Mathf.Max(1, element.FindPropertyRelative("rows").intValue);
            columns = Mathf.Max(1, element.FindPropertyRelative("columns").intValue);
            return true;
        }

        return false;
    }

    private static bool TryFindItemDefinition(SerializedObject serviceObject, string itemId, out SerializedProperty definition)
    {
        var definitions = serviceObject.FindProperty("itemDefinitions");
        for (var i = 0; i < definitions.arraySize; i++)
        {
            var candidate = definitions.GetArrayElementAtIndex(i);
            if (candidate.FindPropertyRelative("itemId").stringValue != itemId) continue;
            definition = candidate;
            return true;
        }

        definition = null;
        return false;
    }

    private static bool TryFindOpenCell(List<GridInventoryEditorItem> editorItems, SerializedObject serviceObject, string containerId, int rows, int columns, int width, int height, out int targetRow, out int targetColumn)
    {
        var occupied = new bool[rows, columns];
        for (var i = 0; i < editorItems.Count; i++)
        {
            var item = editorItems[i];
            if (item == null || item.ContainerId != containerId) continue;
            if (!TryFindItemDefinition(serviceObject, item.ItemId, out var definition)) continue;
            var itemWidth = Mathf.Max(1, definition.FindPropertyRelative("width").intValue);
            var itemHeight = Mathf.Max(1, definition.FindPropertyRelative("height").intValue);
            if (item.Rotation == GridItemRotation.Deg90)
            {
                var temp = itemWidth;
                itemWidth = itemHeight;
                itemHeight = temp;
            }
            for (var row = item.Row; row < Mathf.Min(rows, item.Row + itemHeight); row++)
            for (var column = item.Column; column < Mathf.Min(columns, item.Column + itemWidth); column++)
                occupied[row, column] = true;
        }

        for (var row = 0; row <= rows - height; row++)
        for (var column = 0; column <= columns - width; column++)
        {
            var fits = true;
            for (var testRow = row; testRow < row + height && fits; testRow++)
            for (var testColumn = column; testColumn < column + width; testColumn++)
                if (occupied[testRow, testColumn]) fits = false;
            if (!fits) continue;
            targetRow = row;
            targetColumn = column;
            return true;
        }

        targetRow = 0;
        targetColumn = 0;
        return false;
    }

    private static Sprite FindVisualIcon(SerializedObject sourceUi, string itemId, out Color tint)
    {
        tint = new Color(0.31f, 0.48f, 0.72f, 0.95f);
        var visuals = sourceUi.FindProperty("itemVisuals");
        for (var i = 0; i < visuals.arraySize; i++)
        {
            var visual = visuals.GetArrayElementAtIndex(i);
            if (visual.FindPropertyRelative("itemId").stringValue != itemId) continue;
            tint = visual.FindPropertyRelative("tint").colorValue;
            if (tint.a <= 0f) tint = Color.white;
            return visual.FindPropertyRelative("icon").objectReferenceValue as Sprite;
        }

        return null;
    }

    private static RectTransform EnsureRectChild(Transform parent, string objectName, Vector2 size, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition)
    {
        var existing = parent.Find(objectName);
        RectTransform rect;
        if (existing == null)
        {
            var child = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(child, $"Create {objectName}");
            child.transform.SetParent(parent, false);
            rect = child.AddComponent<RectTransform>();
        }
        else
        {
            rect = existing as RectTransform;
            if (rect == null) rect = existing.gameObject.AddComponent<RectTransform>();
            existing.gameObject.SetActive(true);
        }

        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        rect.localScale = Vector3.one;
        EditorUtility.SetDirty(rect);
        return rect;
    }

    private static Text EnsureTextChild(Transform parent, string objectName, string text, int fontSize, TextAnchor alignment)
    {
        var rect = EnsureRectChild(parent, objectName, new Vector2(200f, 24f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
        var label = rect.GetComponent<Text>();
        if (label == null) label = rect.gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        EditorUtility.SetDirty(label);
        return label;
    }

    private static void EnsureSideCanvas(GameObject sideObject, GridInventoryUnityUI sourceUi)
    {
        var canvas = sideObject.GetComponent<Canvas>();
        if (canvas == null) canvas = sideObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        if (sideObject.GetComponent<GraphicRaycaster>() == null) sideObject.AddComponent<GraphicRaycaster>();

        var sourceCanvas = sourceUi.GetComponentInParent<Canvas>();
        if (sourceCanvas != null)
        {
            canvas.pixelPerfect = sourceCanvas.pixelPerfect;
            canvas.sortingOrder = sourceCanvas.sortingOrder + 1;
        }

        var scaler = sideObject.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = sideObject.AddComponent<CanvasScaler>();
        var sourceScaler = sourceUi.GetComponentInParent<CanvasScaler>();
        if (sourceScaler == null) return;
        scaler.uiScaleMode = sourceScaler.uiScaleMode;
        scaler.referenceResolution = sourceScaler.referenceResolution;
        scaler.screenMatchMode = sourceScaler.screenMatchMode;
        scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
        scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
        scaler.scaleFactor = sourceScaler.scaleFactor;
    }

    private static List<string> ReadContainerIds(GridInventoryService service)
    {
        var ids = new List<string>();
        var serializedObject = new SerializedObject(service);
        var containers = serializedObject.FindProperty("containerDefinitions");
        for (var i = 0; i < containers.arraySize; i++)
        {
            var containerId = containers.GetArrayElementAtIndex(i).FindPropertyRelative("containerId").stringValue;
            if (!string.IsNullOrWhiteSpace(containerId) && !ids.Contains(containerId)) ids.Add(containerId);
        }

        return ids;
    }

    private static Transform FindDirectChild(Transform parent, string name)
    {
        if (parent == null) return null;
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == name) return child;
        }

        return null;
    }

    private static void CopyProperty(SerializedProperty source, SerializedProperty target)
    {
        if (source == null || target == null) return;
        switch (source.propertyType)
        {
            case SerializedPropertyType.Color:
                target.colorValue = source.colorValue;
                break;
            case SerializedPropertyType.Vector2:
                target.vector2Value = source.vector2Value;
                break;
            case SerializedPropertyType.ObjectReference:
                target.objectReferenceValue = source.objectReferenceValue;
                break;
        }
    }

    private static void CopyArray(SerializedProperty source, SerializedProperty target)
    {
        if (source == null || target == null || !source.isArray || !target.isArray) return;
        target.ClearArray();
        for (var i = 0; i < source.arraySize; i++)
        {
            target.InsertArrayElementAtIndex(i);
            var sourceElement = source.GetArrayElementAtIndex(i);
            var targetElement = target.GetArrayElementAtIndex(i);
            targetElement.FindPropertyRelative("itemId").stringValue = sourceElement.FindPropertyRelative("itemId").stringValue;
            targetElement.FindPropertyRelative("icon").objectReferenceValue = sourceElement.FindPropertyRelative("icon").objectReferenceValue;
            targetElement.FindPropertyRelative("tint").colorValue = sourceElement.FindPropertyRelative("tint").colorValue;
        }
    }
}
