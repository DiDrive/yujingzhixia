#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 小地图控制器的编辑器说明与快捷入口。
/// </summary>
[CustomEditor(typeof(MinimapController))]
public class MinimapControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "小地图 UI 是场景和 Prefab 中的普通对象。可直接展开 MinimapUI 修改位置、尺寸、颜色、图标和底图；运行时不会重建整套 UI。",
            MessageType.Info);

        var controller = (MinimapController)target;
        if (GUILayout.Button("刷新场景标记"))
        {
            controller.RefreshMarkers();
            EditorUtility.SetDirty(controller);
        }
        if (GUILayout.Button("选中小地图根节点"))
            Selection.activeGameObject = controller.gameObject;
    }
}

/// <summary>
/// 在主 Canvas 下创建第一版小地图，并保存为可复用 Prefab。
/// 所有生成节点都保存在编辑器中，不依赖运行时构建。
/// </summary>
public static class MinimapSceneBuilder
{
    private const string RootName = "MinimapUI";
    private const string MenuPath = "Tools/余烬之下/小地图/创建或选中小地图";
    private const string PrefabFolder = "Assets/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/MinimapUI.prefab";
    private static Font buildFont;

    [MenuItem(MenuPath)]
    public static void CreateFromMenu()
    {
        var controller = CreateInActiveScene(false);
        if (controller == null) return;
        Selection.activeGameObject = controller.gameObject;
        EditorGUIUtility.PingObject(controller.gameObject);
    }

    /// <summary>
    /// 自动化和菜单共用的创建入口。检测到现有小地图时直接返回，不覆盖用户调整。
    /// </summary>
    public static MinimapController CreateInActiveScene(bool silent)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return null;

        var existing = FindInActiveScene<MinimapController>();
        if (existing != null)
        {
            if (!silent)
                EditorUtility.DisplayDialog("小地图", "当前场景已经存在 MinimapUI，已为你选中。", "知道了");
            return existing;
        }

        var canvas = FindMainCanvas(scene);
        if (canvas == null)
        {
            if (!silent)
                EditorUtility.DisplayDialog("小地图", "当前场景没有找到根 Canvas。", "知道了");
            return null;
        }
        buildFont = FindUiFont(canvas);

        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Minimap UI");

        var root = CreateUiObject(RootName, canvas.transform, typeof(Image), typeof(CanvasGroup));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = new Vector2(1f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(1f, 1f);
        rootRect.anchoredPosition = new Vector2(-24f, -24f);
        rootRect.sizeDelta = new Vector2(272f, 238f);

        var panel = root.GetComponent<Image>();
        panel.color = new Color(0.035f, 0.045f, 0.05f, 0.92f);
        panel.raycastTarget = false;
        var group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var controller = Undo.AddComponent<MinimapController>(root);

        var title = CreateText("Title", root.transform, "区域地图", 16, TextAnchor.MiddleLeft);
        SetTopLeftRect(title.rectTransform, new Vector2(12f, -6f), new Vector2(160f, 30f));
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(0.9f, 0.93f, 0.9f, 1f);

        var floorLabel = CreateText("FloorLabel", root.transform, "一楼", 13, TextAnchor.MiddleRight);
        SetTopLeftRect(floorLabel.rectTransform, new Vector2(176f, -6f), new Vector2(84f, 30f));
        floorLabel.color = new Color(0.35f, 0.82f, 1f, 1f);

        var viewport = CreateUiObject("Viewport", root.transform, typeof(Image), typeof(RectMask2D));
        var viewportRect = (RectTransform)viewport.transform;
        SetTopLeftRect(viewportRect, new Vector2(12f, -40f), new Vector2(248f, 166f));
        var viewportImage = viewport.GetComponent<Image>();
        viewportImage.color = new Color(0.08f, 0.1f, 0.11f, 1f);
        viewportImage.raycastTarget = false;

        var mapImageObject = CreateUiObject("FloorMap", viewport.transform, typeof(Image));
        var mapImageRect = (RectTransform)mapImageObject.transform;
        StretchRect(mapImageRect);
        var mapImage = mapImageObject.GetComponent<Image>();
        mapImage.color = new Color(0.08f, 0.11f, 0.12f, 1f);
        mapImage.raycastTarget = false;
        mapImage.preserveAspect = false;

        // 原型阶段使用编辑器可见的网格；正式底图赋给 FloorMap 的 Sprite 后可整体隐藏 GridOverlay。
        var gridOverlay = CreateUiObject("GridOverlay", viewport.transform);
        StretchRect((RectTransform)gridOverlay.transform);
        CreateGridLines(gridOverlay.transform, 8, 6);

        var markerLayerObject = CreateUiObject("MarkerLayer", viewport.transform);
        var markerLayer = (RectTransform)markerLayerObject.transform;
        StretchRect(markerLayer);

        var playerMarker = CreateMarker(
            "PlayerMarker",
            markerLayer,
            new Color(0.2f, 0.82f, 1f, 1f),
            "▲",
            14f);

        var extractionMarker = CreateMarker(
            "ExtractionMarker_Default",
            markerLayer,
            new Color(0.25f, 0.95f, 0.5f, 1f),
            "撤",
            18f);

        var status = CreateText(
            "StatusLabel",
            root.transform,
            "撤离点 / 已发现兴趣点",
            12,
            TextAnchor.MiddleLeft);
        SetTopLeftRect(status.rectTransform, new Vector2(12f, -212f), new Vector2(248f, 20f));
        status.color = new Color(0.62f, 0.67f, 0.66f, 1f);

        var floorDefinitions = new[]
        {
            CreateFloor(0, "一楼", new Color(0.075f, 0.105f, 0.115f, 1f)),
            CreateFloor(1, "二楼", new Color(0.085f, 0.095f, 0.125f, 1f)),
            CreateFloor(2, "三楼", new Color(0.095f, 0.085f, 0.115f, 1f))
        };
        controller.ConfigureEditor(
            viewportRect,
            mapImage,
            markerLayer,
            playerMarker,
            floorLabel,
            status,
            floorDefinitions);

        EnsurePrefabFolder();
        var connectedRoot = PrefabUtility.SaveAsPrefabAssetAndConnect(
            root,
            PrefabPath,
            InteractionMode.AutomatedAction);
        if (connectedRoot != null)
            controller = connectedRoot.GetComponent<MinimapController>();

        // 第一版自动把场景中的默认撤离点接入预先创建的 UI 标记。
        var extractionPoint = FindInActiveScene<ExtractionPoint>();
        if (extractionPoint != null)
        {
            var marker = extractionPoint.GetComponent<MinimapMarker>();
            if (marker == null) marker = Undo.AddComponent<MinimapMarker>(extractionPoint.gameObject);
            marker.ConfigureEditor(
                extractionPoint.ExtractionId,
                extractionPoint.DisplayName,
                MinimapMarkerKind.Extraction,
                0,
                true,
                0f,
                extractionMarker,
                extractionMarker.GetComponent<Image>());
            EditorUtility.SetDirty(marker);
        }

        controller.RefreshMarkers();
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);

