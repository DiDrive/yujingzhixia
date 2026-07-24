using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class MapTilemapTools
{
    private const string GridName = "Grid";
    private const string MarkersRootName = "MapMarkers";
    private const string FloorRootPrefix = "Floor_";
    private const int FirstFloorIndex = 0;
    private const int DefaultFloorCount = 3;
    private const string SortingLayerMapBack = "MapBack";
    private const string SortingLayerActor = "Actor";
    private const string SortingLayerMapFront = "MapFront";
    private const string WallPhysicsLayerName = "Wall";
    private const GridLayout.CellLayout TargetCellLayout = GridLayout.CellLayout.IsometricZAsY;
    private const TilemapRenderer.SortOrder TargetSortOrder = TilemapRenderer.SortOrder.TopRight;
    private static readonly Vector3 TargetCellSize = new(1f, 0.5f, 1f);
    private static readonly Regex FloorSuffixRegex = new(@"_F\d+$");
    private static readonly LegacyLayerRenameDefinition[] LegacyLayerRenames =
    {
        new("Ground", "GroundBase"),
        new("Deco", "LowDeco"),
        new("Wall", "WallBack")
    };

    private static readonly TilemapLayerDefinition[] LayerDefinitions =
    {
        new("GroundBase", SortingLayerMapBack, 0, false, false),
        new("GroundDetail", SortingLayerMapBack, 10, false, false),
        new("LowDeco", SortingLayerMapBack, 20, false, false),
        new("WallBack", SortingLayerMapBack, 30, false, false),
        new("Collision", SortingLayerMapBack, 40, true, true),
        new("WallFront", SortingLayerMapFront, 50, false, false),
        new("RoofOrCover", SortingLayerMapFront, 60, false, false),
        new("Foreground", SortingLayerMapFront, 70, false, false),
        new("Interactable", SortingLayerMapFront, 80, false, false)
    };

    [MenuItem("Tools/Map/Setup Map (Rendering + Collision)")]
    public static void SetupMap()
    {
        var grid = FindOrCreateGrid();
        var targetFloorIndices = GetTargetFloorIndices(grid);
        var floorLayerDefinitions = BuildFloorLayerDefinitions(grid);
        EnsureSortingLayersExist(targetFloorIndices, floorLayerDefinitions);
        var renameLogs = new List<string>();
        if (HasFloorGroups(grid))
        {
            EnsureFloorTilemapGroups(grid, targetFloorIndices, floorLayerDefinitions);
            EnsureCollisionComponents(grid, targetFloorIndices, floorLayerDefinitions);
        }
        else
        {
            TryAutoUpgradeLegacyLayers(grid, renameLogs);
            EnsureStandardTilemapLayers(grid);
            EnsureCollisionComponents(grid);
        }

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Selection.activeGameObject = grid.gameObject;
        Debug.Log("Map setup finished.\n" + string.Join("\n", renameLogs));
    }

    /// <summary>
    /// 创建或补齐当前场景的标准瓦片地图层级。
    /// 会确保场景里存在 Grid，并将其配置为 Isometric Z as Y，
    /// 然后在 Grid 下创建适合室内地图的标准 Tilemap 层：
    /// GroundBase、GroundDetail、LowDeco、WallBack、Collision、WallFront、RoofOrCover、Foreground、Interactable。
    /// 已存在的同名层不会重复创建，只会补齐缺失的 Tilemap / TilemapRenderer，并设置建议的排序顺序。
    /// </summary>
    [MenuItem("Tools/Map/Create Standard Tilemap Layers")]
    public static void CreateStandardTilemapLayers()
    {
        var grid = FindOrCreateGrid();
        var targetFloorIndices = GetTargetFloorIndices(grid);
        var floorLayerDefinitions = BuildFloorLayerDefinitions(grid);
        EnsureSortingLayersExist(targetFloorIndices, floorLayerDefinitions);
        var renameLogs = new List<string>();
        if (HasFloorGroups(grid))
        {
            EnsureFloorTilemapGroups(grid, targetFloorIndices, floorLayerDefinitions);
            EnsureCollisionComponents(grid, targetFloorIndices, floorLayerDefinitions);
        }
        else
        {
            TryAutoUpgradeLegacyLayers(grid, renameLogs);
            EnsureStandardTilemapLayers(grid);
            EnsureCollisionComponents(grid);
        }

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Selection.activeGameObject = grid.gameObject;
        Debug.Log("Standard indoor tilemap layers are ready.\n" + string.Join("\n", renameLogs));
    }

    /// <summary>
    /// 将旧版地图层级升级到当前的室内地图层级结构。
    /// 当前会尝试把 Ground 重命名为 GroundBase、Deco 重命名为 LowDeco、Wall 重命名为 WallBack，
    /// 然后自动补齐 GroundDetail、Collision、WallFront、RoofOrCover、Foreground、Interactable 等缺失层。
    /// 同时会把 Grid 配置为 Isometric Z as Y，便于等距瓦片正确对位。
    /// 不会删除任何已有层，避免误删已经画好的 Tile。
    /// </summary>
    [MenuItem("Tools/Map/Upgrade Legacy Tilemap Layers")]
    public static void UpgradeLegacyTilemapLayers()
    {
        var grid = FindOrCreateGrid();
        var targetFloorIndices = GetTargetFloorIndices(grid);
        var floorLayerDefinitions = BuildFloorLayerDefinitions(grid);
        EnsureSortingLayersExist(targetFloorIndices, floorLayerDefinitions);
        var logs = new List<string>();
        if (HasFloorGroups(grid))
        {
            EnsureFloorTilemapGroups(grid, targetFloorIndices, floorLayerDefinitions);
            EnsureCollisionComponents(grid, targetFloorIndices, floorLayerDefinitions);
        }
        else
        {
            TryAutoUpgradeLegacyLayers(grid, logs);
            EnsureStandardTilemapLayers(grid);
            EnsureCollisionComponents(grid);
        }

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Selection.activeGameObject = grid.gameObject;
        Debug.Log("Legacy tilemap upgrade finished.\n" + string.Join("\n", logs));
    }

    /// <summary>
    /// 给需要阻挡移动的瓦片层补齐 2D 碰撞组件。
    /// 当前只处理 Collision 层：添加 Static Rigidbody2D、TilemapCollider2D、CompositeCollider2D。
    /// CompositeCollider2D 用于合并相邻格子的碰撞边界，减少角色贴墙抖动和碰撞体数量。
    /// </summary>
    [MenuItem("Tools/Map/Add Collision Components")]
    public static void AddCollisionComponents()
    {
        var grid = FindOrCreateGrid();
        var targetFloorIndices = GetTargetFloorIndices(grid);
        var floorLayerDefinitions = BuildFloorLayerDefinitions(grid);
        EnsureSortingLayersExist(targetFloorIndices, floorLayerDefinitions);

        if (HasFloorGroups(grid))
        {
            EnsureCollisionComponents(grid, targetFloorIndices, floorLayerDefinitions);
        }
        else
        {
            EnsureCollisionComponents(grid);
        }

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Debug.Log("Collision components are ready on Collision tilemap.");
    }

    /// <summary>
    /// 按当前场景已有的楼层模板补齐多楼层 Tilemap 组。
    /// 优先读取 Floor_0（不存在则读取任意 Floor_x）作为模板，
    /// 复制其子层命名、排序层、顺序、碰撞配置到其他楼层。
    /// 适合已经手工搭好某一层后，用它批量生成其他楼层。
    /// </summary>
    [MenuItem("Tools/Map/Create Missing Floor Tilemap Groups")]
    public static void CreateMissingFloorTilemapGroups()
    {
        var grid = FindOrCreateGrid();
        var targetFloorIndices = GetTargetFloorIndices(grid);
        var floorLayerDefinitions = BuildFloorLayerDefinitions(grid);
        EnsureSortingLayersExist(targetFloorIndices, floorLayerDefinitions);
        EnsureFloorTilemapGroups(grid, targetFloorIndices, floorLayerDefinitions);
        EnsureCollisionComponents(grid, targetFloorIndices, floorLayerDefinitions);

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Selection.activeGameObject = grid.gameObject;
        Debug.Log("Missing floor tilemap groups are ready.");
    }

    [MenuItem("Tools/Map/Show Collision Layer")]
    public static void ShowCollisionLayer()
    {
        ToggleCollisionLayerVisibility(true);
    }

    [MenuItem("Tools/Map/Hide Collision Layer")]
    public static void HideCollisionLayer()
    {
        ToggleCollisionLayerVisibility(false);
    }

    /// <summary>
    /// 创建地图玩法点位的统一根节点和分组。
    /// 会创建 MapMarkers，以及 PlayerSpawnPoints、EnemySpawnPoints、LootPoints、ExtractionPoints、
    /// ShelterEntrances、RegionTriggers 等子分组，方便策划在场景里摆出生点、物资点、撤离点和区域触发器。
    /// </summary>
    [MenuItem("Tools/Map/Create Map Markers Root")]
    public static void CreateMapMarkersRoot()
    {
        var root = GameObject.Find(MarkersRootName);
        if (root == null)
        {
            root = new GameObject(MarkersRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Map Markers Root");
        }

        EnsureMarkerGroup(root.transform, "PlayerSpawnPoints");
        EnsureMarkerGroup(root.transform, "EnemySpawnPoints");
        EnsureMarkerGroup(root.transform, "LootPoints");
        EnsureMarkerGroup(root.transform, "ExtractionPoints");
        EnsureMarkerGroup(root.transform, "ShelterEntrances");
        EnsureMarkerGroup(root.transform, "RegionTriggers");

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("Map marker groups are ready.");
    }

    /// <summary>
    /// 校验当前场景是否具备基础地图制作条件。
    /// 会检查 Grid、标准 Tilemap 层、碰撞组件、MapMarkers 根节点，以及是否至少存在一个玩家出生点。
    /// 检查结果会输出到 Console，用于进游戏前快速发现漏配项。
    /// </summary>
    [MenuItem("Tools/Map/Validate Current Map")]
    public static void ValidateCurrentMap()
    {
        var warnings = new List<string>();
        var grid = GameObject.Find(GridName)?.GetComponent<Grid>();
        if (grid == null)
        {
            warnings.Add("Missing Grid object.");
        }
        else
        {
            if (grid.cellLayout != TargetCellLayout)
            {
                warnings.Add($"Grid cellLayout is {grid.cellLayout}, expected {TargetCellLayout}.");
            }

            if (HasFloorGroups(grid))
            {
                var targetFloorIndices = GetTargetFloorIndices(grid);
                var floorLayerDefinitions = BuildFloorLayerDefinitions(grid);
                EnsureSortingLayersExist(targetFloorIndices, floorLayerDefinitions);

                for (var floorListIndex = 0; floorListIndex < targetFloorIndices.Count; floorListIndex++)
                {
                    var floorIndex = targetFloorIndices[floorListIndex];
                    var floorRoot = FindFloorRoot(grid.transform, floorIndex);
                    if (floorRoot == null)
                    {
                        warnings.Add($"Missing floor group: {GetFloorRootName(floorIndex)}.");
                        continue;
                    }

                    for (var definitionIndex = 0; definitionIndex < floorLayerDefinitions.Count; definitionIndex++)
                    {
                        var definition = floorLayerDefinitions[definitionIndex];
                        var layer = FindDirectChild(floorRoot.transform, definition.Name);
                        if (layer == null)
                        {
                            warnings.Add($"Missing tilemap layer: {GetFloorRootName(floorIndex)}/{definition.Name}.");
                            continue;
                        }

                        if (layer.GetComponent<Tilemap>() == null) warnings.Add($"{GetFloorRootName(floorIndex)}/{definition.Name} is missing Tilemap.");
                        var renderer = layer.GetComponent<TilemapRenderer>();
                        if (renderer == null)
                        {
                            warnings.Add($"{GetFloorRootName(floorIndex)}/{definition.Name} is missing TilemapRenderer.");
                        }
                        else
                        {
                            var expectedSortingLayerName = ResolveFloorScopedName(definition.SortingLayerNamePattern, floorIndex);
                            if (!string.IsNullOrEmpty(expectedSortingLayerName) && renderer.sortingLayerName != expectedSortingLayerName)
                            {
                                warnings.Add($"{GetFloorRootName(floorIndex)}/{definition.Name} sorting layer is {renderer.sortingLayerName}, expected {expectedSortingLayerName}.");
                            }
                        }

                        if (!definition.NeedsCollider) continue;

                        var expectedPhysicsLayerName = ResolveFloorScopedName(definition.PhysicsLayerNamePattern, floorIndex);
                        var expectedPhysicsLayer = LayerMask.NameToLayer(expectedPhysicsLayerName);
                        if (expectedPhysicsLayer >= 0 && layer.layer != expectedPhysicsLayer)
                        {
                            warnings.Add($"{GetFloorRootName(floorIndex)}/{definition.Name} GameObject layer is {LayerMask.LayerToName(layer.layer)}, expected {expectedPhysicsLayerName}.");
                        }

                        if (layer.GetComponent<TilemapCollider2D>() == null) warnings.Add($"{GetFloorRootName(floorIndex)}/{definition.Name} is missing TilemapCollider2D.");
                        if (layer.GetComponent<CompositeCollider2D>() == null) warnings.Add($"{GetFloorRootName(floorIndex)}/{definition.Name} is missing CompositeCollider2D.");
                        if (layer.GetComponent<Rigidbody2D>() == null) warnings.Add($"{GetFloorRootName(floorIndex)}/{definition.Name} is missing Rigidbody2D.");
                    }
                }
            }
            else
            {
                EnsureSortingLayersExist();
                foreach (var definition in LayerDefinitions)
                {
                    var layer = FindDirectChild(grid.transform, definition.Name);
                    if (layer == null)
                    {
                        warnings.Add($"Missing tilemap layer: {definition.Name}.");
                        continue;
                    }

                    if (layer.GetComponent<Tilemap>() == null) warnings.Add($"{definition.Name} is missing Tilemap.");
                    var renderer = layer.GetComponent<TilemapRenderer>();
                    if (renderer == null)
                    {
                        warnings.Add($"{definition.Name} is missing TilemapRenderer.");
                    }
                    else if (!string.IsNullOrEmpty(definition.SortingLayerName) && renderer.sortingLayerName != definition.SortingLayerName)
                    {
                        warnings.Add($"{definition.Name} sorting layer is {renderer.sortingLayerName}, expected {definition.SortingLayerName}.");
                    }

                    if (definition.NeedsCollider)
                    {
                        var wallLayer = LayerMask.NameToLayer(WallPhysicsLayerName);
                        if (wallLayer >= 0 && layer.layer != wallLayer)
                        {
                            warnings.Add($"{definition.Name} GameObject layer is {LayerMask.LayerToName(layer.layer)}, expected {WallPhysicsLayerName}.");
                        }

                        if (layer.GetComponent<TilemapCollider2D>() == null) warnings.Add($"{definition.Name} is missing TilemapCollider2D.");
                        if (layer.GetComponent<CompositeCollider2D>() == null) warnings.Add($"{definition.Name} is missing CompositeCollider2D.");
                        if (layer.GetComponent<Rigidbody2D>() == null) warnings.Add($"{definition.Name} is missing Rigidbody2D.");

                        var tilemap = layer.GetComponent<Tilemap>();
                        if (tilemap != null && !HasAnyTile(tilemap))
                        {
                            warnings.Add($"{definition.Name} has no tiles. Paint blocking tiles here (then you can hide this layer).");
                        }
                    }
                }
            }
        }

        if (GameObject.Find(MarkersRootName) == null) warnings.Add("Missing MapMarkers root.");

        var spawnPoints = Object.FindObjectsOfType<MapSpawnPoint>();
        var hasPlayerSpawn = false;
        for (var i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i].SpawnKind != MapSpawnKind.Player) continue;
            hasPlayerSpawn = true;
            break;
        }

        if (!hasPlayerSpawn) warnings.Add("Missing player MapSpawnPoint.");

        var extractionPoints = Object.FindObjectsOfType<ExtractionPoint>();
        if (extractionPoints.Length == 0) warnings.Add("Missing ExtractionPoint.");

        if (warnings.Count == 0)
        {
            Debug.Log("Map validation passed.");
            return;
        }

        Debug.LogWarning("Map validation found issues:\n" + string.Join("\n", warnings));
    }

    private static Grid FindOrCreateGrid()
    {
        var grid = GameObject.Find(GridName)?.GetComponent<Grid>();
        if (grid != null)
        {
            ConfigureGrid(grid);
            return grid;
        }

        var gridObject = new GameObject(GridName);
        Undo.RegisterCreatedObjectUndo(gridObject, "Create Grid");
        grid = gridObject.AddComponent<Grid>();
        ConfigureGrid(grid);
        return grid;
    }

    private static void EnsureFloorTilemapGroups(Grid grid, IReadOnlyList<int> floorIndices, IReadOnlyList<FloorTilemapLayerDefinition> definitions)
    {
        ConfigureGrid(grid);

        for (var floorListIndex = 0; floorListIndex < floorIndices.Count; floorListIndex++)
        {
            var floorIndex = floorIndices[floorListIndex];
            var floorRoot = FindFloorRoot(grid.transform, floorIndex);
            if (floorRoot == null)
            {
                floorRoot = new GameObject(GetFloorRootName(floorIndex));
                Undo.RegisterCreatedObjectUndo(floorRoot, $"Create {GetFloorRootName(floorIndex)}");
                floorRoot.transform.SetParent(grid.transform);
                floorRoot.transform.localPosition = Vector3.zero;
                floorRoot.transform.localRotation = Quaternion.identity;
                floorRoot.transform.localScale = Vector3.one;
            }

            floorRoot.transform.SetSiblingIndex(floorListIndex);

            for (var definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
            {
                var definition = definitions[definitionIndex];
                var layer = FindDirectChild(floorRoot.transform, definition.Name);
                if (layer == null)
                {
                    layer = new GameObject(definition.Name);
                    Undo.RegisterCreatedObjectUndo(layer, $"Create {GetFloorRootName(floorIndex)}/{definition.Name}");
                    layer.transform.SetParent(floorRoot.transform);
                    layer.transform.localPosition = Vector3.zero;
                    layer.transform.localRotation = Quaternion.identity;
                    layer.transform.localScale = Vector3.one;
                }

                var physicsLayerName = ResolveFloorScopedName(definition.PhysicsLayerNamePattern, floorIndex);
                var physicsLayer = LayerMask.NameToLayer(physicsLayerName);
                if (physicsLayer >= 0) layer.layer = physicsLayer;

                EnsureComponent<Tilemap>(layer);
                var renderer = EnsureComponent<TilemapRenderer>(layer);
                var sortingLayerName = ResolveFloorScopedName(definition.SortingLayerNamePattern, floorIndex);
                if (!string.IsNullOrEmpty(sortingLayerName))
                {
                    renderer.sortingLayerName = sortingLayerName;
                }

                renderer.sortingOrder = definition.SortingOrder;
                renderer.sortOrder = TargetSortOrder;
                renderer.enabled = !definition.HideRendererByDefault;
                layer.transform.SetSiblingIndex(definitionIndex);
            }
        }
    }

    private static void EnsureStandardTilemapLayers(Grid grid)
    {
        ConfigureGrid(grid);
        EnsureSortingLayersExist();
        var ensureOrder = 0;
        foreach (var definition in LayerDefinitions)
        {
            var layer = FindDirectChild(grid.transform, definition.Name);
            if (layer == null)
            {
                layer = new GameObject(definition.Name);
                Undo.RegisterCreatedObjectUndo(layer, $"Create {definition.Name} Tilemap");
                layer.transform.SetParent(grid.transform);
                layer.transform.localPosition = Vector3.zero;
                layer.transform.localRotation = Quaternion.identity;
                layer.transform.localScale = Vector3.one;
            }

            if (definition.NeedsCollider)
            {
                var wallLayer = LayerMask.NameToLayer(WallPhysicsLayerName);
                if (wallLayer >= 0) layer.layer = wallLayer;
            }

            // 每个地图层都必须有 Tilemap 和 TilemapRenderer，才能在 Scene 里绘制并渲染瓦片。
            EnsureComponent<Tilemap>(layer);
            var renderer = EnsureComponent<TilemapRenderer>(layer);
            if (!string.IsNullOrEmpty(definition.SortingLayerName))
            {
                renderer.sortingLayerName = definition.SortingLayerName;
            }

            renderer.sortingOrder = definition.SortingOrder;
            renderer.sortOrder = TargetSortOrder;
            if (definition.HideRendererByDefault) renderer.enabled = false;
            layer.transform.SetSiblingIndex(ensureOrder++);
        }
    }

    private static void ConfigureGrid(Grid grid)
    {
        Undo.RecordObject(grid, "Configure Isometric Grid");
        grid.cellLayout = TargetCellLayout;
        grid.cellGap = Vector3.zero;
        if (grid.cellSize == Vector3.one) grid.cellSize = TargetCellSize;
    }

    private static void EnsureCollisionComponents(Grid grid)
    {
    {
        foreach (var definition in LayerDefinitions)
        {
            if (!definition.NeedsCollider) continue;

            var layer = FindDirectChild(grid.transform, definition.Name);
            if (layer == null) continue;

            var rigidbody2D = EnsureComponent<Rigidbody2D>(layer);
            rigidbody2D.bodyType = RigidbodyType2D.Static;

            var tilemapCollider = EnsureComponent<TilemapCollider2D>(layer);
            tilemapCollider.usedByComposite = true;

            EnsureComponent<CompositeCollider2D>(layer);
        }
    }
    }

    private static void EnsureCollisionComponents(Grid grid, IReadOnlyList<int> floorIndices, IReadOnlyList<FloorTilemapLayerDefinition> definitions)
    {
        for (var floorListIndex = 0; floorListIndex < floorIndices.Count; floorListIndex++)
        {
            var floorRoot = FindFloorRoot(grid.transform, floorIndices[floorListIndex]);
            if (floorRoot == null) continue;

            for (var definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
            {
                var definition = definitions[definitionIndex];
                if (!definition.NeedsCollider) continue;

                var layer = FindDirectChild(floorRoot.transform, definition.Name);
                if (layer == null) continue;

                var rigidbody2D = EnsureComponent<Rigidbody2D>(layer);
                rigidbody2D.bodyType = RigidbodyType2D.Static;

                var tilemapCollider = EnsureComponent<TilemapCollider2D>(layer);
                tilemapCollider.usedByComposite = true;

                EnsureComponent<CompositeCollider2D>(layer);
            }
        }
    }

    private static void ToggleCollisionLayerVisibility(bool visible)
    {
        var grid = GameObject.Find(GridName)?.GetComponent<Grid>();
        if (grid == null) return;

        if (HasFloorGroups(grid))
        {
            var definitions = BuildFloorLayerDefinitions(grid);
            var floorIndices = GetTargetFloorIndices(grid);
            GameObject lastLayer = null;

            for (var floorListIndex = 0; floorListIndex < floorIndices.Count; floorListIndex++)
            {
                var floorRoot = FindFloorRoot(grid.transform, floorIndices[floorListIndex]);
                if (floorRoot == null) continue;

                for (var definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
                {
                    var definition = definitions[definitionIndex];
                    if (!definition.NeedsCollider) continue;

                    var layer = FindDirectChild(floorRoot.transform, definition.Name);
                    if (layer == null) continue;

                    var renderer = layer.GetComponent<TilemapRenderer>();
                    if (renderer == null) continue;
                    Undo.RecordObject(renderer, "Toggle Collision Layer Visibility");
                    renderer.enabled = visible;
                    lastLayer = layer;
                }
            }

            if (lastLayer != null)
            {
                EditorSceneManager.MarkSceneDirty(lastLayer.scene);
                Selection.activeGameObject = lastLayer;
            }

            return;
        }

        var collisionLayer = FindDirectChild(grid.transform, "Collision");
        if (collisionLayer == null) return;
        var collisionRenderer = collisionLayer.GetComponent<TilemapRenderer>();
        if (collisionRenderer == null) return;
        Undo.RecordObject(collisionRenderer, "Toggle Collision Layer Visibility");
        collisionRenderer.enabled = visible;
        EditorSceneManager.MarkSceneDirty(collisionLayer.scene);
        Selection.activeGameObject = collisionLayer;
    }

    private static bool HasAnyTile(Tilemap tilemap)
    {
        var bounds = tilemap.cellBounds;
        foreach (var pos in bounds.allPositionsWithin)
        {
            if (tilemap.GetTile(pos) != null) return true;
        }

        return false;
    }

    private static void TryAutoUpgradeLegacyLayers(Grid grid, List<string> logs)
    {
        for (var i = 0; i < LegacyLayerRenames.Length; i++)
        {
            var rename = LegacyLayerRenames[i];
            var legacyLayer = FindDirectChild(grid.transform, rename.LegacyName);
            if (legacyLayer == null) continue;

            var targetLayer = FindDirectChild(grid.transform, rename.NewName);
            if (targetLayer != null)
            {
                logs.Add($"Skip rename: {rename.LegacyName} -> {rename.NewName} (target already exists).");
                continue;
            }

            Undo.RecordObject(legacyLayer, $"Rename {rename.LegacyName} to {rename.NewName}");
            legacyLayer.name = rename.NewName;
            logs.Add($"Renamed {rename.LegacyName} -> {rename.NewName}.");
        }
    }

    private static GameObject FindDirectChild(Transform parent, string childName)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == childName) return child.gameObject;
        }

        return null;
    }

    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        var component = target.GetComponent<T>();
        if (component != null) return component;
        return Undo.AddComponent<T>(target);
    }

    private static void EnsureSortingLayersExist()
    {
        var tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManager == null || tagManager.Length == 0) return;
        var serialized = new SerializedObject(tagManager[0]);
        var layersProp = serialized.FindProperty("m_SortingLayers");
        if (layersProp == null || !layersProp.isArray) return;

        EnsureSortingLayer(layersProp, SortingLayerMapBack);
        EnsureSortingLayer(layersProp, SortingLayerActor);
        EnsureSortingLayer(layersProp, SortingLayerMapFront);

        serialized.ApplyModifiedProperties();
    }

    private static void EnsureSortingLayersExist(IReadOnlyList<int> floorIndices, IReadOnlyList<FloorTilemapLayerDefinition> floorLayerDefinitions)
    {
        var tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManager == null || tagManager.Length == 0) return;
        var serialized = new SerializedObject(tagManager[0]);
        var layersProp = serialized.FindProperty("m_SortingLayers");
        if (layersProp == null || !layersProp.isArray) return;

        EnsureSortingLayer(layersProp, SortingLayerMapBack);
        EnsureSortingLayer(layersProp, SortingLayerActor);
        EnsureSortingLayer(layersProp, SortingLayerMapFront);

        for (var floorListIndex = 0; floorListIndex < floorIndices.Count; floorListIndex++)
        {
            var floorIndex = floorIndices[floorListIndex];
            EnsureSortingLayer(layersProp, $"{SortingLayerMapBack}_F{floorIndex}");
            EnsureSortingLayer(layersProp, $"{SortingLayerActor}_F{floorIndex}");
            EnsureSortingLayer(layersProp, $"{SortingLayerMapFront}_F{floorIndex}");

            for (var definitionIndex = 0; definitionIndex < floorLayerDefinitions.Count; definitionIndex++)
            {
                var sortingLayerName = ResolveFloorScopedName(floorLayerDefinitions[definitionIndex].SortingLayerNamePattern, floorIndex);
                if (!string.IsNullOrEmpty(sortingLayerName))
                {
                    EnsureSortingLayer(layersProp, sortingLayerName);
                }
            }
        }

        serialized.ApplyModifiedProperties();
    }

    private static void EnsureSortingLayer(SerializedProperty sortingLayers, string name)
    {
        for (var i = 0; i < sortingLayers.arraySize; i++)
        {
            var entry = sortingLayers.GetArrayElementAtIndex(i);
            var entryName = entry.FindPropertyRelative("name");
            if (entryName != null && entryName.stringValue == name) return;
        }

        var ids = new HashSet<int>();
        for (var i = 0; i < sortingLayers.arraySize; i++)
        {
            var entry = sortingLayers.GetArrayElementAtIndex(i);
            var idProp = entry.FindPropertyRelative("uniqueID");
            if (idProp != null) ids.Add(idProp.intValue);
        }

        var newId = MakeStableId(name);
        while (newId == 0 || ids.Contains(newId)) newId++;

        sortingLayers.InsertArrayElementAtIndex(sortingLayers.arraySize);
        var newEntry = sortingLayers.GetArrayElementAtIndex(sortingLayers.arraySize - 1);
        var newName = newEntry.FindPropertyRelative("name");
        var newUniqueId = newEntry.FindPropertyRelative("uniqueID");
        var newLocked = newEntry.FindPropertyRelative("locked");
        if (newName != null) newName.stringValue = name;
        if (newUniqueId != null) newUniqueId.intValue = newId;
        if (newLocked != null) newLocked.intValue = 0;
    }

    private static int MakeStableId(string value)
    {
        unchecked
        {
            var hash = value.GetHashCode();
            if (hash == 0) hash = 1;
            return hash;
        }
    }

    private static void EnsureMarkerGroup(Transform root, string groupName)
    {
        if (FindDirectChild(root, groupName) != null) return;

        var group = new GameObject(groupName);
        Undo.RegisterCreatedObjectUndo(group, $"Create {groupName}");
        group.transform.SetParent(root);
        group.transform.localPosition = Vector3.zero;
        group.transform.localRotation = Quaternion.identity;
        group.transform.localScale = Vector3.one;
    }

    private static bool HasFloorGroups(Grid grid)
    {
        for (var i = 0; i < grid.transform.childCount; i++)
        {
            if (TryGetFloorIndex(grid.transform.GetChild(i).name, out _)) return true;
        }

        return false;
    }

    private static List<int> GetTargetFloorIndices(Grid grid)
    {
        var result = new List<int>();
        var maxFloorIndex = FirstFloorIndex + DefaultFloorCount - 1;

        for (var i = 0; i < grid.transform.childCount; i++)
        {
            if (!TryGetFloorIndex(grid.transform.GetChild(i).name, out var floorIndex)) continue;
            if (!result.Contains(floorIndex)) result.Add(floorIndex);
            if (floorIndex > maxFloorIndex) maxFloorIndex = floorIndex;
        }

        for (var floorIndex = FirstFloorIndex; floorIndex <= maxFloorIndex; floorIndex++)
        {
            if (!result.Contains(floorIndex)) result.Add(floorIndex);
        }

        result.Sort();
        return result;
    }

    private static List<FloorTilemapLayerDefinition> BuildFloorLayerDefinitions(Grid grid)
    {
        var templateFloorRoot = FindTemplateFloorRoot(grid.transform);
        if (templateFloorRoot == null)
        {
            return BuildDefaultFloorLayerDefinitions();
        }

        var result = new List<FloorTilemapLayerDefinition>();
        for (var i = 0; i < templateFloorRoot.transform.childCount; i++)
        {
            var child = templateFloorRoot.transform.GetChild(i).gameObject;
            var tilemap = child.GetComponent<Tilemap>();
            var renderer = child.GetComponent<TilemapRenderer>();
            if (tilemap == null && renderer == null) continue;

            var physicsLayerName = LayerMask.LayerToName(child.layer);
            if (string.IsNullOrEmpty(physicsLayerName)) physicsLayerName = "Default";

            result.Add(new FloorTilemapLayerDefinition(
                child.name,
                MakeFloorScopedPattern(renderer != null ? renderer.sortingLayerName : string.Empty),
                renderer != null ? renderer.sortingOrder : 0,
                physicsLayerName,
                ChildNeedsCollider(child),
                renderer != null && !renderer.enabled));
        }

        return result.Count > 0 ? result : BuildDefaultFloorLayerDefinitions();
    }

    private static List<FloorTilemapLayerDefinition> BuildDefaultFloorLayerDefinitions()
    {
        return new List<FloorTilemapLayerDefinition>
        {
            new("GroundBase", $"{SortingLayerMapBack}_F#", 0, "Default", false, false),
            new("GroundDetail", $"{SortingLayerMapBack}_F#", 10, "Default", false, false),
            new("LowDeco", $"{SortingLayerMapBack}_F#", 20, "Default", false, false),
            new("WallBack", $"{SortingLayerMapBack}_F#", 30, "Default", false, false),
            new("Collision", $"{SortingLayerMapBack}_F#", 40, "Wall_F#", true, true),
            new("WallFront", $"{SortingLayerMapFront}_F#", 50, "Default", false, false),
            new("RoofOrCover", $"{SortingLayerMapFront}_F#", 60, "Default", false, false),
            new("Foreground", $"{SortingLayerMapFront}_F#", 70, "Default", false, false),
            new("Interactable", $"{SortingLayerMapFront}_F#", 80, "Default", false, false)
        };
    }

    private static bool ChildNeedsCollider(GameObject child)
    {
        return child.GetComponent<TilemapCollider2D>() != null ||
               child.GetComponent<CompositeCollider2D>() != null ||
               child.GetComponent<Rigidbody2D>() != null ||
               child.name.Contains("Collision");
    }

    private static GameObject FindTemplateFloorRoot(Transform gridTransform)
    {
        var preferred = FindDirectChild(gridTransform, $"{FloorRootPrefix}{FirstFloorIndex}");
        if (preferred != null) return preferred;

        for (var i = 0; i < gridTransform.childCount; i++)
        {
            var child = gridTransform.GetChild(i).gameObject;
            if (TryGetFloorIndex(child.name, out _)) return child;
        }

        return null;
    }

    private static GameObject FindFloorRoot(Transform gridTransform, int floorIndex)
    {
        return FindDirectChild(gridTransform, GetFloorRootName(floorIndex));
    }

    private static string GetFloorRootName(int floorIndex)
    {
        return $"{FloorRootPrefix}{floorIndex}";
    }

    private static bool TryGetFloorIndex(string objectName, out int floorIndex)
    {
        floorIndex = -1;
        if (!objectName.StartsWith(FloorRootPrefix)) return false;
        return int.TryParse(objectName.Substring(FloorRootPrefix.Length), out floorIndex);
    }

    private static string MakeFloorScopedPattern(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (FloorSuffixRegex.IsMatch(value))
        {
            return FloorSuffixRegex.Replace(value, "_F#");
        }

        return value;
    }

    private static string ResolveFloorScopedName(string pattern, int floorIndex)
    {
        if (string.IsNullOrEmpty(pattern)) return pattern;
        return pattern.Replace("#", floorIndex.ToString());
    }

    private readonly struct TilemapLayerDefinition
    {
        public readonly string Name;
        public readonly string SortingLayerName;
        public readonly int SortingOrder;
        public readonly bool NeedsCollider;
        public readonly bool HideRendererByDefault;

        public TilemapLayerDefinition(string name, string sortingLayerName, int sortingOrder, bool needsCollider, bool hideRendererByDefault)
        {
            Name = name;
            SortingLayerName = sortingLayerName;
            SortingOrder = sortingOrder;
            NeedsCollider = needsCollider;
            HideRendererByDefault = hideRendererByDefault;
        }
    }

    private readonly struct FloorTilemapLayerDefinition
    {
        public readonly string Name;
        public readonly string SortingLayerNamePattern;
        public readonly int SortingOrder;
        public readonly string PhysicsLayerNamePattern;
        public readonly bool NeedsCollider;
        public readonly bool HideRendererByDefault;

        public FloorTilemapLayerDefinition(string name, string sortingLayerNamePattern, int sortingOrder, string physicsLayerNamePattern, bool needsCollider, bool hideRendererByDefault)
        {
            Name = name;
            SortingLayerNamePattern = sortingLayerNamePattern;
            SortingOrder = sortingOrder;
            PhysicsLayerNamePattern = physicsLayerNamePattern;
            NeedsCollider = needsCollider;
            HideRendererByDefault = hideRendererByDefault;
        }
    }

    private readonly struct LegacyLayerRenameDefinition
    {
        public readonly string LegacyName;
        public readonly string NewName;

        public LegacyLayerRenameDefinition(string legacyName, string newName)
        {
            LegacyName = legacyName;
            NewName = newName;
        }
    }
}
