#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 人物生存 HUD 的编辑器构建工具。
/// 生成结果是普通场景对象，位置、尺寸、颜色、字体和层级都能在 Inspector 中继续调整。
/// </summary>
[CustomEditor(typeof(PlayerSurvivalHUD))]
public class PlayerSurvivalHUDEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "HUD 是保存在场景中的普通 UI，不会在运行时生成。可直接展开 PlayerHUD 层级修改位置、大小与样式。",
            MessageType.Info);

        if (GUILayout.Button("选中 HUD 根节点"))
            Selection.activeGameObject = ((PlayerSurvivalHUD)target).gameObject;
    }
}

/// <summary>
/// 提供菜单命令，并在本功能首次导入且 StartScene 已打开时创建一次默认 HUD。
/// </summary>
[InitializeOnLoad]
public static class PlayerSurvivalHUDSceneBuilder
{
    private const string HudObjectName = "PlayerHUD";
    private const string MenuPath = "Tools/余烬之下/人物 HUD/创建人物生存 HUD";
    private static readonly Color PanelColor = new(0.045f, 0.055f, 0.06f, 0.9f);
    private static Font buildFont;

    static PlayerSurvivalHUDSceneBuilder()
    {
        EditorApplication.delayCall += TryInstallOnce;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorSceneManager.sceneSaved += OnSceneSaved;
    }

    [MenuItem(MenuPath)]
    public static void CreateFromMenu()
    {
        var hud = CreateInActiveScene(false);
        if (hud == null) return;
        Selection.activeGameObject = hud.gameObject;
        EditorGUIUtility.PingObject(hud.gameObject);
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += TryInstallOnce;
    }

    private static void TryInstallOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;

        // 只有场景真正保存后才记录完成，避免用户关闭未保存场景后 HUD 永久不再自动出现。
        var key = GetInstallKey();
        if (EditorPrefs.GetBool(key, false)) return;

