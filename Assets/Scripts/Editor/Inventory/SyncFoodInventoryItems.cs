using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SyncFoodInventoryItems
{
    private const string FoodFolder = "Assets/Resources/Characters/food";

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

    [MenuItem("Tools/Inventory/Sync Food Items From Characters Food")]
    public static void Sync()
    {
        var service = Object.FindObjectOfType<GridInventoryService>(true);
        var ui = FindMainInventoryUi();

        if (service == null)
        {
            Debug.LogError("SyncFoodInventoryItems: GridInventoryService not found in the current scene.");
            return;
        }

        if (ui == null)
        {
            Debug.LogError("SyncFoodInventoryItems: GridInventoryUnityUI not found in the current scene.");
            return;
        }

        var foodAssets = AssetDatabase.FindAssets("t:Texture2D", new[] { FoodFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => string.Equals(Path.GetExtension(path), ".png", System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileNameWithoutExtension(path))
            .Select(path => new FoodItemAsset(path, ToItemId(Path.GetFileNameWithoutExtension(path))))
            .Where(asset => !string.IsNullOrWhiteSpace(asset.ItemId))
            .ToList();

        foreach (var asset in foodAssets)
        {
            EnsureSpriteImport(asset.Path);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        WriteItemDefinitions(service, foodAssets);
        WriteItemVisuals(ui, foodAssets);
        BuildSpawnSelector(ui);
        BuildSideContainerViews(service, ui);

        EditorUtility.SetDirty(service);
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(service.gameObject.scene);
        EditorSceneManager.SaveScene(service.gameObject.scene);

        Debug.Log($"SyncFoodInventoryItems: synced {foodAssets.Count} food items from {FoodFolder} and rebuilt the editor-visible spawn dropdown.");
    }

    [MenuItem("Tools/Inventory/Build Side Container Views")]
    public static void BuildSideContainerViewsMenu()
    {
        var service = Object.FindObjectOfType<GridInventoryService>(true);
        var ui = FindMainInventoryUi();

        if (service == null)
        {
            Debug.LogError("SyncFoodInventoryItems: GridInventoryService not found in the current scene.");
            return;
        }

        if (ui == null)
        {
            Debug.LogError("SyncFoodInventoryItems: GridInventoryUnityUI not found in the current scene.");
            return;
        }

        BuildSideContainerViews(service, ui);
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        EditorSceneManager.SaveScene(ui.gameObject.scene);
        Debug.Log("SyncFoodInventoryItems: rebuilt editor-visible side container views.");
    }

    private static void EnsureSpriteImport(string assetPath)
    {
        if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;
        var changed = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            changed = true;
        }

        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            changed = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            changed = true;
        }

        if (changed) importer.SaveAndReimport();
    }

    private static GridInventoryUnityUI FindMainInventoryUi()
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

    private static void WriteItemDefinitions(GridInventoryService service, IReadOnlyList<FoodItemAsset> foodAssets)
    {
        var serializedObject = new SerializedObject(service);
        var definitions = serializedObject.FindProperty("itemDefinitions");
        definitions.ClearArray();

        AddDefinition(definitions, "food_canned", 1, 1, false, 3, 0.5f, "consumable");
        AddDefinition(definitions, "water_bottle", 1, 2, true, 2, 0.8f, "consumable");
        AddDefinition(definitions, "bandage", 1, 1, false, 5, 0.2f, "medical");
        AddDefinition(definitions, "cloth_raw", 2, 1, true, 4, 0.4f, "material");

        var existing = new HashSet<string> { "food_canned", "water_bottle", "bandage", "cloth_raw" };
        foreach (var asset in foodAssets)
        {
            if (!existing.Add(asset.ItemId)) continue;
            AddDefinition(definitions, asset.ItemId, 1, 1, false, 10, 0.1f, "food");
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void AddDefinition(SerializedProperty definitions, string itemId, int width, int height, bool canRotate, int maxStack, float weight, string tag)
    {
        definitions.InsertArrayElementAtIndex(definitions.arraySize);
        var definition = definitions.GetArrayElementAtIndex(definitions.arraySize - 1);
        definition.FindPropertyRelative("itemId").stringValue = itemId;
        definition.FindPropertyRelative("width").intValue = width;
        definition.FindPropertyRelative("height").intValue = height;
        definition.FindPropertyRelative("canRotate").boolValue = canRotate;
        definition.FindPropertyRelative("maxStack").intValue = maxStack;
        definition.FindPropertyRelative("weight").floatValue = weight;

        var tags = definition.FindPropertyRelative("itemTags");
        tags.ClearArray();
        tags.InsertArrayElementAtIndex(0);
        tags.GetArrayElementAtIndex(0).stringValue = tag;
    }

    private static void WriteItemVisuals(GridInventoryUnityUI ui, IReadOnlyList<FoodItemAsset> foodAssets)
    {
        var serializedObject = new SerializedObject(ui);
        var spawnItemIds = serializedObject.FindProperty("spawnItemIds");
        var visuals = serializedObject.FindProperty("itemVisuals");
        var currentSpawnIds = ReadStringArray(spawnItemIds);
        var visualIndexByItemId = ReadVisualIndexByItemId(visuals);

        foreach (var asset in foodAssets)
        {
            if (currentSpawnIds.Add(asset.ItemId))
            {
                spawnItemIds.InsertArrayElementAtIndex(spawnItemIds.arraySize);
                spawnItemIds.GetArrayElementAtIndex(spawnItemIds.arraySize - 1).stringValue = asset.ItemId;
            }

            if (!visualIndexByItemId.TryGetValue(asset.ItemId, out var visualIndex))
            {
                visuals.InsertArrayElementAtIndex(visuals.arraySize);
                visualIndex = visuals.arraySize - 1;
                visualIndexByItemId[asset.ItemId] = visualIndex;
            }

            var visual = visuals.GetArrayElementAtIndex(visualIndex);
            visual.FindPropertyRelative("itemId").stringValue = asset.ItemId;
            visual.FindPropertyRelative("icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(asset.Path);
            visual.FindPropertyRelative("tint").colorValue = Color.white;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void BuildSpawnSelector(GridInventoryUnityUI ui)
    {
        var serializedObject = new SerializedObject(ui);
        serializedObject.FindProperty("showSpawnTools").boolValue = true;
        serializedObject.FindProperty("allowSpawnFromEmptyCells").boolValue = true;
        var spawnRoot = serializedObject.FindProperty("spawnRootOverride").objectReferenceValue as RectTransform;
        var spawnItemIds = ReadStringArray(serializedObject.FindProperty("spawnItemIds")).ToList();
        serializedObject.ApplyModifiedProperties();

        if (spawnRoot == null)
        {
            spawnRoot = FindDeepChild(ui.transform, "SpawnRoot") as RectTransform;
            if (spawnRoot == null) spawnRoot = FindDeepChild(ui.transform, "SpawnToolbar") as RectTransform;
        }

        if (spawnRoot == null)
        {
            Debug.LogWarning("SyncFoodInventoryItems: SpawnRoot not found, so the editor-visible spawn dropdown was not rebuilt.");
            return;
        }

        ClearChildren(spawnRoot);
        spawnRoot.gameObject.SetActive(true);

        var dropdown = CreateDropdown(spawnRoot, spawnItemIds);
        var dragEntry = CreateSpawnDragEntry(spawnRoot, spawnItemIds.Count > 0 ? spawnItemIds[0] : string.Empty);
        var handler = dragEntry.AddComponent<GridInventoryUnityUISpawnItem>();
        handler.Bind(ui, spawnItemIds.Count > 0 ? spawnItemIds[0] : string.Empty);
        var selector = AddAndBindSpawnSelector(dropdown.gameObject, ui, handler, spawnItemIds);
        ApplySpawnDragEntryVisual(ui, dragEntry, handler, spawnItemIds.Count > 0 ? spawnItemIds[0] : string.Empty);

        dropdown.onValueChanged.RemoveAllListeners();
        dropdown.onValueChanged.AddListener(index =>
        {
            if (index < 0 || index >= spawnItemIds.Count) return;
            ApplySpawnDragEntryVisual(ui, dragEntry, handler, spawnItemIds[index]);
        });

        EditorUtility.SetDirty(spawnRoot);
        EditorUtility.SetDirty(dropdown);
        EditorUtility.SetDirty(dragEntry);
        if (selector != null) EditorUtility.SetDirty(selector);
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
            var layout = EnsureContainerViewLayout(sourceUi, sideRect, service, containerId);
            ConfigureEditorSideContainerView(sourceUi, service, sideUi, containerId, layout);
            sideObject.SetActive(true);

            EditorUtility.SetDirty(sideObject);
            EditorUtility.SetDirty(sideRect);
            EditorUtility.SetDirty(sideUi);
            sideIndex++;
        }
    }

    private static void EnsureSideCanvas(GameObject sideObject, GridInventoryUnityUI sourceUi)
    {
        var canvas = sideObject.GetComponent<Canvas>();
        if (canvas == null) canvas = sideObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        if (sideObject.GetComponent<GraphicRaycaster>() == null)
        {
            sideObject.AddComponent<GraphicRaycaster>();
        }

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

    private static ContainerViewLayout EnsureContainerViewLayout(GridInventoryUnityUI sourceUi, RectTransform root, GridInventoryService service, string containerId)
    {
        var source = new SerializedObject(sourceUi);
        var cellSize = source.FindProperty("cellSize").floatValue;
        var cellSpacing = source.FindProperty("cellSpacing").floatValue;
        var panelLeftPadding = source.FindProperty("panelLeftPadding").floatValue;
        var toolbarTopOffset = source.FindProperty("toolbarTopOffset").floatValue;
        var toolbarHeight = source.FindProperty("toolbarHeight").floatValue;
        var toolbarSpacing = source.FindProperty("toolbarSpacing").floatValue;
        var gridTopSpacing = source.FindProperty("gridTopSpacing").floatValue;
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
        var toolHeight = toolbarHeight * 3f + toolbarSpacing * 2f;
        var toolbarWidth = Mathf.Max(gridSize.x, source.FindProperty("minToolbarWidth").floatValue);

        var panelBackground = root.GetComponent<Image>();
        if (panelBackground == null) panelBackground = root.gameObject.AddComponent<Image>();
        panelBackground.sprite = panelSprite;
        panelBackground.type = panelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        panelBackground.color = panelColor;

        var titleText = EnsureTextChild(root, "TitleText", $"背包  [{containerId}]  未选择", 18, TextAnchor.MiddleLeft);
        var titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(12f, -8f);
        titleRect.sizeDelta = new Vector2(-24f, 24f);

        var toolRoot = EnsureRectChild(root, "toolRoot", new Vector2(toolbarWidth, toolHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(panelLeftPadding, -toolbarTopOffset));
        var toolbarRoot = EnsureRectChild(toolRoot, "ContainerToolbar", new Vector2(toolbarWidth, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
        var spawnRoot = EnsureRectChild(toolRoot, "SpawnToolbar", new Vector2(toolbarWidth, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -(toolbarHeight + toolbarSpacing)));
        var actionRoot = EnsureRectChild(toolRoot, "ActionToolbar", new Vector2(toolbarWidth, toolbarHeight), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -(toolbarHeight + toolbarSpacing) * 2f));

        var gridPosition = new Vector2(panelLeftPadding, -(toolbarTopOffset + toolHeight + gridTopSpacing));
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
        titleText.transform.SetAsLastSibling();

        var panelWidth = panelLeftPadding + Mathf.Max(gridSize.x, toolbarWidth) + source.FindProperty("panelBackgroundPadding").vector2Value.x;
        var panelHeight = toolbarTopOffset + toolHeight + gridTopSpacing + gridSize.y + source.FindProperty("panelBackgroundPadding").vector2Value.y;
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

    private static void ConfigureEditorSideContainerView(GridInventoryUnityUI sourceUi, GridInventoryService service, GridInventoryUnityUI sideUi, string containerId, ContainerViewLayout layout)
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
        target.ApplyModifiedProperties();
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

    private static Component AddAndBindSpawnSelector(GameObject dropdownObject, GridInventoryUnityUI ui, GridInventoryUnityUISpawnItem handler, IReadOnlyList<string> spawnItemIds)
    {
        var selectorType = System.Type.GetType("GridInventoryUnityUISpawnSelector, Assembly-CSharp");
        if (selectorType == null)
        {
            Debug.LogWarning("SyncFoodInventoryItems: GridInventoryUnityUISpawnSelector type was not available yet. Re-run the sync after Unity finishes compiling.");
            return null;
        }

        var selector = dropdownObject.GetComponent(selectorType);
        if (selector == null) selector = dropdownObject.AddComponent(selectorType);
        selectorType.GetMethod("Bind")?.Invoke(selector, new object[] { ui, handler, spawnItemIds });
        return selector;
    }

    private static Dropdown CreateDropdown(RectTransform parent, IReadOnlyList<string> itemIds)
    {
        var rect = CreateRect("SpawnItemDropdown", parent, new Vector2(420f, 32f), new Vector2(0f, -1f));
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.14f, 0.14f, 0.14f, 0.98f);
        var dropdown = rect.gameObject.AddComponent<Dropdown>();
        dropdown.targetGraphic = image;
        dropdown.captionText = CreateText("Label", rect, itemIds.Count > 0 ? itemIds[0] : string.Empty, 15, TextAnchor.MiddleLeft);
        dropdown.captionText.rectTransform.offsetMin = new Vector2(10f, 0f);
        dropdown.captionText.rectTransform.offsetMax = new Vector2(-28f, 0f);
        var arrow = CreateText("Arrow", rect, "v", 18, TextAnchor.MiddleCenter);
        arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
        arrow.rectTransform.anchorMax = new Vector2(1f, 1f);
        arrow.rectTransform.sizeDelta = new Vector2(26f, 0f);
        arrow.rectTransform.anchoredPosition = new Vector2(-13f, 0f);

        var template = CreateDropdownTemplate(rect);
        dropdown.template = template;
        dropdown.itemText = template.GetComponentInChildren<Toggle>(true).GetComponentInChildren<Text>(true);
        dropdown.ClearOptions();
        dropdown.AddOptions(itemIds.ToList());
        dropdown.value = 0;
        dropdown.RefreshShownValue();
        return dropdown;
    }

    private static RectTransform CreateDropdownTemplate(RectTransform dropdownRect)
    {
        var template = CreateRect("Template", dropdownRect, new Vector2(420f, 240f), new Vector2(0f, -34f));
        template.anchorMin = new Vector2(0f, 0f);
        template.anchorMax = new Vector2(1f, 0f);
        template.pivot = new Vector2(0f, 1f);
        var templateImage = template.gameObject.AddComponent<Image>();
        templateImage.color = new Color(0.08f, 0.08f, 0.08f, 0.98f);
        var scrollRect = template.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        var viewport = CreateStretchRect("Viewport", template);
        var mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        viewport.gameObject.AddComponent<Image>().color = Color.white;

        var content = CreateStretchRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.sizeDelta = new Vector2(0f, 32f);
        scrollRect.viewport = viewport;
        scrollRect.content = content;

        var item = CreateRect("Item", content, new Vector2(420f, 30f), Vector2.zero);
        item.anchorMin = new Vector2(0f, 1f);
        item.anchorMax = new Vector2(1f, 1f);
        item.pivot = new Vector2(0f, 1f);
        var itemBackground = item.gameObject.AddComponent<Image>();
        itemBackground.color = new Color(0.18f, 0.18f, 0.18f, 0.95f);
        var itemToggle = item.gameObject.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBackground;
        var itemLabel = CreateText("Item Label", item, "Option", 15, TextAnchor.MiddleLeft);
        itemLabel.rectTransform.offsetMin = new Vector2(10f, 0f);
        itemLabel.rectTransform.offsetMax = new Vector2(-10f, 0f);

        template.gameObject.SetActive(false);
        return template;
    }

    private static GameObject CreateSpawnDragEntry(RectTransform parent, string itemId)
    {
        var rect = CreateRect("SpawnSelectedItem", parent, new Vector2(260f, 32f), new Vector2(432f, -1f));
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.16f, 0.16f, 0.16f, 0.95f);
        image.type = Image.Type.Simple;
        var label = CreateText("Text", rect, itemId, 15, TextAnchor.MiddleCenter);
        label.color = Color.white;
        rect.gameObject.AddComponent<Button>();
        return rect.gameObject;
    }

    private static void ApplySpawnDragEntryVisual(GridInventoryUnityUI ui, GameObject dragEntry, GridInventoryUnityUISpawnItem handler, string itemId)
    {
        handler.Bind(ui, itemId);
        var label = dragEntry.GetComponentInChildren<Text>(true);
        if (label != null) label.text = itemId;

        var image = dragEntry.GetComponent<Image>();
        if (image == null) return;

        var visualSprite = FindSpriteForItemId(ui, itemId);
        image.sprite = visualSprite;
        image.type = Image.Type.Simple;
        image.color = visualSprite != null ? Color.white : new Color(0.16f, 0.16f, 0.16f, 0.95f);
    }

    private static Sprite FindSpriteForItemId(GridInventoryUnityUI ui, string itemId)
    {
        var serializedObject = new SerializedObject(ui);
        var visuals = serializedObject.FindProperty("itemVisuals");
        for (var i = 0; i < visuals.arraySize; i++)
        {
            var visual = visuals.GetArrayElementAtIndex(i);
            if (visual.FindPropertyRelative("itemId").stringValue != itemId) continue;
            return visual.FindPropertyRelative("icon").objectReferenceValue as Sprite;
        }

        return null;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 anchoredPosition)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.layer = parent.gameObject.layer;
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        return rect;
    }

    private static RectTransform CreateStretchRect(string name, Transform parent)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.layer = parent.gameObject.layer;
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static Text CreateText(string name, Transform parent, string value, int fontSize, TextAnchor alignment)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.layer = parent.gameObject.layer;
        var rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = obj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        if (parent.name == name) return parent;
        for (var i = 0; i < parent.childCount; i++)
        {
            var result = FindDeepChild(parent.GetChild(i), name);
            if (result != null) return result;
        }

        return null;
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

    private static void ClearChildren(Transform root)
    {
        for (var i = root.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }

    private static HashSet<string> ReadStringArray(SerializedProperty property)
    {
        var values = new HashSet<string>();
        for (var i = 0; i < property.arraySize; i++)
        {
            var value = property.GetArrayElementAtIndex(i).stringValue;
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }

        return values;
    }

    private static HashSet<string> ReadVisualItemIds(SerializedProperty property)
    {
        var values = new HashSet<string>();
        for (var i = 0; i < property.arraySize; i++)
        {
            var value = property.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue;
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }

        return values;
    }

    private static Dictionary<string, int> ReadVisualIndexByItemId(SerializedProperty property)
    {
        var values = new Dictionary<string, int>();
        for (var i = 0; i < property.arraySize; i++)
        {
            var value = property.GetArrayElementAtIndex(i).FindPropertyRelative("itemId").stringValue;
            if (!string.IsNullOrWhiteSpace(value) && !values.ContainsKey(value)) values.Add(value, i);
        }

        return values;
    }

    private static string ToItemId(string name)
    {
        return name.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
    }

    private readonly struct FoodItemAsset
    {
        public FoodItemAsset(string path, string itemId)
        {
            Path = path;
            ItemId = itemId;
        }

        public string Path { get; }
        public string ItemId { get; }
    }
}
