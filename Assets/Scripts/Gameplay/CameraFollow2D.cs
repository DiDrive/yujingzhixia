using Gameplay;
using UnityEngine;

/// <summary>
/// 2D 镜头跟随：优先跟随手动指定目标；为空时自动查找 PlayerCombatController2D。
/// 挂到 Main Camera 上即可使用。
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow2D : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("跟随目标。为空时会自动查找场景中的 PlayerCombatController2D。")]
    [SerializeField] private Transform target;
    [Tooltip("为 true 时：target 为空会自动查找玩家作为跟随目标。")]
    [SerializeField] private bool autoFindPlayer = true;

    [Header("Follow")]
    [Tooltip("镜头相对目标的世界坐标偏移。通常只需要调 X/Y；Z 会自动保留当前相机深度。")]
    [SerializeField] private Vector2 offset = Vector2.zero;
    [Tooltip("跟随平滑时间（秒）。越小越跟手，越大越柔和。设为 0 则直接跟随。")]
    [SerializeField] private float smoothTime = 0.12f;
    [Tooltip("镜头最大跟随速度。用于限制远距离瞬移时的追赶速度。")]
    [SerializeField] private float maxSpeed = 30f;

    [Header("Look Ahead")]
    [Tooltip("是否根据鼠标方向做镜头前瞻。适合射击/瞄准类俯视游戏。")]
    [SerializeField] private bool enableMouseLookAhead = false;
    [Tooltip("鼠标前瞻最大距离。鼠标离角色越远，镜头越朝该方向偏移，但不会超过该值。")]
    [SerializeField] private float maxLookAheadDistance = 1.5f;
    [Tooltip("前瞻偏移的平滑时间（秒）。越小越跟手，越大越柔和。")]
    [SerializeField] private float lookAheadSmoothTime = 0.08f;

    [Header("Clamp")]
    [Tooltip("是否限制镜头可移动范围。")]
    [SerializeField] private bool clampToBounds = false;
    [Tooltip("镜头可移动的最小世界坐标。仅在启用范围限制时生效。默认表示相机可视区域左下角能到达的最小位置。")]
    [SerializeField] private Vector2 minBounds = new Vector2(-100f, -100f);
    [Tooltip("镜头可移动的最大世界坐标。仅在启用范围限制时生效。默认表示相机可视区域右上角能到达的最大位置。")]
    [SerializeField] private Vector2 maxBounds = new Vector2(100f, 100f);

    private Camera cachedCamera;
    private Vector3 currentVelocity;
    private Vector3 lookAheadVelocity;
    private Vector3 currentLookAhead;
    private float lockedZ;

    private void Awake()
    {
        cachedCamera = GetComponent<Camera>();
        lockedZ = transform.position.z;
    }

    private void Start()
    {
        TryFindTarget();
        SnapToTargetIfPossible();
    }

    private void LateUpdate()
    {
        if (target == null) TryFindTarget();
        if (target == null) return;

        var lookAhead = GetLookAheadOffset();
        var desired = new Vector3(
            target.position.x + offset.x + lookAhead.x,
            target.position.y + offset.y + lookAhead.y,
            lockedZ);

        if (clampToBounds)
        {
            desired = ClampDesiredPosition(desired);
        }

        if (smoothTime <= 0f)
        {
            transform.position = desired;
            return;
        }

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desired,
            ref currentVelocity,
            smoothTime,
            Mathf.Max(0.01f, maxSpeed),
            Time.unscaledDeltaTime);
    }

    private void TryFindTarget()
    {
        if (!autoFindPlayer || target != null) return;
        var player = FindObjectOfType<PlayerCombatController2D>(true);
        if (player != null) target = player.transform;
    }

    private void SnapToTargetIfPossible()
    {
        if (target == null) return;
        var p = target.position;
        transform.position = new Vector3(p.x + offset.x, p.y + offset.y, lockedZ);
    }

    private Vector3 GetLookAheadOffset()
    {
        if (!enableMouseLookAhead || target == null) return Vector3.zero;
        if (cachedCamera == null) cachedCamera = GetComponent<Camera>();
        if (cachedCamera == null) return Vector3.zero;

        var mouseScreen = Input.mousePosition;
        mouseScreen.z = Mathf.Abs(lockedZ);
        var mouseWorld = cachedCamera.ScreenToWorldPoint(mouseScreen);
        var delta = mouseWorld - target.position;
        delta.z = 0f;

        var rawLookAhead = delta.sqrMagnitude <= 0.0001f
            ? Vector3.zero
            : Vector3.ClampMagnitude(delta, Mathf.Max(0f, maxLookAheadDistance));

        if (lookAheadSmoothTime <= 0f)
        {
            currentLookAhead = rawLookAhead;
            return currentLookAhead;
        }

        currentLookAhead = Vector3.SmoothDamp(
            currentLookAhead,
            rawLookAhead,
            ref lookAheadVelocity,
            lookAheadSmoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
        return currentLookAhead;
    }

    private Vector3 ClampDesiredPosition(Vector3 desired)
    {
        if (cachedCamera == null) cachedCamera = GetComponent<Camera>();
        if (cachedCamera == null || !cachedCamera.orthographic)
        {
            desired.x = Mathf.Clamp(desired.x, minBounds.x, maxBounds.x);
            desired.y = Mathf.Clamp(desired.y, minBounds.y, maxBounds.y);
            return desired;
        }

        var halfHeight = cachedCamera.orthographicSize;
        var halfWidth = halfHeight * cachedCamera.aspect;
        var minX = minBounds.x + halfWidth;
        var maxX = maxBounds.x - halfWidth;
        var minY = minBounds.y + halfHeight;
        var maxY = maxBounds.y - halfHeight;

        // 当边界小于可视范围时，将相机锁在中心，避免 Clamp 反转。
        if (minX > maxX) desired.x = (minBounds.x + maxBounds.x) * 0.5f;
        else desired.x = Mathf.Clamp(desired.x, minX, maxX);

        if (minY > maxY) desired.y = (minBounds.y + maxBounds.y) * 0.5f;
        else desired.y = Mathf.Clamp(desired.y, minY, maxY);

        return desired;
    }
}