        var activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.path != "Assets/Scenes/StartScene.unity") return;

        var existing = UnityEngine.Object.FindObjectOfType<PlayerSurvivalHUD>(true);
        if (existing != null)
        {
            if (existing.HasRequiredReferences)
            {
                if (!activeScene.isDirty) EditorPrefs.SetBool(key, true);
                return;
            }

            // 只清理由本工具创建但中途失败的同名节点，不触碰用户自己命名的 HUD。
            if (existing.name != HudObjectName) return;
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        CreateInActiveScene(true);
        // 创建后只标记场景为脏；保存时再由 OnSceneSaved 记录安装完成。
    }

    private static void OnSceneSaved(Scene scene)
    {
        if (scene.path != "Assets/Scenes/StartScene.unity") return;
        var existing = UnityEngine.Object.FindObjectOfType<PlayerSurvivalHUD>(true);
        if (existing != null && existing.HasRequiredReferences)
            EditorPrefs.SetBool(GetInstallKey(), true);
    }

    private static string GetInstallKey()
    {
        return $"EmbersBelow.PlayerSurvivalHUD.Installed.V3.{Application.dataPath.GetHashCode()}";
    }

    /// <summary>
    /// 在活动场景主 Canvas 下创建完整 HUD。不会保存场景，以免意外保存用户尚未确认的其他编辑。
    /// </summary>
    private static PlayerSurvivalHUD CreateInActiveScene(bool silent)
    {
        var existing = UnityEngine.Object.FindObjectOfType<PlayerSurvivalHUD>(true);
        if (existing != null)
        {
            if (!existing.HasRequiredReferences && existing.name == HudObjectName)
            {
                if (!silent && !EditorUtility.DisplayDialog(
                        "人物 HUD",
                        "检测到一个未完成的 PlayerHUD，是否删除并重新创建？",
                        "重新创建",
                        "取消"))
                    return null;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }
            else
            {
            if (!silent) EditorUtility.DisplayDialog("人物 HUD", "场景中已经存在 PlayerSurvivalHUD。", "知道了");
            return existing;
            }
        }

        var canvas = FindMainCanvas();
        if (canvas == null)
        {
            if (!silent) EditorUtility.DisplayDialog("人物 HUD", "没有找到可用的主 Canvas，请先打开 StartScene。", "知道了");
            return null;
        }
        buildFont = FindHudFont(canvas);

        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Player Survival HUD");

        var root = CreateUiObject(HudObjectName, canvas.transform, typeof(Image), typeof(CanvasGroup));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = new Vector2(24f, -24f);
        rootRect.sizeDelta = new Vector2(304f, 206f);

        var panelImage = root.GetComponent<Image>();
        panelImage.color = PanelColor;
        panelImage.raycastTarget = false;
        var canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        var hud = Undo.AddComponent<PlayerSurvivalHUD>(root);
        var title = CreateText("Title", root.transform, "生存状态", 16, TextAnchor.MiddleLeft);
        SetRect(title.rectTransform, new Vector2(12f, -8f), new Vector2(280f, 26f));
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(0.9f, 0.92f, 0.9f, 1f);

        var hp = CreateStatRow(root.transform, "HP", "生命", 42f, new Color(0.74f, 0.18f, 0.16f, 1f));
        var hunger = CreateStatRow(root.transform, "Hunger", "饥饿", 76f, new Color(0.83f, 0.53f, 0.15f, 1f));
        var thirst = CreateStatRow(root.transform, "Thirst", "口渴", 110f, new Color(0.18f, 0.55f, 0.78f, 1f));
        var fatigue = CreateStatRow(root.transform, "Fatigue", "精力", 144f, new Color(0.43f, 0.68f, 0.37f, 1f));

        var infection = CreateText("InfectionStatus", root.transform, "状态正常", 13, TextAnchor.MiddleLeft);
        SetRect(infection.rectTransform, new Vector2(12f, -178f), new Vector2(280f, 20f));

        hud.Configure(
            hp.Fill, hp.Value,
            hunger.Fill, hunger.Value,
            thirst.Fill, thirst.Value,
            fatigue.Fill, fatigue.Value,
            infection);

        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Undo.CollapseUndoOperations(undoGroup);

        if (!silent)
        {
            EditorUtility.DisplayDialog(
                "人物 HUD 已创建",
                "已在主 Canvas 下创建 PlayerHUD。它是编辑器场景对象，可以直接调整；请记得保存场景。",
                "知道了");
        }
        return hud;
    }

    private static Canvas FindMainCanvas()
    {
        var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
        Canvas fallback = null;
        for (var i = 0; i < canvases.Length; i++)
        {
            var canvas = canvases[i];
            if (canvas == null || canvas.transform.parent != null) continue;
            fallback ??= canvas;
            if (canvas.name == "Canvas") return canvas;
        }
        return fallback;
    }

    /// <summary>
    /// 优先复用场景中已有 UGUI Text 的字体，避免新 HUD 与现有中文字体风格不一致。
    /// </summary>
    private static Font FindHudFont(Canvas canvas)
    {
        var existingText = canvas != null ? canvas.GetComponentInChildren<Text>(true) : null;
        if (existingText != null && existingText.font != null) return existingText.font;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static StatRow CreateStatRow(Transform parent, string objectName, string label, float top, Color fillColor)
    {
        var row = CreateUiObject(objectName, parent);
        SetRect((RectTransform)row.transform, new Vector2(12f, -top), new Vector2(280f, 26f));

        var labelText = CreateText("Label", row.transform, label, 13, TextAnchor.MiddleLeft);
        SetRect(labelText.rectTransform, Vector2.zero, new Vector2(54f, 26f));

        var bar = CreateUiObject("Bar", row.transform, typeof(Image));
        SetRect((RectTransform)bar.transform, new Vector2(56f, -4f), new Vector2(224f, 18f));
        var barImage = bar.GetComponent<Image>();
        barImage.color = new Color(0.12f, 0.13f, 0.14f, 0.95f);
        barImage.raycastTarget = false;

        var fill = CreateUiObject("Fill", bar.transform, typeof(Image));
        var fillRect = (RectTransform)fill.transform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);
        var fillImage = fill.GetComponent<Image>();
        fillImage.color = fillColor;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = 0;
        fillImage.fillAmount = 0.75f;
        fillImage.raycastTarget = false;

        var value = CreateText("Value", bar.transform, "75 / 100", 11, TextAnchor.MiddleCenter);
        var valueRect = value.rectTransform;
        valueRect.anchorMin = Vector2.zero;
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;
        value.color = Color.white;

        return new StatRow(fillImage, value);
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

    private static Text CreateText(string name, Transform parent, string value, int fontSize, TextAnchor alignment)
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

    private static void SetRect(RectTransform rect, Vector2 topLeftPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeftPosition;
        rect.sizeDelta = size;
    }

    private readonly struct StatRow
    {
        public readonly Image Fill;
        public readonly Text Value;

        public StatRow(Image fill, Text value)
        {
            Fill = fill;
            Value = value;
        }
    }
}
#endif
