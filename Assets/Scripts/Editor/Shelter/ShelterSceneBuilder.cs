#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 一次性建立基地灰盒场景，并把 StartScene 的核心服务接入跨场景流程。
/// 生成结果全部是普通场景对象或预制体实例，后续可以直接在编辑器中移动、缩放和替换美术资源。
/// </summary>
[InitializeOnLoad]
public static class ShelterSceneBuilder
{
    private const string StartScenePath = "Assets/Scenes/StartScene.unity";
    private const string ShelterScenePath = "Assets/Scenes/ShelterScene.unity";
    private const string CorePrefabPath = "Assets/Prefabs/Core/PersistentGameCore.prefab";
    private const string MainBagPrefabPath = "Assets/Prefabs/Bag/InventoryUI_main_bag.prefab";
    private const string PocketPrefabPath = "Assets/Prefabs/Bag/InventoryUI_min_bag.prefab";
    private const string StashPrefabPath = "Assets/Prefabs/Bag/InventoryUI_shelter_stash.prefab";
    private const string MenuPath = "Tools/余烬之下/基地系统/重新构建基地灰盒场景";
    private const string SyncCoreMenuPath = "Tools/余烬之下/基地系统/同步跨场景核心";
    private const string SyncInventoryMenuPath = "Tools/余烬之下/基地系统/重置基地背包与仓库UI为预制体布局";
    private const string SyncStashPreviewMenuPath = "Tools/余烬之下/基地系统/同步仓库预制体的行列预览";
    private static bool building;

    static ShelterSceneBuilder()
    {
        EditorApplication.delayCall += BuildIfNeeded;
    }

