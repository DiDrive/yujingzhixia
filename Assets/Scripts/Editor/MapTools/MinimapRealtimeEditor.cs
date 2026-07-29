#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 把已有 MinimapUI 升级为“专用相机 + RenderTexture + RawImage”的实时整层总览。
/// 生成的相机、RenderTexture 和 RawImage 都是编辑器资产或场景对象。
/// </summary>
[InitializeOnLoad]
public static class MinimapRealtimeSceneBuilder
{
    private const string MenuPath = "Tools/余烬之下/小地图/升级为实时整层总览";
    private const string CameraObjectName = "MinimapCamera";
    private const string RenderTextureFolder = "Assets/RenderTextures";
    private const string RenderTexturePath = RenderTextureFolder + "/MinimapOverview.renderTexture";
    private const string CameraPrefabPath = "Assets/Prefabs/UI/MinimapCamera.prefab";

    static MinimapRealtimeSceneBuilder()
    {
        EditorApplication.delayCall += TryUpgradeOpenedStartScene;
    }

    [MenuItem(MenuPath)]
    public static void UpgradeFromMenu()
    {
        var renderer = UpgradeActiveScene(false);
        if (renderer == null) return;
        Selection.activeGameObject = renderer.gameObject;
        EditorGUIUtility.PingObject(renderer.gameObject);
    }

    /// <summary>
    /// 自动化入口。若已经升级，只补齐引用并返回现有组件，不覆盖用户调整。
    /// </summary>
    public static MinimapRealtimeRenderer UpgradeActiveScene(bool silent)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return null;

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return null;
        var minimap = FindInScene<MinimapController>(scene);
        if (minimap == null)
        {
            if (!silent)
                EditorUtility.DisplayDialog("实时小地图", "当前场景没有 MinimapUI，请先创建小地图。", "知道了");
            return null;
        }

        var sceneWasDirty = scene.isDirty;
        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Upgrade Minimap To Realtime");

        var renderTexture = GetOrCreateRenderTexture();
        var floorMap = minimap.transform.Find("Viewport/FloorMap");
        if (floorMap == null)
        {
            if (!silent)
                EditorUtility.DisplayDialog("实时小地图", "MinimapUI 中缺少 Viewport/FloorMap。", "知道了");
            return null;
        }

        var rawImage = floorMap.GetComponent<RawImage>();
        if (rawImage == null)
        {
            // Unity 的同一 GameObject 只能存在一个 Graphic；实时模式需要用 RawImage
            // 正式替换旧 Image，而不是把两个组件叠加在一起。
            var fallbackImage = floorMap.GetComponent<Image>();
            if (fallbackImage != null) Undo.DestroyObjectImmediate(fallbackImage);
            rawImage = Undo.AddComponent<RawImage>(floorMap.gameObject);
        }
        rawImage.texture = renderTexture;
        rawImage.color = Color.white;
        rawImage.raycastTarget = false;
        rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);

        var gridOverlay = minimap.transform.Find("Viewport/GridOverlay");
        if (gridOverlay != null) gridOverlay.gameObject.SetActive(false);
        minimap.ConfigureRealtimeEditor(rawImage);
        EditorUtility.SetDirty(minimap);

        var cameraObject = FindRootObject(scene, CameraObjectName);
        if (cameraObject == null)
        {
            cameraObject = new GameObject(CameraObjectName, typeof(Camera), typeof(MinimapRealtimeRenderer));
            Undo.RegisterCreatedObjectUndo(cameraObject, "Create Minimap Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
        }

        var camera = cameraObject.GetComponent<Camera>();
        var renderer = cameraObject.GetComponent<MinimapRealtimeRenderer>();
        if (camera == null) camera = Undo.AddComponent<Camera>(cameraObject);
        if (renderer == null) renderer = Undo.AddComponent<MinimapRealtimeRenderer>(cameraObject);

        var floorVisibility = FindInScene<FloorVisibilityController>(scene);
        var mask = BuildDefaultCullingMask();
        renderer.ConfigureEditor(
            renderTexture,
            floorVisibility,
            new Vector2(-36f, -9f),
            new Vector2(50f, 37f),
            mask,
            10f);
        EditorUtility.SetDirty(camera);
        EditorUtility.SetDirty(renderer);

        EnsureFolder("Assets/Prefabs", "UI");
        PrefabUtility.SaveAsPrefabAssetAndConnect(
            cameraObject,
            CameraPrefabPath,
            InteractionMode.AutomatedAction);

        if (PrefabUtility.IsPartOfPrefabInstance(minimap.gameObject))
        {
            PrefabUtility.ApplyPrefabInstance(
                minimap.gameObject,
                InteractionMode.AutomatedAction);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);

        // 自动升级只在场景原本干净时保存，避免顺带保存用户未确认的其他编辑。
        if (silent && !sceneWasDirty)
            EditorSceneManager.SaveScene(scene);

        if (!silent)
        {
            EditorUtility.DisplayDialog(
                "实时小地图已启用",
                "小地图现在通过 MinimapCamera 实时显示整层场景。室外复用楼层系统显示建筑楼顶；进入建筑后显示玩家当前楼层。",
                "知道了");
        }
        return renderer;
    }

    private static void TryUpgradeOpenedStartScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != "Assets/Scenes/StartScene.unity") return;
        if (FindInScene<MinimapController>(scene) == null) return;
        UpgradeActiveScene(true);
    }

    private static RenderTexture GetOrCreateRenderTexture()
    {
        EnsureFolder("Assets", "RenderTextures");
        var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
        if (existing != null) return existing;

        // 世界边界为 86×46，RenderTexture 使用相同比例，确保地图图像与 UI 标记严格对齐。
        var texture = new RenderTexture(512, 274, 16, RenderTextureFormat.ARGB32)
        {
            name = "MinimapOverview",
            antiAliasing = 1,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };
        AssetDatabase.CreateAsset(texture, RenderTexturePath);
        AssetDatabase.SaveAssets();
        return texture;
    }

    private static LayerMask BuildDefaultCullingMask()
    {
        var mask = ~0;
        ExcludeLayer(ref mask, "UI");
        ExcludeLayer(ref mask, "Actor_F0");
        ExcludeLayer(ref mask, "Actor_F1");
        ExcludeLayer(ref mask, "Actor_F2");
        return mask;
    }

    private static void ExcludeLayer(ref int mask, string layerName)
    {
        var layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0) mask &= ~(1 << layer);
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }

    private static GameObject FindRootObject(Scene scene, string objectName)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == objectName) return root;
        }
        return null;
    }

    private static void EnsureFolder(string parent, string child)
    {
        var path = parent + "/" + child;
        if (AssetDatabase.IsValidFolder(path)) return;
        if (!AssetDatabase.IsValidFolder(parent))
        {
            var slash = parent.LastIndexOf('/');
            if (slash > 0)
                EnsureFolder(parent.Substring(0, slash), parent.Substring(slash + 1));
        }
        AssetDatabase.CreateFolder(parent, child);
    }
}

/// <summary>
/// 实时小地图相机的 Inspector 说明。
/// </summary>
[CustomEditor(typeof(MinimapRealtimeRenderer))]
public class MinimapRealtimeRendererEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "Follow Player 模式下玩家固定在中央，地图随玩家移动；Whole Floor Overview 可切回固定整层总览。相机按 Refresh Rate 手动刷新，并复用 FloorVisibilityController 的室内外显隐结果。",
            MessageType.Info);

        if (GUILayout.Button("立即刷新编辑器预览"))
            ((MinimapRealtimeRenderer)target).RenderNow();
    }
}
#endif
