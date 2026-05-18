using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class MapTilemapTools
{
    private const string GridName = "Grid";
    private const string MarkersRootName = "MapMarkers";

    private static readonly TilemapLayerDefinition[] LayerDefinitions =
    {
        new("Ground", 0, false),
        new("Wall", 10, true),
        new("Deco", 20, false),
        new("Collision", 30, true),
        new("Foreground", 40, false),
        new("Interactable", 50, false)
    };

    /// <summary>
    /// 创建或补齐当前场景的标准瓦片地图层级。
    /// 会确保场景里存在 Grid，并在 Grid 下创建 Ground、Wall、Deco、Collision、Foreground、Interactable 六个 Tilemap。
    /// 已存在的同名层不会重复创建，只会补齐缺失的 Tilemap / TilemapRenderer，并设置建议的排序顺序。
    /// </summary>
    [MenuItem("Tools/Map/Create Standard Tilemap Layers")]
    public static void CreateStandardTilemapLayers()
    {
        var grid = FindOrCreateGrid();

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

            // 每个地图层都必须有 Tilemap 和 TilemapRenderer，才能在 Scene 里绘制并渲染瓦片。
            EnsureComponent<Tilemap>(layer);
            var renderer = EnsureComponent<TilemapRenderer>(layer);
            renderer.sortingOrder = definition.SortingOrder;
        }

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Selection.activeGameObject = grid.gameObject;
        Debug.Log("Standard Tilemap layers are ready.");
    }

    /// <summary>
    /// 给需要阻挡移动的瓦片层补齐 2D 碰撞组件。
    /// 当前会处理 Wall 和 Collision 两层：添加 Static Rigidbody2D、TilemapCollider2D、CompositeCollider2D。
    /// CompositeCollider2D 用于合并相邻格子的碰撞边界，减少角色贴墙抖动和碰撞体数量。
    /// </summary>
    [MenuItem("Tools/Map/Add Collision Components")]
    public static void AddCollisionComponents()
    {
        var grid = FindOrCreateGrid();
        foreach (var definition in LayerDefinitions)
        {
            if (!definition.NeedsCollider) continue;

            var layer = FindDirectChild(grid.transform, definition.Name);
            if (layer == null)
            {
                Debug.LogWarning($"Tilemap layer missing: {definition.Name}. Run Create Standard Tilemap Layers first.");
                continue;
            }

            // CompositeCollider2D 需要 Rigidbody2D；地图碰撞不移动，所以固定为 Static。
            var rigidbody2D = EnsureComponent<Rigidbody2D>(layer);
            rigidbody2D.bodyType = RigidbodyType2D.Static;

            var tilemapCollider = EnsureComponent<TilemapCollider2D>(layer);
            tilemapCollider.usedByComposite = true;

            EnsureComponent<CompositeCollider2D>(layer);
        }

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Debug.Log("Collision components are ready on Wall and Collision tilemaps.");
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
            foreach (var definition in LayerDefinitions)
            {
                var layer = FindDirectChild(grid.transform, definition.Name);
                if (layer == null)
                {
                    warnings.Add($"Missing tilemap layer: {definition.Name}.");
                    continue;
                }

                if (layer.GetComponent<Tilemap>() == null) warnings.Add($"{definition.Name} is missing Tilemap.");
                if (layer.GetComponent<TilemapRenderer>() == null) warnings.Add($"{definition.Name} is missing TilemapRenderer.");

                if (definition.NeedsCollider)
                {
                    if (layer.GetComponent<TilemapCollider2D>() == null) warnings.Add($"{definition.Name} is missing TilemapCollider2D.");
                    if (layer.GetComponent<CompositeCollider2D>() == null) warnings.Add($"{definition.Name} is missing CompositeCollider2D.");
                    if (layer.GetComponent<Rigidbody2D>() == null) warnings.Add($"{definition.Name} is missing Rigidbody2D.");
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
        if (grid != null) return grid;

        var gridObject = new GameObject(GridName);
        Undo.RegisterCreatedObjectUndo(gridObject, "Create Grid");
        grid = gridObject.AddComponent<Grid>();
        grid.cellSize = Vector3.one;
        return grid;
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

    private readonly struct TilemapLayerDefinition
    {
        public readonly string Name;
        public readonly int SortingOrder;
        public readonly bool NeedsCollider;

        public TilemapLayerDefinition(string name, int sortingOrder, bool needsCollider)
        {
            Name = name;
            SortingOrder = sortingOrder;
            NeedsCollider = needsCollider;
        }
    }
}