    [MenuItem(MenuPath)]
    private static void RebuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog(
                "重新构建基地灰盒",
                "这会覆盖 ShelterScene，但不会删除或覆盖 StartScene 中的地图内容。是否继续？",
                "重新构建",
                "取消")) return;

        BuildAll(true);
    }

    [MenuItem(SyncCoreMenuPath)]
    private static void SyncCrossSceneCore()
    {
        BuildAll(false);
    }

    [MenuItem(SyncInventoryMenuPath)]
    private static void SyncShelterInventoryUi()
    {
        EnsureStashPrefabAsset();
        EnsureShelterInventoryUi(true);
        AssetDatabase.SaveAssets();
    }

    [MenuItem(SyncStashPreviewMenuPath)]
    private static void SyncStashPrefabPreviewMenu()
    {
        ReadConfiguredStashSize(out var rows, out var columns);
        RebuildStashPrefabPreview(rows, columns);
        AssetDatabase.SaveAssets();
        Debug.Log($"基地仓库预制体的编辑器预览已同步为 {rows}×{columns}。 ");
    }

    private static void BuildIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        var shelterMissing = AssetDatabase.LoadAssetAtPath<SceneAsset>(ShelterScenePath) == null;
        var corePrefabMissing = AssetDatabase.LoadAssetAtPath<GameObject>(CorePrefabPath) == null;
        var stashPrefabMissing = AssetDatabase.LoadAssetAtPath<GameObject>(StashPrefabPath) == null;
        var startNeedsSetup = !StartSceneContainsPersistentFlow();
        if (shelterMissing || corePrefabMissing || stashPrefabMissing || startNeedsSetup) BuildAll(false);
    }

    private static void BuildAll(bool forceShelterRebuild)
    {
        if (building) return;
        building = true;
        try
        {
            var stashPrefabCreated = EnsureStashPrefabAsset();
            EnsureStartSceneFlow();
            if (forceShelterRebuild || AssetDatabase.LoadAssetAtPath<SceneAsset>(ShelterScenePath) == null)
                BuildShelterScene();
            else
            {
                EnsureShelterCore();
                EnsureShelterInventoryUi(stashPrefabCreated);
            }
            EnsureBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("基地系统已建立：撤离可切换到 ShelterScene，基地可从出击点返回 StartScene。");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            building = false;
        }
    }

    private static bool StartSceneContainsPersistentFlow()
    {
        var loaded = SceneManager.GetSceneByPath(StartScenePath);
        if (loaded.IsValid() && loaded.isLoaded)
            return loaded.GetRootGameObjects().Any(root => root.GetComponent<PersistentGameServices>() != null);

        // 未加载时用 YAML 文本做轻量判断，避免每次脚本重载都打开用户场景。
        if (!System.IO.File.Exists(StartScenePath)) return false;
        var yaml = System.IO.File.ReadAllText(StartScenePath);
        return yaml.Contains("Core_GameServices") || yaml.Contains("Core_SceneFlow");
    }

    private static void EnsureStartSceneFlow()
    {
        var scene = SceneManager.GetSceneByPath(StartScenePath);
        var openedTemporarily = !scene.IsValid() || !scene.isLoaded;
        if (openedTemporarily) scene = EditorSceneManager.OpenScene(StartScenePath, OpenSceneMode.Additive);

        var flowRoot = FindRoot(scene, "Core_GameServices") ?? FindRoot(scene, "Core_SceneFlow");
        if (flowRoot == null)
        {
            flowRoot = new GameObject("Core_GameServices");
            SceneManager.MoveGameObjectToScene(flowRoot, scene);
        }
        flowRoot.name = "Core_GameServices";

        var persistence = flowRoot.GetComponent<PersistentGameServices>() ?? flowRoot.AddComponent<PersistentGameServices>();
        var flow = flowRoot.GetComponent<SceneFlowController>() ?? flowRoot.AddComponent<SceneFlowController>();
        flow.Configure("StartScene", "ShelterScene");

        // 旧场景把纯数据 GridInventoryService 放在 Canvas/GridSystems 下。
        // UI 随场景卸载而网格数据必须常驻，因此把它提升为独立根节点；现有序列化引用不会因此失效。
        var gridService = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<GridInventoryService>(true))
            .FirstOrDefault();
        if (gridService != null)
        {
            gridService.transform.SetParent(null, false);
            gridService.transform.localPosition = Vector3.zero;
            gridService.transform.localRotation = Quaternion.identity;
            gridService.transform.localScale = Vector3.one;
            if (gridService.transform is RectTransform gridRect)
                gridRect.anchoredPosition = Vector2.zero;
            gridService.gameObject.name = "Core_GridInventory";
        }

        var serviceNames = new[]
        {
            "Core_GameStateMachine",
            "Core_Survival",
            "Core_Inventory",
            "Core_GridInventory",
            "Core_LootTableDatabase",
            "Gameplay_Workbench",
            "Gameplay_Shelter",
            "Gameplay_NightAssault"
        };
        var roots = serviceNames
            .Select(name => FindNamedObject(scene, flowRoot.transform, name))
            .Where(root => root != null)
            .ToArray();

        // 核心服务归入同一个预制体层级。这样任意玩法场景都能预摆同一套核心，
        // 直接从该场景运行时也不依赖先打开另一张地图。
        for (var i = 0; i < roots.Length; i++)
            roots[i].transform.SetParent(flowRoot.transform, true);
        persistence.Configure(roots);

        EditorUtility.SetDirty(flowRoot);
        EditorUtility.SetDirty(persistence);
        EditorUtility.SetDirty(flow);
        EditorSceneManager.MarkSceneDirty(scene);

        EnsureAssetFolder("Assets/Prefabs/Core");
        if (PrefabUtility.IsPartOfPrefabInstance(flowRoot))
            PrefabUtility.ApplyPrefabInstance(flowRoot, InteractionMode.AutomatedAction);
        else
            PrefabUtility.SaveAsPrefabAssetAndConnect(flowRoot, CorePrefabPath, InteractionMode.AutomatedAction);
        EditorSceneManager.SaveScene(scene);
        if (openedTemporarily) EditorSceneManager.CloseScene(scene, true);
    }

    private static void BuildShelterScene()
    {
        var previousActive = SceneManager.GetActiveScene();
        var shelterWasActive = previousActive.IsValid() && previousActive.path == ShelterScenePath;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var oldShelter = SceneManager.GetSceneByPath(ShelterScenePath);
        if (oldShelter.IsValid() && oldShelter.isLoaded && oldShelter != scene)
            EditorSceneManager.CloseScene(oldShelter, true);
        SceneManager.SetActiveScene(scene);

        InstantiateCorePrefab(scene);

        var environment = new GameObject("Shelter_Environment");
        CreateArea("Floor", environment.transform, Vector2.zero, new Vector2(20f, 12f), new Color(0.23f, 0.20f, 0.16f), false);
        CreateWall(environment.transform, "Wall_Top", new Vector2(0f, 6f), new Vector2(20.5f, 0.5f));
        CreateWall(environment.transform, "Wall_Bottom", new Vector2(0f, -6f), new Vector2(20.5f, 0.5f));
        CreateWall(environment.transform, "Wall_Left", new Vector2(-10f, 0f), new Vector2(0.5f, 12f));
        CreateWall(environment.transform, "Wall_Right", new Vector2(10f, 0f), new Vector2(0.5f, 12f));

        var stations = new GameObject("Shelter_Stations");
        CreateStation(stations.transform, "Station_Storage", new Vector2(-6.5f, 2.5f), new Vector2(2.5f, 2f), new Color(0.27f, 0.47f, 0.62f), ShelterInteractionKind.Storage, "基地仓库");
        CreateStation(stations.transform, "Station_Workbench", new Vector2(-2.3f, 3.7f), new Vector2(2.5f, 1.5f), new Color(0.58f, 0.42f, 0.24f), ShelterInteractionKind.Workbench, "工作台");
        CreateStation(stations.transform, "Station_Drink", new Vector2(2.3f, 3.7f), new Vector2(1.8f, 1.5f), new Color(0.18f, 0.53f, 0.72f), ShelterInteractionKind.Drink, "饮水台");
        CreateStation(stations.transform, "Station_Rest", new Vector2(6.5f, 2.5f), new Vector2(2.8f, 1.8f), new Color(0.38f, 0.52f, 0.36f), ShelterInteractionKind.Rest, "休息区", 0.6f);
        CreateStation(stations.transform, "Station_Deploy", new Vector2(0f, -4.4f), new Vector2(3.2f, 1.4f), new Color(0.65f, 0.30f, 0.22f), ShelterInteractionKind.Deploy, "出击点", 1f);

        var spawnObject = new GameObject("ShelterSpawn_Return");
        spawnObject.transform.position = new Vector3(0f, -2.2f, 0f);
        var spawnPoint = spawnObject.AddComponent<MapSpawnPoint>();

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CameraFollow2D));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        var camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 6.5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.04f, 0.045f);

        var canvas = CreateCanvas();
        var mainUi = InstantiateInventoryUi(MainBagPrefabPath, scene, canvas.transform, "InventoryUI_main_bag");
        var pocketUi = InstantiateInventoryUi(PocketPrefabPath, scene, canvas.transform, "InventoryUI_min_bag");
        var stashUi = InstantiateInventoryUi(StashPrefabPath, scene, canvas.transform, "InventoryUI_shelter_stash");
        var promptUi = CreateInteractionPrompt(canvas.transform);
        CreateSurvivalHud(canvas.transform);
        CreateShelterLegend(canvas.transform);

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        var controllerObject = new GameObject("Shelter_SceneController");
        var controller = controllerObject.AddComponent<ShelterSceneController>();
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/player.prefab");
        controller.Configure(
            playerPrefab != null ? playerPrefab.GetComponent<PlayerCombatController2D>() : null,
            spawnPoint,
            mainUi,
            pocketUi,
            stashUi,
            promptUi);

        EditorSceneManager.SaveScene(scene, ShelterScenePath);
        if (shelterWasActive)
        {
            SceneManager.SetActiveScene(scene);
        }
        else
        {
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>为已有基地场景补上核心预制体，不重建也不覆盖用户调整过的基地内容。</summary>
    private static void EnsureShelterCore()
    {
        var scene = SceneManager.GetSceneByPath(ShelterScenePath);
        var openedTemporarily = !scene.IsValid() || !scene.isLoaded;
        if (openedTemporarily) scene = EditorSceneManager.OpenScene(ShelterScenePath, OpenSceneMode.Additive);

        if (!scene.GetRootGameObjects().Any(root => root.GetComponent<PersistentGameServices>() != null))
        {
            InstantiateCorePrefab(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (openedTemporarily) EditorSceneManager.CloseScene(scene, true);
    }

    private static void InstantiateCorePrefab(Scene scene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CorePrefabPath);
        if (prefab == null)
            throw new InvalidOperationException($"找不到核心预制体：{CorePrefabPath}");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "Core_GameServices";
    }

    /// <summary>
    /// 从口袋 UI 派生独立仓库预制体。两者可以共享代码与基础风格，但不再共享尺寸和位置配置。
    /// </summary>
    private static bool EnsureStashPrefabAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(StashPrefabPath) != null) return false;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PocketPrefabPath) == null)
            throw new InvalidOperationException($"找不到口袋预制体：{PocketPrefabPath}");
        if (!AssetDatabase.CopyAsset(PocketPrefabPath, StashPrefabPath))
            throw new InvalidOperationException($"无法创建基地仓库预制体：{StashPrefabPath}");

        var root = PrefabUtility.LoadPrefabContents(StashPrefabPath);
        try
        {
            root.name = "InventoryUI_shelter_stash";
            if (root.transform is RectTransform rect)
                rect.anchoredPosition = new Vector2(-670f, -18f);

            var view = root.GetComponent<GridInventoryUnityUI>();
            if (view == null) throw new InvalidOperationException("仓库预制体缺少 GridInventoryUnityUI。 ");
            var serialized = new SerializedObject(view);
            serialized.FindProperty("currentContainerId").stringValue = "shelter_stash";
            serialized.FindProperty("cellSize").floatValue = 30f;
            serialized.FindProperty("cellSpacing").floatValue = 3f;
            serialized.FindProperty("autoResizePanelBackground").boolValue = true;
            serialized.FindProperty("autoResizeManualGridToContainer").boolValue = true;
            serialized.FindProperty("minToolbarWidth").floatValue = 420f;
            serialized.FindProperty("showContainerToolbar").boolValue = false;
            serialized.FindProperty("showSpawnTools").boolValue = false;
            serialized.FindProperty("showActionToolbar").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            RebuildStashPrefabPreview(root, 12, 16);

            PrefabUtility.SaveAsPrefabAsset(root, StashPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return true;
    }

    /// <summary>把仓库预制体中的 Cell_* 实体节点重建成配置的行列数，Prefab Mode 中也能直接看到最终网格。</summary>
    private static void RebuildStashPrefabPreview(int rows, int columns)
    {
        rows = Mathf.Max(1, rows);
        columns = Mathf.Max(1, columns);
        var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
        var usesOpenStage = currentStage != null && currentStage.assetPath == StashPrefabPath;
        var root = usesOpenStage
            ? currentStage.prefabContentsRoot
            : PrefabUtility.LoadPrefabContents(StashPrefabPath);
        try
        {
            RebuildStashPrefabPreview(root, rows, columns);
            if (usesOpenStage)
            {
                // Prefab Mode 中的 root 属于预览场景，不是可传给 SavePrefabAsset 的资源对象。
                // 标记场景为 Dirty，Unity 的 Prefab Stage 保存流程（Ctrl+S）会正确写回资源。
                EditorSceneManager.MarkSceneDirty(currentStage.scene);
            }
            else
            {
                PrefabUtility.SaveAsPrefabAsset(root, StashPrefabPath);
            }
        }
        finally
        {
            if (!usesOpenStage) PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void RebuildStashPrefabPreview(GameObject root, int rows, int columns)
    {
        if (root == null) throw new InvalidOperationException("无法打开基地仓库预制体。 ");
        var view = root.GetComponent<GridInventoryUnityUI>();
        if (view == null) throw new InvalidOperationException("基地仓库预制体缺少 GridInventoryUnityUI。 ");

        var serialized = new SerializedObject(view);
        var cellSize = Mathf.Max(1f, serialized.FindProperty("cellSize").floatValue);
        var cellSpacing = Mathf.Max(0f, serialized.FindProperty("cellSpacing").floatValue);
        var emptyCellColor = serialized.FindProperty("emptyCellColor").colorValue;
        var lockedCellColor = serialized.FindProperty("lockedCellColor")?.colorValue ?? new Color(0.055f, 0.055f, 0.065f, 0.98f);
        var gridRoot = root.transform.Find("GridRoot") as RectTransform;
        var cellLayer = root.transform.Find("CellLayer") as RectTransform;
        var itemLayer = root.transform.Find("ItemLayer") as RectTransform;
        var ghostLayer = root.transform.Find("GhostLayer") as RectTransform;
        var panelRect = root.transform as RectTransform;
        if (gridRoot == null || cellLayer == null || itemLayer == null || ghostLayer == null || panelRect == null)
            throw new InvalidOperationException("基地仓库预制体的 GridRoot/CellLayer/ItemLayer/GhostLayer 引用不完整。 ");

        var previousGridSize = gridRoot.sizeDelta;
        var horizontalChrome = Mathf.Max(0f, panelRect.sizeDelta.x - previousGridSize.x);
        var verticalChrome = Mathf.Max(0f, panelRect.sizeDelta.y - previousGridSize.y);
        var contentSize = new Vector2(
            columns * cellSize + Mathf.Max(0, columns - 1) * cellSpacing,
            rows * cellSize + Mathf.Max(0, rows - 1) * cellSpacing);

        gridRoot.sizeDelta = contentSize;
        SyncPreviewLayer(cellLayer, gridRoot, contentSize);
        SyncPreviewLayer(itemLayer, gridRoot, contentSize);
        SyncPreviewLayer(ghostLayer, gridRoot, contentSize);
        panelRect.sizeDelta = new Vector2(contentSize.x + horizontalChrome, contentSize.y + verticalChrome);

        for (var i = cellLayer.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(cellLayer.GetChild(i).gameObject);
        for (var i = itemLayer.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(itemLayer.GetChild(i).gameObject);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var cell = new GameObject($"Cell_{row}_{column}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                cell.layer = root.layer;
                var rect = cell.GetComponent<RectTransform>();
                rect.SetParent(cellLayer, false);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(column * (cellSize + cellSpacing), -row * (cellSize + cellSpacing));
                rect.sizeDelta = new Vector2(cellSize, cellSize);
                // 仓库默认开放前 96 格；预制体模式直接显示锁格效果，便于美术调整。
                cell.GetComponent<Image>().color = row * columns + column < 96 ? emptyCellColor : lockedCellColor;
            }
        }

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(view);
    }

    private static void SyncPreviewLayer(RectTransform layer, RectTransform gridRoot, Vector2 size)
    {
        layer.anchorMin = gridRoot.anchorMin;
        layer.anchorMax = gridRoot.anchorMax;
        layer.pivot = gridRoot.pivot;
        layer.anchoredPosition = gridRoot.anchoredPosition;
        layer.sizeDelta = size;
    }

    private static void ReadConfiguredStashSize(out int rows, out int columns)
    {
        rows = 12;
        columns = 16;
        if (!System.IO.File.Exists(ShelterScenePath)) return;
        var yaml = System.IO.File.ReadAllText(ShelterScenePath);
        var rowMatch = System.Text.RegularExpressions.Regex.Match(yaml, @"\bstashRows:\s*(\d+)");
        var columnMatch = System.Text.RegularExpressions.Regex.Match(yaml, @"\bstashColumns:\s*(\d+)");
        if (rowMatch.Success && int.TryParse(rowMatch.Groups[1].Value, out var parsedRows)) rows = Mathf.Max(1, parsedRows);
        if (columnMatch.Success && int.TryParse(columnMatch.Groups[1].Value, out var parsedColumns)) columns = Mathf.Max(1, parsedColumns);
    }

    /// <summary>
    /// 为已有基地场景补齐主背包、口袋和仓库三个正式预制体实例。
    /// resetExisting 仅由首次迁移或显式菜单触发，用于清除早期构建脚本造成的错误 RectTransform Overrides。
    /// </summary>
    private static void EnsureShelterInventoryUi(bool resetExisting)
    {
        var scene = SceneManager.GetSceneByPath(ShelterScenePath);
        var openedTemporarily = !scene.IsValid() || !scene.isLoaded;
        if (openedTemporarily) scene = EditorSceneManager.OpenScene(ShelterScenePath, OpenSceneMode.Additive);

        var canvas = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
            .FirstOrDefault(item => item.transform.parent == null);
        if (canvas == null) throw new InvalidOperationException("ShelterScene 中找不到主 Canvas。 ");

        var views = canvas.GetComponentsInChildren<GridInventoryUnityUI>(true).ToList();
        if (resetExisting)
        {
            for (var i = views.Count - 1; i >= 0; i--)
            {
                var viewName = views[i].name;
                if (viewName == "InventoryUI_main_bag" ||
                    viewName == "InventoryUI_min_bag" ||
                    viewName == "InventoryUI_shelter_stash")
                    UnityEngine.Object.DestroyImmediate(views[i].gameObject);
            }
            views.Clear();
        }

        var mainUi = views.FirstOrDefault(view => view.name == "InventoryUI_main_bag") ??
                     InstantiateInventoryUi(MainBagPrefabPath, scene, canvas.transform, "InventoryUI_main_bag");
        var pocketUi = views.FirstOrDefault(view => view.name == "InventoryUI_min_bag") ??
                       InstantiateInventoryUi(PocketPrefabPath, scene, canvas.transform, "InventoryUI_min_bag");
        var stashUi = views.FirstOrDefault(view => view.name == "InventoryUI_shelter_stash");
        if (stashUi != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(stashUi.gameObject) != StashPrefabPath)
        {
            UnityEngine.Object.DestroyImmediate(stashUi.gameObject);
            stashUi = null;
        }
        stashUi ??= InstantiateInventoryUi(StashPrefabPath, scene, canvas.transform, "InventoryUI_shelter_stash");

        var controller = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ShelterSceneController>(true))
            .FirstOrDefault();
        var spawnPoint = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<MapSpawnPoint>(true))
            .FirstOrDefault(point => point.name == "ShelterSpawn_Return");
        var promptUi = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<WorldInteractionPromptUI>(true))
            .FirstOrDefault();
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/player.prefab");
        controller?.Configure(
            playerPrefab != null ? playerPrefab.GetComponent<PlayerCombatController2D>() : null,
            spawnPoint,
            mainUi,
            pocketUi,
            stashUi,
            promptUi);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedTemporarily) EditorSceneManager.CloseScene(scene, true);
    }

    private static void EnsureAssetFolder(string path)
    {
        var parts = path.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        return scene.IsValid() && scene.isLoaded
            ? scene.GetRootGameObjects().FirstOrDefault(root => root.name == name)
            : null;
    }

    private static GameObject FindNamedObject(Scene scene, Transform coreRoot, string name)
    {
        var root = FindRoot(scene, name);
        if (root != null) return root;
        if (coreRoot == null) return null;

        for (var i = 0; i < coreRoot.childCount; i++)
        {
            var child = coreRoot.GetChild(i);
            if (child.name == name) return child.gameObject;
        }
        return null;
    }

    private static GameObject CreateArea(string name, Transform parent, Vector2 position, Vector2 size, Color color, bool collider)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(position.x, position.y, 0f);
        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        // 按 Sprite 实际 Bounds 换算 Transform 缩放，既得到准确世界尺寸，也不触发九宫格 Full Rect 警告。
        renderer.drawMode = SpriteDrawMode.Simple;
        var spriteSize = renderer.sprite != null ? (Vector2)renderer.sprite.bounds.size : Vector2.one;
        go.transform.localScale = new Vector3(
            size.x / Mathf.Max(0.0001f, spriteSize.x),
            size.y / Mathf.Max(0.0001f, spriteSize.y),
            1f);
        renderer.color = color;
        renderer.sortingOrder = -10;
        if (collider) go.AddComponent<BoxCollider2D>();
        return go;
    }

    private static void CreateWall(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var wall = CreateArea(name, parent, position, size, new Color(0.12f, 0.13f, 0.14f), true);
        var wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer >= 0) wall.layer = wallLayer;
    }

    private static void CreateStation(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size,
        Color color,
        ShelterInteractionKind kind,
        string displayName,
        float duration = 0f)
    {
        var station = CreateArea(name, parent, position, size, color, false);
        station.GetComponent<SpriteRenderer>().sortingOrder = 0;
        var collider = station.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        station.AddComponent<ShelterInteractionPoint>().Configure(kind, displayName, duration);
    }

    private static Canvas CreateCanvas()
    {
        var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static GridInventoryUnityUI InstantiateInventoryUi(string path, Scene scene, Transform parent, string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return null;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = name;
        instance.transform.SetParent(parent, false);
        return instance.GetComponent<GridInventoryUnityUI>();
    }

    private static WorldInteractionPromptUI CreateInteractionPrompt(Transform parent)
    {
        var root = CreateUiObject("WorldInteractionPromptUI", parent, typeof(Image), typeof(CanvasGroup), typeof(Canvas));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, 34f);
        rootRect.sizeDelta = new Vector2(560f, 92f);
        root.GetComponent<Image>().color = new Color(0.035f, 0.04f, 0.045f, 0.9f);

        var prompt = CreateText("Prompt", root.transform, "[F] 打开 基地仓库", 20, TextAnchor.MiddleCenter);
        SetRect(prompt.rectTransform, new Vector2(10f, -8f), new Vector2(540f, 34f));

        var progressRoot = CreateUiObject("Progress", root.transform, typeof(Image));
        SetRect((RectTransform)progressRoot.transform, new Vector2(30f, -48f), new Vector2(500f, 24f));
        progressRoot.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.10f, 1f);
        var fillObject = CreateUiObject("Fill", progressRoot.transform, typeof(Image));
        var fillRect = (RectTransform)fillObject.transform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);
        var fill = fillObject.GetComponent<Image>();
        fill.color = new Color(0.20f, 0.68f, 0.88f, 1f);
        var progressText = CreateText("ProgressText", progressRoot.transform, "交互进度 0%", 13, TextAnchor.MiddleCenter);
        Stretch(progressText.rectTransform);

        var feedback = CreateText("Feedback", root.transform, "操作完成", 18, TextAnchor.MiddleCenter);
        SetRect(feedback.rectTransform, new Vector2(10f, -20f), new Vector2(540f, 42f));

        var ui = root.AddComponent<WorldInteractionPromptUI>();
        ui.Configure(root.GetComponent<Canvas>(), root.GetComponent<CanvasGroup>(), prompt, progressRoot, fill, progressText, feedback);
        return ui;
    }

    private static void CreateSurvivalHud(Transform parent)
    {
        var root = CreateUiObject("PlayerHUD", parent, typeof(Image), typeof(CanvasGroup));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = new Vector2(24f, -24f);
        rootRect.sizeDelta = new Vector2(304f, 206f);
        root.GetComponent<Image>().color = new Color(0.045f, 0.055f, 0.06f, 0.9f);
        root.GetComponent<CanvasGroup>().blocksRaycasts = false;

        var title = CreateText("Title", root.transform, "基地状态", 16, TextAnchor.MiddleLeft);
        SetRect(title.rectTransform, new Vector2(12f, -8f), new Vector2(280f, 26f));
        var hp = CreateStatRow(root.transform, "HP", "生命", 42f, new Color(0.74f, 0.18f, 0.16f));
        var hunger = CreateStatRow(root.transform, "Hunger", "饥饿", 76f, new Color(0.83f, 0.53f, 0.15f));
        var thirst = CreateStatRow(root.transform, "Thirst", "口渴", 110f, new Color(0.18f, 0.55f, 0.78f));
        var fatigue = CreateStatRow(root.transform, "Fatigue", "精力", 144f, new Color(0.43f, 0.68f, 0.37f));
        var infection = CreateText("InfectionStatus", root.transform, "状态正常", 13, TextAnchor.MiddleLeft);
        SetRect(infection.rectTransform, new Vector2(12f, -178f), new Vector2(280f, 20f));
        root.AddComponent<PlayerSurvivalHUD>().Configure(
            hp.fill, hp.value, hunger.fill, hunger.value,
            thirst.fill, thirst.value, fatigue.fill, fatigue.value, infection);
    }

    private static (Image fill, Text value) CreateStatRow(Transform parent, string name, string label, float top, Color color)
    {
        var row = CreateUiObject(name, parent);
        SetRect((RectTransform)row.transform, new Vector2(12f, -top), new Vector2(280f, 26f));
        var labelText = CreateText("Label", row.transform, label, 13, TextAnchor.MiddleLeft);
        SetRect(labelText.rectTransform, Vector2.zero, new Vector2(54f, 26f));
        var bar = CreateUiObject("Bar", row.transform, typeof(Image));
        SetRect((RectTransform)bar.transform, new Vector2(56f, -4f), new Vector2(224f, 18f));
        bar.GetComponent<Image>().color = new Color(0.12f, 0.13f, 0.14f, 0.95f);
        var fillObject = CreateUiObject("Fill", bar.transform, typeof(Image));
        Stretch((RectTransform)fillObject.transform);
        var fill = fillObject.GetComponent<Image>();
        fill.color = color;
        var value = CreateText("Value", bar.transform, "100 / 100", 11, TextAnchor.MiddleCenter);
        Stretch(value.rectTransform);
        return (fill, value);
    }

    private static void CreateShelterLegend(Transform parent)
    {
        var text = CreateText(
            "ShelterLegend",
            parent,
            "基地灰盒  |  蓝：仓库  棕：工作台  青：饮水  绿：休息  红：出击",
            16,
            TextAnchor.MiddleCenter);
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -18f);
        rect.sizeDelta = new Vector2(900f, 34f);
    }

    private static GameObject CreateUiObject(string name, Transform parent, params Type[] extraComponents)
    {
        var types = new List<Type> { typeof(RectTransform), typeof(CanvasRenderer) };
        types.AddRange(extraComponents);
        var go = new GameObject(name, types.ToArray());
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Text CreateText(string name, Transform parent, string value, int size, TextAnchor alignment)
    {
        var go = CreateUiObject(name, parent, typeof(Text));
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 topLeft, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeft;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void EnsureBuildSettings()
    {
        // 正式游戏从基地开始；其余已配置场景保持原有相对顺序。
        var paths = EditorBuildSettings.scenes
            .Select(scene => scene.path)
            .Where(path => path != ShelterScenePath && path != StartScenePath)
            .ToList();
        paths.Insert(0, StartScenePath);
        paths.Insert(0, ShelterScenePath);
        EditorBuildSettings.scenes = paths
            .Distinct()
            .Select(path => new EditorBuildSettingsScene(path, true))
            .ToArray();
    }

}
#endif
