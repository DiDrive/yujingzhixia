using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[CustomEditor(typeof(SearchableLootContainer))]
public class SearchableLootContainerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        serializedObject.Update();

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField(
            new GUIContent("场景 UI 编辑", "生成真实的场景 UI 对象，之后可以像主背包一样直接调整 RectTransform、背景、格子大小和位置。"),
            EditorStyles.boldLabel);

        var sceneViewProperty = serializedObject.FindProperty("containerUiSceneView");
        var sceneView = sceneViewProperty.objectReferenceValue as GridInventoryUnityUI;

        if (sceneView == null)
        {
            EditorGUILayout.HelpBox("当前使用运行时动态生成的物资窗口。点击下面按钮后，会在主 Canvas 下创建一个编辑模式可见的容器 UI。", MessageType.Info);
            var createContent = new GUIContent(
                "创建场景容器 UI",
                "在主背包 Canvas 下创建 InventoryUI_loot_* 对象，并自动绑定到当前搜索容器。该对象在编辑模式可见，运行时搜索前自动隐藏。");
            if (GUILayout.Button(createContent, GUILayout.Height(28f)))
            {
                sceneView = CreateSceneView();
                sceneViewProperty.objectReferenceValue = sceneView;
                serializedObject.ApplyModifiedProperties();
                MarkDirty();
            }
        }
        else
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var selectContent = new GUIContent(
                    "选中场景 UI",
                    "在 Hierarchy 中选中该容器 UI，方便直接使用 RectTransform 工具调整位置和大小。");
                if (GUILayout.Button(selectContent))
                {
                    Selection.activeGameObject = sceneView.gameObject;
                    EditorGUIUtility.PingObject(sceneView.gameObject);
                }

                var syncContent = new GUIContent(
                    "同步格子预览",
                    "根据当前 Rows、Columns，以及场景 UI 上的 Cell Size、Cell Spacing 重建编辑器格子；保留你手工调整的面板位置和大小。");
                if (GUILayout.Button(syncContent))
                {
                    SyncSceneView(sceneView, false);
                }
            }

            var fitContent = new GUIContent(
                "按当前网格推荐面板尺寸",
                "根据行列数、格子大小和间距自动计算面板尺寸。只在你主动点击时修改 RectTransform，不会持续覆盖手工布局。");
            if (GUILayout.Button(fitContent))
            {
                SyncSceneView(sceneView, true);
            }

            EditorGUILayout.HelpBox(
                "调整方法：点击“选中场景 UI”，然后在 Scene/Inspector 中修改 RectTransform。修改 GridInventoryUnityUI 的 Cell Size 或 Cell Spacing 后，点击“同步格子预览”。",
                MessageType.None);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private GridInventoryUnityUI CreateSceneView()
    {
        var container = (SearchableLootContainer)target;
        EnsurePersistentId();
        serializedObject.ApplyModifiedProperties();

        var mainUi = FindMainInventoryUi();
        var parent = ResolveUiParent(mainUi);
        var persistentId = serializedObject.FindProperty("persistentId").stringValue;
        var objectName = $"InventoryUI_loot_{Sanitize(persistentId)}";

        var rootObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Undo.RegisterCreatedObjectUndo(rootObject, "Create Searchable Container UI");
        rootObject.transform.SetParent(parent, false);
        var rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);

        var offset = serializedObject.FindProperty("containerUiOffset").vector2Value;
        var mainRect = mainUi != null ? mainUi.transform as RectTransform : null;
        rootRect.anchoredPosition = (mainRect != null ? mainRect.anchoredPosition : Vector2.zero) + offset;
        rootObject.GetComponent<Image>().color = new Color(0.055f, 0.055f, 0.055f, 0.94f);

        var ui = rootObject.AddComponent<GridInventoryUnityUI>();
        BuildManualHierarchy(ui, rootRect);
        ConfigureUiComponent(ui);
        SyncSceneView(ui, true);
        Selection.activeGameObject = rootObject;
        EditorGUIUtility.PingObject(rootObject);
        return ui;
    }

    private void BuildManualHierarchy(GridInventoryUnityUI ui, RectTransform root)
    {
        var title = CreateText("Title", root, "物资容器", 18, TextAnchor.MiddleLeft);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(12f, -42f);
        titleRect.offsetMax = new Vector2(-48f, -6f);

        var toolRoot = CreateRect("ToolRoot", root, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero);
        var toolbar = CreateRect("ContainerToolbar", toolRoot, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        var spawn = CreateRect("SpawnToolbar", toolRoot, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        var action = CreateRect("ActionToolbar", toolRoot, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        toolbar.gameObject.SetActive(false);
        spawn.gameObject.SetActive(false);
        action.gameObject.SetActive(false);

        var gridRoot = CreateRect("GridRoot", root, new Vector2(200f, 160f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -50f));
        var gridImage = gridRoot.gameObject.AddComponent<Image>();
        gridImage.color = new Color(0.09f, 0.09f, 0.09f, 0.95f);
        var cellLayer = CreateRect("CellLayer", root, gridRoot.sizeDelta, gridRoot.anchorMin, gridRoot.anchorMax, gridRoot.pivot, gridRoot.anchoredPosition);
        var itemLayer = CreateRect("ItemLayer", root, gridRoot.sizeDelta, gridRoot.anchorMin, gridRoot.anchorMax, gridRoot.pivot, gridRoot.anchoredPosition);
        var ghostLayer = CreateRect("GhostLayer", root, gridRoot.sizeDelta, gridRoot.anchorMin, gridRoot.anchorMax, gridRoot.pivot, gridRoot.anchoredPosition);
        ghostLayer.gameObject.SetActive(false);

        var serializedUi = new SerializedObject(ui);
        serializedUi.FindProperty("titleTextOverride").objectReferenceValue = title;
        serializedUi.FindProperty("toolRootOverride").objectReferenceValue = toolRoot;
        serializedUi.FindProperty("toolbarRootOverride").objectReferenceValue = toolbar;
        serializedUi.FindProperty("spawnRootOverride").objectReferenceValue = spawn;
        serializedUi.FindProperty("actionRootOverride").objectReferenceValue = action;
        serializedUi.FindProperty("gridRootOverride").objectReferenceValue = gridRoot;
        serializedUi.FindProperty("cellLayerRootOverride").objectReferenceValue = cellLayer;
        serializedUi.FindProperty("itemLayerRootOverride").objectReferenceValue = itemLayer;
        serializedUi.FindProperty("ghostRootOverride").objectReferenceValue = ghostLayer;
        serializedUi.FindProperty("panelBackgroundOverride").objectReferenceValue = root.GetComponent<Image>();
        serializedUi.FindProperty("gridBackgroundOverride").objectReferenceValue = gridImage;
        serializedUi.ApplyModifiedPropertiesWithoutUndo();
    }

    private void ConfigureUiComponent(GridInventoryUnityUI ui)
    {
        var service = FindObjectOfType<GridInventoryService>();
        var persistentId = serializedObject.FindProperty("persistentId").stringValue;
        var serializedUi = new SerializedObject(ui);
        serializedUi.FindProperty("gridInventoryService").objectReferenceValue = service;
        serializedUi.FindProperty("startOpen").boolValue = false;
        serializedUi.FindProperty("showSpawnTools").boolValue = false;
        serializedUi.FindProperty("allowSpawnFromEmptyCells").boolValue = false;
        serializedUi.FindProperty("openOtherContainersAsSidePanels").boolValue = false;
        serializedUi.FindProperty("buildRuntimeLayout").boolValue = false;
        serializedUi.FindProperty("strictManualMode").boolValue = true;
        serializedUi.FindProperty("autoAlignToolbarWithGrid").boolValue = false;
        serializedUi.FindProperty("autoResizePanelBackground").boolValue = false;
        serializedUi.FindProperty("autoResizeManualGridToContainer").boolValue = false;
        serializedUi.FindProperty("showContainerToolbar").boolValue = false;
        serializedUi.FindProperty("showActionToolbar").boolValue = false;
        serializedUi.FindProperty("currentContainerId").stringValue = $"loot_{Sanitize(persistentId)}";
        serializedUi.FindProperty("cellSize").floatValue = serializedObject.FindProperty("containerUiCellSize").floatValue;
        serializedUi.FindProperty("cellSpacing").floatValue = serializedObject.FindProperty("containerUiCellSpacing").floatValue;
        serializedUi.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
    }

    private void SyncSceneView(GridInventoryUnityUI ui, bool fitPanel)
    {
        if (ui == null) return;
        var rows = Mathf.Max(1, serializedObject.FindProperty("rows").intValue);
        var columns = Mathf.Max(1, serializedObject.FindProperty("columns").intValue);
        var serializedUi = new SerializedObject(ui);
        var cellSize = Mathf.Clamp(serializedUi.FindProperty("cellSize").floatValue, 20f, 96f);
        var spacing = Mathf.Clamp(serializedUi.FindProperty("cellSpacing").floatValue, 0f, 16f);
        var gridRoot = serializedUi.FindProperty("gridRootOverride").objectReferenceValue as RectTransform;
        var cellLayer = serializedUi.FindProperty("cellLayerRootOverride").objectReferenceValue as RectTransform;
        var itemLayer = serializedUi.FindProperty("itemLayerRootOverride").objectReferenceValue as RectTransform;
        var ghostLayer = serializedUi.FindProperty("ghostRootOverride").objectReferenceValue as RectTransform;
        if (gridRoot == null || cellLayer == null || itemLayer == null || ghostLayer == null) return;

        var contentWidth = columns * cellSize + Mathf.Max(0, columns - 1) * spacing;
        var contentHeight = rows * cellSize + Mathf.Max(0, rows - 1) * spacing;
        var gridSize = new Vector2(contentWidth, contentHeight);
        gridRoot.sizeDelta = gridSize;
        cellLayer.sizeDelta = gridSize;
        itemLayer.sizeDelta = gridSize;
        ghostLayer.sizeDelta = gridSize;
        cellLayer.anchoredPosition = gridRoot.anchoredPosition;
        itemLayer.anchoredPosition = gridRoot.anchoredPosition;
        ghostLayer.anchoredPosition = gridRoot.anchoredPosition;

        ClearChildren(cellLayer);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var cell = CreateRect(
                    $"Cell_{row}_{column}",
                    cellLayer,
                    new Vector2(cellSize, cellSize),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(column * (cellSize + spacing), -row * (cellSize + spacing)));
                var image = cell.gameObject.AddComponent<Image>();
                image.color = new Color(0.18f, 0.14f, 0.13f, 0.95f);
            }
        }

        if (fitPanel)
        {
            var rootRect = ui.transform as RectTransform;
            if (rootRect != null) rootRect.sizeDelta = new Vector2(contentWidth + 28f, contentHeight + 64f);
        }

        var title = serializedUi.FindProperty("titleTextOverride").objectReferenceValue as Text;
        if (title != null)
        {
            title.text = serializedObject.FindProperty("displayName").stringValue;
        }

        EditorUtility.SetDirty(ui);
        EditorUtility.SetDirty(ui.gameObject);
        MarkDirty();
        SceneView.RepaintAll();
    }

    private void EnsurePersistentId()
    {
        var idProperty = serializedObject.FindProperty("persistentId");
        if (!string.IsNullOrWhiteSpace(idProperty.stringValue)) return;
        idProperty.stringValue = Guid.NewGuid().ToString("N").Substring(0, 12);
    }

    private static GridInventoryUnityUI FindMainInventoryUi()
    {
        var views = FindObjectsOfType<GridInventoryUnityUI>(true);
        for (var i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].name == "InventoryUI_main_bag") return views[i];
        }
        for (var i = 0; i < views.Length; i++)
        {
            if (views[i] != null && !views[i].IsSideContainerView) return views[i];
        }
        return null;
    }

    private static Transform ResolveUiParent(GridInventoryUnityUI mainUi)
    {
        if (mainUi != null)
        {
            var canvas = mainUi.GetComponentInParent<Canvas>();
            if (canvas != null) return canvas.transform;
            if (mainUi.transform.parent != null) return mainUi.transform.parent;
        }

        var existingCanvas = FindObjectOfType<Canvas>();
        if (existingCanvas != null) return existingCanvas.transform;
        var canvasObject = new GameObject("InventoryCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Create Inventory Canvas");
        var canvasComponent = canvasObject.GetComponent<Canvas>();
        canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
        return canvasObject.transform;
    }

    private static RectTransform CreateRect(
        string name,
        Transform parent,
        Vector2 size,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");
        gameObject.transform.SetParent(parent, false);
        var rect = gameObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        return rect;
    }

    private static Text CreateText(string name, Transform parent, string value, int fontSize, TextAnchor alignment)
    {
        var rect = CreateRect(name, parent, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        return text;
    }

    private static void ClearChildren(RectTransform root)
    {
        for (var i = root.childCount - 1; i >= 0; i--)
        {
            Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
        }
    }

    private static string Sanitize(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "container";
        var chars = source.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
        }
        return new string(chars);
    }

    private void MarkDirty()
    {
        EditorUtility.SetDirty(target);
        if (((SearchableLootContainer)target).gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(((SearchableLootContainer)target).gameObject.scene);
        }
    }
}
