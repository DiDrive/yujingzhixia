using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class BootUpdateUIBuilder
{
    private const string BootScenePath = "Assets/Scenes/BootScene.unity";
    private const string MenuPath = "Tools/余烬之下/启动更新界面/创建或更新";
    private const string SourceFontPath = "Assets/TextMesh Pro/Fonts/SourceHanSansSC-VF.otf";

    [MenuItem(MenuPath)]
    private static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != BootScenePath)
        {
            EditorUtility.DisplayDialog(
                "启动更新界面",
                "请先打开 Assets/Scenes/BootScene.unity，再执行此菜单。",
                "知道了");
            return;
        }

        var canvas = Object.FindObjectOfType<Canvas>(true);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("启动更新界面", "BootScene 中没有找到 Canvas。", "知道了");
            return;
        }

        var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (font == null)
        {
            EditorUtility.DisplayDialog(
                "启动更新界面",
                "没有找到启动界面所需的思源黑体字体资源。",
                "知道了");
            return;
        }

        Undo.SetCurrentGroupName("创建启动更新界面");

        var oldRoot = canvas.transform.Find("BootUpdateUI");
        if (oldRoot != null)
            Undo.DestroyObjectImmediate(oldRoot.gameObject);

        ConfigureCanvas(canvas);

        var root = CreateUIObject("BootUpdateUI", canvas.transform);
        Stretch(root.GetComponent<RectTransform>());
        var rootGroup = root.AddComponent<CanvasGroup>();
        var background = root.AddComponent<Image>();
        background.color = new Color32(13, 18, 22, 255);

        var panel = CreateUIObject("UpdatePanel", root.transform);
        SetRect(panel.GetComponent<RectTransform>(), new Vector2(760f, 380f), Vector2.zero);
        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color32(25, 33, 38, 245);

        var title = CreateText("TitleText", panel.transform, font, "余烬之下", 42,
            new Vector2(0f, 122f), new Vector2(680f, 58f), FontStyle.Bold);
        var status = CreateText("StatusText", panel.transform, font, "正在准备资源更新", 26,
            new Vector2(0f, 55f), new Vector2(680f, 42f), FontStyle.Normal);
        var detail = CreateText("DetailText", panel.transform, font, "请稍候……", 18,
            new Vector2(0f, 13f), new Vector2(680f, 36f), FontStyle.Normal);
        detail.color = new Color32(174, 190, 198, 255);

        var progressBackground = CreateUIObject("ProgressBackground", panel.transform);
        SetRect(progressBackground.GetComponent<RectTransform>(), new Vector2(640f, 18f), new Vector2(0f, -38f));
        var progressBackgroundImage = progressBackground.AddComponent<Image>();
        progressBackgroundImage.color = new Color32(48, 59, 64, 255);

        var progressFillObject = CreateUIObject("ProgressFill", progressBackground.transform);
        Stretch(progressFillObject.GetComponent<RectTransform>());
        var progressFill = progressFillObject.AddComponent<Image>();
        progressFill.color = new Color32(213, 120, 52, 255);
        progressFill.type = Image.Type.Simple;
        SetHorizontalFill(progressFill.rectTransform, 0f);

        var version = CreateText("VersionText", panel.transform, font, "资源版本：--", 16,
            new Vector2(0f, -76f), new Vector2(640f, 28f), FontStyle.Normal);
        version.color = new Color32(130, 145, 152, 255);

        var enter = CreateButton("EnterButton", panel.transform, font, "进入游戏",
            new Vector2(0f, -130f));
        var retry = CreateButton("RetryButton", panel.transform, font, "重试",
            new Vector2(-110f, -130f));
        var exit = CreateButton("ExitButton", panel.transform, font, "退出游戏",
            new Vector2(110f, -130f));
        enter.gameObject.SetActive(false);
        retry.gameObject.SetActive(false);
        exit.gameObject.SetActive(false);

        var view = root.AddComponent<BootUpdateUI>();
        var viewObject = new SerializedObject(view);
        viewObject.FindProperty("rootGroup").objectReferenceValue = rootGroup;
        viewObject.FindProperty("titleText").objectReferenceValue = title;
        viewObject.FindProperty("statusText").objectReferenceValue = status;
        viewObject.FindProperty("detailText").objectReferenceValue = detail;
        viewObject.FindProperty("versionText").objectReferenceValue = version;
        viewObject.FindProperty("progressFill").objectReferenceValue = progressFill;
        viewObject.FindProperty("enterButton").objectReferenceValue = enter;
        viewObject.FindProperty("retryButton").objectReferenceValue = retry;
        viewObject.FindProperty("exitButton").objectReferenceValue = exit;
        viewObject.ApplyModifiedPropertiesWithoutUndo();

        var updater = Object.FindObjectOfType<MyYooAsset>(true);
        if (updater != null)
        {
            var updaterObject = new SerializedObject(updater);
            updaterObject.FindProperty("updateUI").objectReferenceValue = view;
            updaterObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(updater);
        }

        EditorUtility.SetDirty(canvas);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        Debug.Log("BootScene 启动更新界面已创建并保存。");
    }

    private static void ConfigureCanvas(Canvas canvas)
    {
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = Undo.AddComponent<CanvasScaler>(canvas.gameObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvas.pixelPerfect = true;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(gameObject, $"创建 {name}");
        gameObject.layer = LayerMask.NameToLayer("UI");
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static Text CreateText(
        string name,
        Transform parent,
        Font font,
        string content,
        int fontSize,
        Vector2 position,
        Vector2 size,
        FontStyle style)
    {
        var gameObject = CreateUIObject(name, parent);
        SetRect(gameObject.GetComponent<RectTransform>(), size, position);
        var text = gameObject.AddComponent<Text>();
        text.text = content;
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(
        string name,
        Transform parent,
        Font font,
        string label,
        Vector2 position)
    {
        var gameObject = CreateUIObject(name, parent);
        SetRect(gameObject.GetComponent<RectTransform>(), new Vector2(180f, 52f), position);
        var image = gameObject.AddComponent<Image>();
        image.color = new Color32(68, 80, 85, 255);
        var button = gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        var text = CreateText("Label", gameObject.transform, font, label, 20,
            Vector2.zero, new Vector2(170f, 46f), FontStyle.Normal);
        Stretch(text.rectTransform);
        return button;
    }

    private static void SetRect(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetHorizontalFill(RectTransform rect, float progress)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
