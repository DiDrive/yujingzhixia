using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 单层小地图的静态配置。
/// 底图 Sprite 在 Inspector 中直接引用；世界边界负责把世界坐标换算为 UI 坐标。
/// </summary>
[Serializable]
public class MinimapFloorDefinition
{
    [Tooltip("逻辑楼层索引。0 表示一楼，1 表示二楼。")]
    public int floorIndex;
    [Tooltip("显示在小地图标题中的楼层名称。")]
    public string displayName = "一楼";
    [Tooltip("该楼层的小地图底图。为空时显示编辑器中的原型网格背景。")]
    public Sprite mapSprite;
    [Tooltip("没有正式底图时使用的背景颜色。")]
    public Color fallbackColor = new(0.08f, 0.11f, 0.12f, 1f);
    [Tooltip("地图左下角对应的最小世界坐标。")]
    public Vector2 worldMin = new(-36f, -9f);
    [Tooltip("地图右上角对应的最大世界坐标。")]
    public Vector2 worldMax = new(50f, 37f);

    /// <summary>防止边界宽高为零造成坐标换算异常。</summary>
    public void Sanitize()
    {
        floorIndex = Mathf.Max(0, floorIndex);
        if (worldMax.x <= worldMin.x) worldMax.x = worldMin.x + 1f;
        if (worldMax.y <= worldMin.y) worldMax.y = worldMin.y + 1f;
    }
}