        if (!silent)
        {
            EditorUtility.DisplayDialog(
                "小地图已创建",
                "MinimapUI 已放入主 Canvas，并保存为 Assets/Prefabs/UI/MinimapUI.prefab。所有位置和样式均可在编辑器中调整。",
                "知道了");
        }
        return controller;
    }

    private static MinimapFloorDefinition CreateFloor(int floorIndex, string displayName, Color color)
    {
        return new MinimapFloorDefinition
        {
            floorIndex = floorIndex,
            displayName = displayName,
            fallbackColor = color,
            worldMin = new Vector2(-36f, -9f),
            worldMax = new Vector2(50f, 37f)
        };
    }

    private static RectTransform CreateMarker(
        string name,
        Transform parent,
        Color color,
        string textValue,
        float size)
    {
        var marker = CreateUiObject(name, parent, typeof(Image));
        var rect = (RectTransform)marker.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = Vector2.zero;
        var image = marker.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        var label = CreateText("Label", marker.transform, textValue, 10, TextAnchor.MiddleCenter);
        StretchRect(label.rectTransform);
        label.color = new Color(0.02f, 0.03f, 0.03f, 1f);
        label.fontStyle = FontStyle.Bold;
        return rect;
    }

    private static void CreateGridLines(Transform parent, int columns, int rows)
    {
        for (var i = 1; i < columns; i++)
        {
            var line = CreateUiObject($"Vertical_{i}", parent, typeof(Image));
            var rect = (RectTransform)line.transform;
            var x = i / (float)columns;
            rect.anchorMin = new Vector2(x, 0f);
            rect.anchorMax = new Vector2(x, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1f, 0f);
            rect.anchoredPosition = Vector2.zero;
            var image = line.GetComponent<Image>();
            image.color = new Color(0.35f, 0.5f, 0.52f, 0.16f);
            image.raycastTarget = false;
        }

        for (var i = 1; i < rows; i++)
        {
            var line = CreateUiObject($"Horizontal_{i}", parent, typeof(Image));
            var rect = (RectTransform)line.transform;
            var y = i / (float)rows;
            rect.anchorMin = new Vector2(0f, y);
            rect.anchorMax = new Vector2(1f, y);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            var image = line.GetComponent<Image>();
            image.color = new Color(0.35f, 0.5f, 0.52f, 0.16f);
            image.raycastTarget = false;
        }
    }

    private static Canvas FindMainCanvas(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var canvas = root.GetComponent<Canvas>();
            if (canvas != null && root.name == "Canvas") return canvas;
        }
        foreach (var root in scene.GetRootGameObjects())
        {
            var canvas = root.GetComponent<Canvas>();
            if (canvas != null) return canvas;
        }
        return null;
    }

    private static T FindInActiveScene<T>() where T : Component
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            var found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }

    private static Font FindUiFont(Canvas canvas)
    {
        var text = canvas != null ? canvas.GetComponentInChildren<Text>(true) : null;
        if (text != null && text.font != null) return text.font;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static GameObject CreateUiObject(string name, Transform parent, params Type[] extraComponents)
    {
        var types = new Type[extraComponents.Length + 2];
        types[0] = typeof(RectTransform);
        types[1] = typeof(CanvasRenderer);
        Array.Copy(extraComponents, 0, types, 2, extraComponents.Length);
        var go = new GameObject(name, types);
        go.layer = LayerMask.NameToLayer("UI");
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        Undo.SetTransformParent(go.transform, parent, $"Parent {name}");
        go.transform.localScale = Vector3.one;
        return go;
    }

    private static Text CreateText(
        string name,
        Transform parent,
        string value,
        int fontSize,
        TextAnchor alignment)
    {
        var go = CreateUiObject(name, parent, typeof(Text));
        var text = go.GetComponent<Text>();
        text.font = buildFont != null
            ? buildFont
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = new Color(0.82f, 0.84f, 0.82f, 1f);
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static void SetTopLeftRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void StretchRect(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void EnsurePrefabFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
    }
}
#endif
