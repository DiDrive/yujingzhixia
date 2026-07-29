using UnityEngine;

/// <summary>
/// 实时小地图相机的取景方式。
/// </summary>
public enum MinimapViewMode
{
    FollowPlayer,
    WholeFloorOverview
}

/// <summary>
/// 使用一台编辑器中预先存在的正交相机，把当前场景的真实地图渲染到 RenderTexture。
/// 地图楼层与室内/室外状态直接复用 FloorVisibilityController 已经应用到 Renderer 的显隐结果：
/// 室外保留完整建筑外观与楼顶，进入建筑后则显示玩家当前楼层。
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class MinimapRealtimeRenderer : MonoBehaviour
{
    [Header("Editor-authored References")]
    [Tooltip("专用小地图相机。通常就是当前 GameObject 上的 Camera。")]
    [SerializeField] private Camera minimapCamera;
    [Tooltip("小地图相机输出的 RenderTexture。该资源在编辑器中创建并绑定到 RawImage。")]
    [SerializeField] private RenderTexture targetTexture;
    [Tooltip("现有楼层显隐控制器。小地图会拍摄它处理后的真实场景结果。")]
    [SerializeField] private FloorVisibilityController floorVisibilityController;

    [Header("Whole-floor Overview")]
    [Tooltip("整层总览左下角对应的最小世界坐标。需要和 MinimapController 的地图边界一致。")]
    [SerializeField] private Vector2 worldMin = new(-36f, -9f);
    [Tooltip("整层总览右上角对应的最大世界坐标。需要和 MinimapController 的地图边界一致。")]
    [SerializeField] private Vector2 worldMax = new(50f, 37f);
    [Tooltip("相机在 Z 轴上的位置。地图位于 Z=0 附近时，使用负数并朝 +Z 渲染。")]
    [SerializeField] private float cameraZ = -100f;

    [Header("Camera Framing")]
    [Tooltip("Follow Player：玩家固定在小地图中央，地图随玩家移动。Whole Floor Overview：固定显示整层总览。")]
    [SerializeField] private MinimapViewMode viewMode = MinimapViewMode.FollowPlayer;
    [Tooltip("小地图跟随目标。运行时为空会自动查找出生后的玩家。")]
    [SerializeField] private Transform followTarget;
    [Tooltip("跟随模式下小地图纵向可见的世界单位数。数值越小，小地图放得越大。")]
    [Min(2f)]
    [SerializeField] private float followViewHeight = 18f;
    [Tooltip("跟随目标为空时，是否自动查找运行时生成的玩家。")]
    [SerializeField] private bool autoFindPlayer = true;

    [Header("Rendering")]
    [Tooltip("小地图每秒刷新次数。8～12 次通常已经足够流畅，并能明显降低第二台相机的性能开销。")]
    [Range(1f, 30f)]
    [SerializeField] private float refreshRate = 10f;
    [Tooltip("小地图没有场景内容处使用的底色。")]
    [SerializeField] private Color backgroundColor = new(0.035f, 0.05f, 0.055f, 1f);
    [Tooltip("小地图相机渲染层。默认排除 UI 和 Actor_F0/F1/F2，玩家使用独立 UI 标记显示。")]
    [SerializeField] private LayerMask cullingMask = ~0;
    [Tooltip("非运行状态下启用组件或修改参数时，是否自动刷新一次编辑器预览。")]
    [SerializeField] private bool previewInEditMode = true;

    [Header("Runtime Status (运行时状态)")]
    [Tooltip("上一次成功渲染时玩家是否处于建筑内部。")]
    [SerializeField] private bool lastRenderWasIndoor;
    [Tooltip("上一次成功渲染时的逻辑楼层。")]
    [SerializeField] private int lastRenderedFloor;

    private float nextRenderTime;
    private float nextTargetSearchTime;
    private bool editModePreviewPending;

    public RenderTexture TargetTexture => targetTexture;
    public Vector2 WorldMin => worldMin;
    public Vector2 WorldMax => worldMax;
    public bool IsFollowingTarget =>
        viewMode == MinimapViewMode.FollowPlayer && followTarget != null;
    public Vector2 VisibleWorldMin => GetVisibleWorldBounds().min;
    public Vector2 VisibleWorldMax => GetVisibleWorldBounds().max;
    public bool LastRenderWasIndoor => lastRenderWasIndoor;
    public int LastRenderedFloor => lastRenderedFloor;

    private void Awake()
    {
        ResolveReferences();
        ConfigureCamera();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ConfigureCamera();
        nextRenderTime = 0f;

        if (!Application.isPlaying && previewInEditMode)
            editModePreviewPending = true;
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            // Camera.Render 不能在 OnValidate/Prefab 保存回调中执行，
            // 因此把编辑器预览延迟到之后的 ExecuteAlways LateUpdate。
            if (previewInEditMode && editModePreviewPending)
            {
                editModePreviewPending = false;
                RenderNow();
            }
            return;
        }
        ResolveFollowTarget();
        if (Time.unscaledTime < nextRenderTime) return;

        nextRenderTime = Time.unscaledTime + 1f / Mathf.Max(1f, refreshRate);
        RenderNow();
    }

    private void OnValidate()
    {
        refreshRate = Mathf.Clamp(refreshRate, 1f, 30f);
        followViewHeight = Mathf.Max(2f, followViewHeight);
        if (worldMax.x <= worldMin.x) worldMax.x = worldMin.x + 1f;
        if (worldMax.y <= worldMin.y) worldMax.y = worldMin.y + 1f;
        ResolveReferences();
        ConfigureCamera();

        if (!Application.isPlaying && previewInEditMode && isActiveAndEnabled)
            editModePreviewPending = true;
    }

    /// <summary>
    /// 编辑器构建工具使用的配置入口。
    /// </summary>
    public void ConfigureEditor(
        RenderTexture newTargetTexture,
        FloorVisibilityController newFloorVisibilityController,
        Vector2 newWorldMin,
        Vector2 newWorldMax,
        LayerMask newCullingMask,
        float newRefreshRate)
    {
        targetTexture = newTargetTexture;
        floorVisibilityController = newFloorVisibilityController;
        worldMin = newWorldMin;
        worldMax = newWorldMax;
        cullingMask = newCullingMask;
        refreshRate = Mathf.Clamp(newRefreshRate, 1f, 30f);
        ResolveReferences();
        ConfigureCamera();
        RenderNow();
    }

    /// <summary>
    /// 立即拍摄一次真实地图。相机本身保持 disabled，避免 Unity 每帧自动重复渲染。
    /// </summary>
    [ContextMenu("立即刷新小地图预览")]
    public void RenderNow()
    {
        ResolveReferences();
        ResolveFollowTarget();
        ConfigureCamera();
        if (minimapCamera == null || targetTexture == null) return;
        if (!targetTexture.IsCreated()) targetTexture.Create();

        // FloorVisibilityController 在默认执行顺序完成 LateUpdate，
        // 本组件以 1000 的执行顺序随后拍摄，因此能得到正确的室内/室外与楼层结果。
        if (floorVisibilityController != null)
        {
            lastRenderWasIndoor = floorVisibilityController.IsIndoorFocusActive;
            lastRenderedFloor = floorVisibilityController.CurrentFloorIndex;
        }

        minimapCamera.Render();
    }

    private void ResolveReferences()
    {
        if (minimapCamera == null) minimapCamera = GetComponent<Camera>();
        if (floorVisibilityController == null)
            floorVisibilityController = FindObjectOfType<FloorVisibilityController>(true);
    }

    /// <summary>
    /// 玩家由 GameBootstrap 在运行时生成，因此小地图不能依赖场景中预摆的 Player。
    /// 找到一次后会缓存 Transform，不会在每次渲染时重复扫描场景。
    /// </summary>
    private void ResolveFollowTarget()
    {
        if (followTarget != null || !autoFindPlayer || !Application.isPlaying) return;
        if (Time.unscaledTime < nextTargetSearchTime) return;

        nextTargetSearchTime = Time.unscaledTime + 0.5f;
        var controllers = FindObjectsOfType<PlayerCombatController2D>(true);
        if (controllers != null && controllers.Length > 0)
            followTarget = controllers[0].transform;
    }

    private void ConfigureCamera()
    {
        if (minimapCamera == null) return;

        var size = worldMax - worldMin;
        var center = IsFollowingTarget
            ? (Vector2)followTarget.position
            : (worldMin + worldMax) * 0.5f;
        var viewHeight = IsFollowingTarget
            ? Mathf.Max(2f, followViewHeight)
            : size.y;
        transform.position = new Vector3(center.x, center.y, cameraZ);
        transform.rotation = Quaternion.identity;

        minimapCamera.enabled = false;
        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = Mathf.Max(0.5f, viewHeight * 0.5f);
        minimapCamera.nearClipPlane = 0.1f;
        minimapCamera.farClipPlane = Mathf.Max(200f, Mathf.Abs(cameraZ) + 50f);
        minimapCamera.clearFlags = CameraClearFlags.SolidColor;
        minimapCamera.backgroundColor = backgroundColor;
        minimapCamera.allowHDR = false;
        minimapCamera.allowMSAA = false;
        minimapCamera.useOcclusionCulling = false;
        minimapCamera.cullingMask = cullingMask;
        minimapCamera.targetTexture = targetTexture;
    }

    /// <summary>
    /// 返回相机当前真正拍到的世界矩形。
    /// 标记层使用同一范围换算坐标，保证背景移动后玩家、撤离点和兴趣点仍然对齐。
    /// </summary>
    private Rect GetVisibleWorldBounds()
    {
        var center = (Vector2)transform.position;
        var halfHeight = minimapCamera != null
            ? Mathf.Max(0.5f, minimapCamera.orthographicSize)
            : Mathf.Max(1f, followViewHeight * 0.5f);
        var aspect = 1f;
        if (targetTexture != null && targetTexture.height > 0)
            aspect = (float)targetTexture.width / targetTexture.height;
        else if (minimapCamera != null && minimapCamera.aspect > 0f)
            aspect = minimapCamera.aspect;

        var halfWidth = halfHeight * aspect;
        return Rect.MinMaxRect(
            center.x - halfWidth,
            center.y - halfHeight,
            center.x + halfWidth,
            center.y + halfHeight);
    }
}
