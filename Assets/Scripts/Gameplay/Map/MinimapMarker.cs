using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 小地图标记的用途分类。
/// 分类主要用于策划辨认和后续扩展筛选规则，不参与物品或交互逻辑。
/// </summary>
public enum MinimapMarkerKind
{
    Extraction,
    InterestPoint,
    SearchableContainer,
    Custom
}

/// <summary>
/// 挂在地图世界物体上的小地图标记配置。
/// 对应的 UI 标记必须预先放在 MinimapUI/Viewport/MarkerLayer 下并拖入 Ui Marker，
/// 运行时只更新这个已有 UI 对象的位置与显隐，不动态创建 UI。
/// </summary>
[DisallowMultipleComponent]
public class MinimapMarker : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("标记唯一 ID。为空时使用当前 GameObject 名称。")]
    [SerializeField] private string markerId;
    [Tooltip("策划与调试时显示的名称。")]
    [SerializeField] private string displayName = "兴趣点";
    [Tooltip("标记分类。第一版支持撤离点、兴趣点、可搜索容器和自定义标记。")]
    [SerializeField] private MinimapMarkerKind kind = MinimapMarkerKind.InterestPoint;

    [Header("Floor")]
    [Tooltip("标记所在的逻辑楼层。0 表示一楼，1 表示二楼。")]
    [Min(0)] [SerializeField] private int floorIndex;
    [Tooltip("若物体自身或父节点存在 FloorSortingState，是否优先使用它的当前楼层。静态点位通常不需要开启。")]
    [SerializeField] private bool followFloorSortingState;
    [Tooltip("可选的楼层状态引用。为空时会从当前物体及父节点查找。")]
    [SerializeField] private FloorSortingState floorSortingState;

    [Header("Discovery")]
    [Tooltip("进入本局时是否已经显示。撤离点通常开启，普通兴趣点通常关闭。")]
    [SerializeField] private bool discoveredAtStart;
    [Tooltip("是否在玩家进入发现半径后永久发现。关闭后只能通过代码调用 Discover。")]
    [SerializeField] private bool revealWithinDistance = true;
    [Tooltip("玩家与标记处于同一楼层且进入该世界距离后，标记会在本局显示。")]
    [Min(0f)] [SerializeField] private float discoveryRadius = 4f;

    [Header("Editor-authored UI")]
    [Tooltip("场景中预先创建的 UI 标记。必须是 MinimapUI/Viewport/MarkerLayer 的子节点。")]
    [SerializeField] private RectTransform uiMarker;
    [Tooltip("可选的标记图片引用。后续换图时直接替换此 Image 的 Sprite。")]
    [SerializeField] private Image uiImage;

    [Header("Runtime Status (运行时状态)")]
    [Tooltip("当前局是否已经发现。进入 Play Mode 后由系统维护。")]
    [SerializeField] private bool discovered;

    private ExtractionPoint extractionPoint;

    public string MarkerId => string.IsNullOrWhiteSpace(markerId) ? name : markerId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public MinimapMarkerKind Kind => kind;
    public int FloorIndex
    {
        get
        {
            if (!followFloorSortingState) return Mathf.Max(0, floorIndex);
            if (floorSortingState == null)
                floorSortingState = GetComponentInParent<FloorSortingState>();
            return floorSortingState != null
                ? Mathf.Max(0, floorSortingState.VisibilityFloorIndex)
                : Mathf.Max(0, floorIndex);
        }
    }
    public RectTransform UiMarker => uiMarker;
    public Image UiImage => uiImage;
    public bool IsDiscovered => discovered;
    public bool HasUiMarker => uiMarker != null;

    /// <summary>
    /// 撤离点失效后不再显示；其他标记跟随自身 GameObject 的启用状态。
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            if (!isActiveAndEnabled) return false;
            if (extractionPoint == null) extractionPoint = GetComponent<ExtractionPoint>();
            return extractionPoint == null || extractionPoint.IsAvailable;
        }
    }

    private void Awake()
    {
        ResolveReferences();
        RestoreDiscoveryState();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        RestoreDiscoveryState();
    }

    private void OnValidate()
    {
        floorIndex = Mathf.Max(0, floorIndex);
        discoveryRadius = Mathf.Max(0f, discoveryRadius);
        ResolveReferences();
    }

    /// <summary>
    /// 检查玩家是否满足自动发现条件。已发现状态在当前局内不会因离开范围而消失。
    /// </summary>
    public void TryDiscover(Vector3 playerPosition, int playerFloorIndex)
    {
        if (discovered || !revealWithinDistance) return;
        if (playerFloorIndex != FloorIndex) return;

        var offset = (Vector2)(playerPosition - transform.position);
        if (offset.sqrMagnitude <= discoveryRadius * discoveryRadius)
            Discover();
    }

    /// <summary>供任务、交互或剧情系统直接发现标记。</summary>
    public void Discover()
    {
        discovered = true;
        MinimapDiscoveryStateStore.MarkDiscovered(MarkerId);
    }

    /// <summary>开始新一局时恢复初始发现状态。</summary>
    public void ResetDiscovery()
    {
        discovered = discoveredAtStart;
        if (discovered) MinimapDiscoveryStateStore.MarkDiscovered(MarkerId);
        else MinimapDiscoveryStateStore.Unmark(MarkerId);
    }

    /// <summary>
    /// 编辑器构建工具使用的统一配置入口，避免通过字段名称反射写入。
    /// </summary>
    public void ConfigureEditor(
        string newMarkerId,
        string newDisplayName,
        MinimapMarkerKind newKind,
        int newFloorIndex,
        bool newDiscoveredAtStart,
        float newDiscoveryRadius,
        RectTransform newUiMarker,
        Image newUiImage)
    {
        markerId = newMarkerId;
        displayName = newDisplayName;
        kind = newKind;
        floorIndex = Mathf.Max(0, newFloorIndex);
        discoveredAtStart = newDiscoveredAtStart;
        revealWithinDistance = !newDiscoveredAtStart;
        discoveryRadius = Mathf.Max(0f, newDiscoveryRadius);
        uiMarker = newUiMarker;
        uiImage = newUiImage;
        discovered = discoveredAtStart;
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (uiMarker != null && uiImage == null)
            uiImage = uiMarker.GetComponent<Image>();
        if (followFloorSortingState && floorSortingState == null)
            floorSortingState = GetComponentInParent<FloorSortingState>();
        if (extractionPoint == null)
            extractionPoint = GetComponent<ExtractionPoint>();
    }

    private void RestoreDiscoveryState()
    {
        discovered = discoveredAtStart || MinimapDiscoveryStateStore.Contains(MarkerId);
        if (discovered) MinimapDiscoveryStateStore.MarkDiscovered(MarkerId);
    }

    private void OnDrawGizmos()
    {
        var color = kind switch
        {
            MinimapMarkerKind.Extraction => new Color(0.25f, 1f, 0.5f, 0.9f),
            MinimapMarkerKind.SearchableContainer => new Color(1f, 0.75f, 0.2f, 0.9f),
            MinimapMarkerKind.InterestPoint => new Color(0.25f, 0.75f, 1f, 0.9f),
            _ => new Color(0.9f, 0.9f, 0.9f, 0.9f)
        };
        Gizmos.color = color;
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.25f, discoveryRadius));
#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.35f,
            $"小地图：{DisplayName}\n楼层 {FloorIndex + 1}");
#endif
    }
}