/// <summary>
/// 当前楼层小地图控制器。
/// UI 结构全部在编辑器中预先创建，运行时只负责查找玩家、切换楼层底图、
/// 更新玩家/点位坐标以及控制发现标记显隐。
/// </summary>
[DisallowMultipleComponent]
public class MinimapController : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("玩家世界坐标引用。为空时运行时自动查找 PlayerCombatController2D。")]
    [SerializeField] private Transform player;
    [Tooltip("玩家逻辑楼层。为空时会从玩家自身或父节点自动查找。")]
    [SerializeField] private FloorSortingState playerFloorState;
    [Tooltip("玩家为空时是否自动查找场景中运行时生成的玩家。")]
    [SerializeField] private bool autoFindPlayer = true;

    [Header("Editor-authored UI")]
    [Tooltip("显示底图与标记的矩形区域。所有标记 UI 都应是它或 Marker Layer 的子节点。")]
    [SerializeField] private RectTransform mapRect;
    [Tooltip("当前楼层底图。替换小地图样式时主要修改这个 Image 的 Sprite。")]
    [SerializeField] private Image floorMapImage;
    [Tooltip("实时整层总览输出。绑定后优先显示 RenderTexture，不再要求手工制作 Floor Map Sprite。")]
    [SerializeField] private RawImage realtimeMapImage;
    [Tooltip("实时小地图相机控制器。跟随模式下，标记会使用相机当前可见范围进行坐标换算。")]
    [SerializeField] private MinimapRealtimeRenderer realtimeRenderer;
    [Tooltip("预先放置所有标记 UI 的层级。")]
    [SerializeField] private RectTransform markerLayer;
    [Tooltip("预先放置的玩家标记 UI。")]
    [SerializeField] private RectTransform playerMarker;
    [Tooltip("显示当前楼层名称的文本。")]
    [SerializeField] private Text floorLabel;
    [Tooltip("底部状态文本，例如等待玩家出生。")]
    [SerializeField] private Text statusLabel;

    [Header("Floor Maps")]
    [Tooltip("每层底图与世界坐标边界。楼层索引必须与 FloorSortingState 一致。")]
    [SerializeField] private MinimapFloorDefinition[] floors =
    {
        new() { floorIndex = 0, displayName = "一楼" },
        new() { floorIndex = 1, displayName = "二楼" },
        new() { floorIndex = 2, displayName = "三楼" }
    };

    [Header("Markers")]
    [Tooltip("是否自动收集场景中的 MinimapMarker。只会更新已经绑定 Ui Marker 的标记，不会创建 UI。")]
    [SerializeField] private bool autoCollectMarkers = true;
    [Tooltip("重新收集标记的间隔。运行时新增或启用的兴趣点会在该间隔后加入。")]
    [Min(0.1f)] [SerializeField] private float markerRefreshInterval = 1f;
    [Tooltip("是否把超出地图边界的标记限制在地图边缘。")]
    [SerializeField] private bool clampMarkersToMap = true;
    [Tooltip("手动补充的标记列表。自动收集开启时会与场景标记合并。")]
    [SerializeField] private List<MinimapMarker> markers = new();

    [Header("Runtime Status (运行时状态)")]
    [Tooltip("当前正在显示的逻辑楼层。")]
    [SerializeField] private int currentFloorIndex = -1;
    [Tooltip("当前已收集的有效标记数量。")]
    [SerializeField] private int registeredMarkerCount;

    private float nextMarkerRefreshTime;
    private float nextPlayerSearchTime;
    private MinimapFloorDefinition currentFloor;

    public bool HasRequiredReferences =>
        mapRect != null &&
        (floorMapImage != null || realtimeMapImage != null) &&
        markerLayer != null &&
        playerMarker != null &&
        floorLabel != null;

    private void Awake()
    {
        SanitizeConfiguration();
        ResolveRealtimeRenderer();
        RefreshMarkers();
        ResolvePlayer();
        ApplyFloor(force: true);
    }

    private void OnEnable()
    {
        nextMarkerRefreshTime = 0f;
        if (Application.isPlaying)
            SetUiVisible(true);
    }

    private void Update()
    {
        if (player == null && Time.unscaledTime >= nextPlayerSearchTime)
            ResolvePlayer();

        if (autoCollectMarkers && Time.unscaledTime >= nextMarkerRefreshTime)
            RefreshMarkers();

        if (player == null)
        {
            if (playerMarker != null) playerMarker.gameObject.SetActive(false);
            if (statusLabel != null) statusLabel.text = "等待玩家出生";
            UpdateMarkerVisibilityWithoutPlayer();
            return;
        }

        var resolvedFloor = playerFloorState != null
            ? playerFloorState.VisibilityFloorIndex
            : 0;
        if (resolvedFloor != currentFloorIndex)
            ApplyFloor(force: true);

        if (statusLabel != null) statusLabel.text = "撤离点 / 已发现兴趣点";
        UpdatePlayerMarker();
        UpdateWorldMarkers();
    }

    private void OnValidate()
    {
        markerRefreshInterval = Mathf.Max(0.1f, markerRefreshInterval);
        SanitizeConfiguration();
        ResolveRealtimeRenderer();
        if (!Application.isPlaying)
            ApplyFloor(force: true);
    }

    /// <summary>
    /// 编辑器构建工具使用的引用配置入口。
    /// </summary>
    public void ConfigureEditor(
        RectTransform newMapRect,
        Image newFloorMapImage,
        RectTransform newMarkerLayer,
        RectTransform newPlayerMarker,
        Text newFloorLabel,
        Text newStatusLabel,
        MinimapFloorDefinition[] newFloors)
    {
        mapRect = newMapRect;
        floorMapImage = newFloorMapImage;
        markerLayer = newMarkerLayer;
        playerMarker = newPlayerMarker;
        floorLabel = newFloorLabel;
        statusLabel = newStatusLabel;
        floors = newFloors;
        SanitizeConfiguration();
        ApplyFloor(force: true);
    }

    /// <summary>
    /// 把小地图切换到实时 RenderTexture 显示。
    /// 原 Image 仍保留在 Prefab 中作为故障回退，但正常情况下会被禁用。
    /// </summary>
    public void ConfigureRealtimeEditor(RawImage newRealtimeMapImage)
    {
        realtimeMapImage = newRealtimeMapImage;
        if (realtimeMapImage != null) realtimeMapImage.enabled = true;
        if (floorMapImage != null) floorMapImage.enabled = realtimeMapImage == null;
        ApplyFloor(force: true);
    }

    /// <summary>
    /// 重新收集场景标记。不会创建任何 UI，只接管已经绑定 Ui Marker 的标记。
    /// </summary>
    [ContextMenu("刷新小地图标记")]
    public void RefreshMarkers()
    {
        nextMarkerRefreshTime = Time.unscaledTime + markerRefreshInterval;
        if (markers == null) markers = new List<MinimapMarker>();

        for (var i = markers.Count - 1; i >= 0; i--)
        {
            if (markers[i] == null || !markers[i].HasUiMarker)
                markers.RemoveAt(i);
        }

        if (autoCollectMarkers)
        {
            var found = FindObjectsOfType<MinimapMarker>(true);
            for (var i = 0; i < found.Length; i++)
            {
                var marker = found[i];
                if (marker != null && marker.HasUiMarker && !markers.Contains(marker))
                    markers.Add(marker);
            }
        }

        registeredMarkerCount = markers.Count;
    }

    private void ResolvePlayer()
    {
        nextPlayerSearchTime = Time.unscaledTime + 0.5f;
        if (player != null)
        {
            ResolvePlayerFloorState();
            return;
        }
        if (!autoFindPlayer) return;

        var controllers = FindObjectsOfType<PlayerCombatController2D>(true);
        if (controllers == null || controllers.Length == 0) return;
        player = controllers[0].transform;
        ResolvePlayerFloorState();
    }

    private void ResolvePlayerFloorState()
    {
        if (player == null || playerFloorState != null) return;
        playerFloorState =
            player.GetComponent<FloorSortingState>() ??
            player.GetComponentInParent<FloorSortingState>();
    }

    private void ApplyFloor(bool force)
    {
        var desiredFloor = playerFloorState != null
            ? Mathf.Max(0, playerFloorState.VisibilityFloorIndex)
            : Mathf.Max(0, currentFloorIndex);
        if (currentFloorIndex < 0) desiredFloor = 0;
        if (!force && desiredFloor == currentFloorIndex) return;

        currentFloorIndex = desiredFloor;
        currentFloor = FindFloorDefinition(currentFloorIndex);
        if (currentFloor == null) return;

        if (floorLabel != null)
            floorLabel.text = string.IsNullOrWhiteSpace(currentFloor.displayName)
                ? "未命名楼层"
                : currentFloor.displayName;

        if (floorMapImage != null)
        {
            floorMapImage.enabled = realtimeMapImage == null || realtimeMapImage.texture == null;
            floorMapImage.sprite = currentFloor.mapSprite;
            floorMapImage.color = currentFloor.mapSprite != null
                ? Color.white
                : currentFloor.fallbackColor;
        }

        if (realtimeMapImage != null)
            realtimeMapImage.enabled = realtimeMapImage.texture != null;
    }

    private MinimapFloorDefinition FindFloorDefinition(int floorIndex)
    {
        if (floors == null || floors.Length == 0) return null;
        for (var i = 0; i < floors.Length; i++)
        {
            if (floors[i] != null && floors[i].floorIndex == floorIndex)
                return floors[i];
        }
        return floors[0];
    }

    private void UpdatePlayerMarker()
    {
        if (playerMarker == null || player == null || currentFloor == null) return;
        playerMarker.gameObject.SetActive(true);

        // 跟随模式下相机会以玩家为中心拍摄，玩家 UI 标记固定在 MarkerLayer 中央。
        // 地图和其他世界标记在它下面移动，避免玩家走到旧总览边界后被夹在边缘。
        if (realtimeRenderer != null && realtimeRenderer.IsFollowingTarget)
        {
            var rect = markerLayer != null ? markerLayer.rect : mapRect.rect;
            playerMarker.anchoredPosition = rect.center;
            return;
        }

        playerMarker.anchoredPosition = WorldToMapPosition(player.position, currentFloor);
    }

    private void UpdateWorldMarkers()
    {
        if (markers == null || currentFloor == null) return;
        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            if (marker == null || marker.UiMarker == null) continue;

            marker.TryDiscover(player.position, currentFloorIndex);
            var shouldShow =
                marker.IsAvailable &&
                marker.IsDiscovered &&
                marker.FloorIndex == currentFloorIndex;
            marker.UiMarker.gameObject.SetActive(shouldShow);
            if (shouldShow)
                marker.UiMarker.anchoredPosition =
                    WorldToMapPosition(marker.transform.position, currentFloor);
        }
    }

    private void UpdateMarkerVisibilityWithoutPlayer()
    {
        if (markers == null) return;
        for (var i = 0; i < markers.Count; i++)
        {
            if (markers[i] != null && markers[i].UiMarker != null)
                markers[i].UiMarker.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 将当前楼层上的世界坐标线性映射到 MapRect 的本地坐标。
    /// 等距瓦片已经体现在世界 X/Y 坐标中，因此不需要额外旋转地图坐标。
    /// </summary>
    private Vector2 WorldToMapPosition(Vector3 worldPosition, MinimapFloorDefinition floor)
    {
        if (mapRect == null || floor == null) return Vector2.zero;

        var mappingMin = floor.worldMin;
        var mappingMax = floor.worldMax;
        if (realtimeRenderer != null && realtimeRenderer.IsFollowingTarget)
        {
            mappingMin = realtimeRenderer.VisibleWorldMin;
            mappingMax = realtimeRenderer.VisibleWorldMax;
        }

        var normalizedX = Mathf.InverseLerp(mappingMin.x, mappingMax.x, worldPosition.x);
        var normalizedY = Mathf.InverseLerp(mappingMin.y, mappingMax.y, worldPosition.y);
        if (clampMarkersToMap)
        {
            normalizedX = Mathf.Clamp01(normalizedX);
            normalizedY = Mathf.Clamp01(normalizedY);
        }

        // 标记的 anchoredPosition 属于 MarkerLayer 的本地坐标系。
        // Viewport 可能使用左上角轴心，因此不能直接拿 Viewport.rect 计算标记坐标。
        var rect = markerLayer != null ? markerLayer.rect : mapRect.rect;
        return new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, normalizedX),
            Mathf.Lerp(rect.yMin, rect.yMax, normalizedY));
    }

    private void ResolveRealtimeRenderer()
    {
        if (realtimeRenderer == null)
            realtimeRenderer = FindObjectOfType<MinimapRealtimeRenderer>(true);
    }

    private void SanitizeConfiguration()
    {
        if (floors == null) return;
        for (var i = 0; i < floors.Length; i++)
            floors[i]?.Sanitize();
    }

    private void SetUiVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }
}
