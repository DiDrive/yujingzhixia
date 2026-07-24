using System;
using System.Collections;
using UnityEngine;
using Gameplay.Combat;

/// <summary>
/// 2D 角色控制：WASD 移动、鼠标朝向、左键轻击、右键蓄力重击。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerCombatController2D : MonoBehaviour, IDamageable, global::IHitReceiver
{
    [Header("Movement")]
    [Tooltip("基础移动速度。")]
    [SerializeField] private float moveSpeed = 4.5f;
    [Tooltip("冲刺/奔跑倍率（预留）。")]
    [SerializeField] private float moveMultiplier = 1f;
    [Tooltip("是否使用 45°斜俯视移动映射（WASD 映射到斜轴）。")]
    [SerializeField] private bool useOblique45Movement = false;
    [Tooltip("45°斜俯视中的“向上”轴方向（屏幕坐标）。")]
    [SerializeField] private Vector2 obliqueUpAxis = new Vector2(1f, 1f);
    [Tooltip("45°斜俯视中的“向右”轴方向（屏幕坐标）。")]
    [SerializeField] private Vector2 obliqueRightAxis = new Vector2(1f, -1f);

    [Header("Look / Facing")]
    [Tooltip("是否使用鼠标位置决定朝向（适合鼠标瞄准/指向攻击）。")]
    [SerializeField] private bool aimWithMouse = true;
    [Tooltip("不使用鼠标瞄准时，是否用移动方向驱动朝向。")]
    [SerializeField] private bool aimWithMoveDirectionWhenNoMouse = true;
    [Tooltip("是否旋转角色 Transform 来表现朝向（像素风通常关闭，改用动画朝向）。")]
    [SerializeField] private bool rotateTransformToAim = false;

    [Header("Pixel")]
    [Tooltip("用于像素对齐的视觉根节点（通常拖角色的 SpriteRenderer 所在子节点）。为空则使用当前物体。")]
    [SerializeField] private Transform pixelSnapVisualRoot;
    [Tooltip("是否启用像素对齐（减少像素抖动/虚焦）。")]
    [SerializeField] private bool pixelSnapEnabled = false;
    [Tooltip("每单位像素数（与贴图 Pixels Per Unit 保持一致）。")]
    [SerializeField] private int pixelsPerUnit = 16;

    [Header("Physics")]
    [Tooltip("为 true 时：使用 Kinematic 移动并在移动前做 Cast，避免与敌人/墙体产生推挤。")]
    [SerializeField] private bool useKinematicMotor = true;
    [Tooltip("移动时参与碰撞检测的层。")]
    [SerializeField] private LayerMask movementCollisionMask = ~0;
    [Tooltip("移动时与碰撞体保持的最小间隙，避免卡进碰撞体导致抖动。")]
    [SerializeField] private float movementSkin = 0.02f;
    [Tooltip("为 true 时：每次移动后检查是否与墙体重叠，若重叠则自动推出去。用于楼梯切层、斜向通道等边界情况下的保底防卡墙。")]
    [SerializeField] private bool resolveWallOverlaps = true;
    [Tooltip("单次 FixedUpdate 最多执行多少次重叠推出迭代。值越大越稳，但也会多做几次碰撞计算。")]
    [SerializeField] private int maxOverlapResolveIterations = 4;
    [Tooltip("推出墙体时额外补的安全间隙，避免刚分离又立刻再次轻微重叠。")]
    [SerializeField] private float overlapResolvePadding = 0.01f;

    [Header("Hit Feedback")]
    [Tooltip("被攻击时的击退距离（单位）。当攻击方传入 DamageInfo.Knockback > 0 时会覆盖该值。")]
    [SerializeField] private float hitKnockbackDistance = 0.8f;
    [Tooltip("被攻击时击退持续时间（秒）。")]
    [SerializeField] private float hitKnockbackDurationSeconds = 0.12f;
    [Tooltip("击退速度上限（单位/秒）。")]
    [SerializeField] private float maxHitKnockbackSpeed = 12f;
    [Tooltip("是否启用受击闪白。")]
    [SerializeField] private bool enableHitFlash = true;
    [Tooltip("受击闪白颜色。")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.45f, 0.45f, 1f);
    [Tooltip("受击闪白持续时间（秒）。")]
    [SerializeField] private float hitFlashDurationSeconds = 0.08f;
    [Tooltip("命中时是否触发 Hitstop。")]
    [SerializeField] private bool enableHitstop = true;
    [Tooltip("Hitstop 时长（秒）。")]
    [SerializeField] private float hitstopDurationSeconds = 0.05f;
    [Tooltip("命中时是否触发相机震动。")]
    [SerializeField] private bool enableCameraShake = true;
    [Tooltip("相机震动时长（秒）。")]
    [SerializeField] private float cameraShakeDurationSeconds = 0.08f;
    [Tooltip("相机震动强度。")]
    [SerializeField] private float cameraShakeMagnitude = 0.12f;

    [Header("Attack")]
    [Tooltip("当前装备武器定义（为空时使用脚本内置默认攻击参数）。")]
    [SerializeField] private WeaponDefinition startingWeapon;
    [Tooltip("投射物生成点（弓箭/枪械）。为空则使用角色位置。")]
    [SerializeField] private Transform projectileSpawnPoint;
    [Tooltip("是否根据角色朝向自动调整投射物生成点位置（要求 projectileSpawnPoint 是角色的子物体）。")]
    [SerializeField] private bool autoMoveProjectileSpawnPoint = true;
    [Tooltip("投射物生成点的基础本地偏移（不随方向变化）。")]
    [SerializeField] private Vector2 projectileSpawnBaseLocalOffset = Vector2.zero;
    [Tooltip("投射物生成点随方向变化的本地偏移（Right 使用 +x，Left 使用 -x；Up 使用 +y，Down 使用 -y）。")]
    [SerializeField] private Vector2 projectileSpawnDirectionalLocalOffset = new Vector2(0.15f, 0.1f);
    [Tooltip("默认轻击冷却（秒）。仅在未装备武器时使用。")]
    [SerializeField] private float defaultLightAttackCooldown = 0.25f;
    [Tooltip("默认轻击伤害。仅在未装备武器时使用。")]
    [SerializeField] private float defaultLightAttackDamage = 20f;
    [Tooltip("默认重击最小伤害。仅在未装备武器时使用。")]
    [SerializeField] private float defaultHeavyAttackMinDamage = 30f;
    [Tooltip("默认重击最大伤害。仅在未装备武器时使用。")]
    [SerializeField] private float defaultHeavyAttackMaxDamage = 70f;
    [Tooltip("默认蓄力到满所需时间（秒）。仅在未装备武器时使用。")]
    [SerializeField] private float defaultHeavyChargeDuration = 1.2f;
    [Tooltip("默认攻击判定距离。仅在未装备武器时使用。")]
    [SerializeField] private float defaultAttackRange = 1.2f;
    [Tooltip("默认攻击判定半径。仅在未装备武器时使用。")]
    [SerializeField] private float defaultAttackRadius = 1.2f;
    [Tooltip("默认攻击目标层（可选）。仅在未装备武器时使用。")]
    [SerializeField] private LayerMask defaultHitMask = ~0;

    [Header("Weapon Switch")]
    [Tooltip("快捷切换武器列表（按 1~9 切换）。")]
    [SerializeField] private WeaponDefinition[] quickSwitchWeapons;
    [Tooltip("是否启用快捷切换（1~9）。")]
    [SerializeField] private bool enableQuickSwitch = true;
    [Tooltip("是否打印武器切换日志。")]
    [SerializeField] private bool debugLogWeaponSwitch = false;

    [Header("Debug")]
    [Tooltip("是否打印攻击日志。")]
    [SerializeField] private bool debugLogAttack = true;

    [Header("Health")]
    [Tooltip("生存系统引用（用于读取 hp 并触发死亡动画）。为空时自动查找。")]
    [SerializeField] private SurvivalSystem survivalSystem;
    [Tooltip("hp 小于等于该值时视为死亡。")]
    [SerializeField] private float deathHpThreshold = 0f;
    [Tooltip("死亡动画播放完成后是否禁用角色控制脚本。")]
    [SerializeField] private bool disableControllerAfterDeathAnimation = false;
    [Tooltip("死亡动画播放完成后是否让尸体逐渐隐去。")]
    [SerializeField] private bool fadeCorpseAfterDeath = true;
    [Tooltip("死亡动画结束事件触发后，延迟多少秒开始淡出。")]
    [SerializeField] private float corpseFadeDelaySeconds = 0f;
    [Tooltip("淡出持续时间（秒）。")]
    [SerializeField] private float corpseFadeDurationSeconds = 1.2f;
    [Tooltip("淡出结束后是否销毁物体。为 false 时仅隐藏 SpriteRenderer。")]
    [SerializeField] private bool destroyAfterFade = false;

    [Header("Animation")]
    [Tooltip("角色动画器（为空时自动从当前物体获取）。")]
    [SerializeField] private Animator characterAnimator;
    [Tooltip("移动输入很小时，仍保持上一次移动方向用于动画朝向。")]
    [SerializeField] private bool keepLastMoveDirectionForAnimation = true;
    [Tooltip("动画方向平滑时间（秒）。")]
    [SerializeField] private float animatorDirectionDamp = 0.08f;
    [Tooltip("受击动画触发器参数名（Trigger）。为空则不触发受击动画。")]
    [SerializeField] private string hitTriggerParam = "Hit";

    private Rigidbody2D rb;
    private Collider2D primaryCollider;
    private Camera mainCamera;
    private Vector2 moveInput;
    private Vector2 moveInputRaw;
    private Vector2 lookDirection = Vector2.right;
    private Vector2 lastMoveDirection = Vector2.down;
    private Vector2 currentMoveVelocity;
    private Vector2 hitKnockbackVelocity;
    private float hitKnockbackEndTime;
    private Coroutine hitFlashRoutine;
    private float nextLightAttackTime;
    private bool isChargingHeavy;
    private float heavyChargeStartTime;
    private WeaponDefinition currentWeapon;
    private bool pendingProjectileShot;
    private bool isDead;
    private bool isDeathAnimationFinished;
    private bool corpseFadeStarted;
    // 缓存 Animator 参数是否存在，兼容不同角色使用参数不完全一致的控制器。
    private bool hasHitTrigger;
    private bool hasLightAttackTrigger;
    private bool hasHeavyAttackTrigger;
    private bool hasMoveX;
    private bool hasMoveY;
    private bool hasSpeed;
    private bool hasFacingX;
    private bool hasFacingY;
    private bool hasIsMoving;
    private bool hasIsChargingHeavy;
    private bool hasHeavyCharge;
    private bool hasMoveDir8;
    private bool hasFaceDir8;
    private bool hasWeaponPose;
    private bool hasIsDead;
    [Header("Runtime Control (运行时控制)")]
    [Tooltip("运行时是否允许读取移动、瞄准、攻击和武器切换输入。撤离完成后由流程系统关闭。")]
    [SerializeField] private bool playerControlEnabled = true;

    public bool IsDead => isDead;
    public bool PlayerControlEnabled => playerControlEnabled;

    public event Action<float, bool> OnAttackPerformed;

    /// <summary>
    /// 当前朝向（单位向量）。
    /// </summary>
    public Vector2 LookDirection => lookDirection;

    /// <summary>
    /// 当前重击蓄力进度（0~1）。
    /// </summary>
    public float HeavyChargeNormalized
    {
        get
        {
            if (!isChargingHeavy) return 0f;
            var elapsed = Time.time - heavyChargeStartTime;
            return Mathf.Clamp01(elapsed / Mathf.Max(0.01f, CurrentHeavyChargeDuration));
        }
    }

    public WeaponDefinition CurrentWeapon => currentWeapon;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        primaryCollider = GetComponent<Collider2D>();
        mainCamera = Camera.main;
        if (rb != null && useKinematicMotor)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
        }
        if (characterAnimator == null) characterAnimator = GetComponent<Animator>();
        CacheAnimatorParameters();
        if (survivalSystem == null) survivalSystem = FindObjectOfType<SurvivalSystem>();
        if (startingWeapon != null) EquipWeapon(startingWeapon);
    }

    private void Update()
    {
        if (UpdateDeathState())
        {
            UpdateAnimatorParams();
            return;
        }

        if (!playerControlEnabled)
        {
            ClearControlInput();
            UpdateAnimatorParams();
            return;
        }

        ReadMoveInput();
        UpdateLookDirection();
        UpdateProjectileSpawnPoint();
        HandleAttackInput();
        UpdateAnimatorParams();
        HandleWeaponQuickSwitch();
    }

    private void LateUpdate()
    {
        if (!pixelSnapEnabled) return;
        var target = pixelSnapVisualRoot != null ? pixelSnapVisualRoot : transform;
        target.position = SnapWorldPositionToPixelGrid(target.position);
    }

    private void FixedUpdate()
    {
        if (isDead || !playerControlEnabled)
        {
            if (rb != null) rb.velocity = Vector2.zero;
            return;
        }
        MoveCharacter();
    }

    /// <summary>
    /// 开启或关闭玩家的移动、瞄准、攻击和武器切换输入。
    /// 关闭时立即清除速度与蓄力状态，避免撤离完成后角色继续滑动或释放攻击。
    /// </summary>
    public void SetPlayerControlEnabled(bool enabled)
    {
        playerControlEnabled = enabled;
        if (!enabled) ClearControlInput();
    }

    /// <summary>清空所有由玩家输入产生的瞬时状态。</summary>
    private void ClearControlInput()
    {
        moveInput = Vector2.zero;
        moveInputRaw = Vector2.zero;
        currentMoveVelocity = Vector2.zero;
        hitKnockbackVelocity = Vector2.zero;
        hitKnockbackEndTime = 0f;
        isChargingHeavy = false;
        pendingProjectileShot = false;
        if (rb != null) rb.velocity = Vector2.zero;
    }

    private void ReadMoveInput()
    {
        var x = Input.GetAxisRaw("Horizontal");
        var y = Input.GetAxisRaw("Vertical");
        moveInputRaw = new Vector2(x, y);
        moveInput = ConvertMoveInput(moveInputRaw);
        if (moveInput.sqrMagnitude > 0.0001f)
        {
            lastMoveDirection = moveInput.normalized;
        }
    }

    private void MoveCharacter()
    {
        if (rb == null) return;
        var survivalMoveMultiplier = survivalSystem != null ? survivalSystem.MoveSpeedMultiplier : 1f;
        var velocity = moveInput * moveSpeed * Mathf.Max(0f, moveMultiplier) * survivalMoveMultiplier;
        if (Time.time < hitKnockbackEndTime) velocity += hitKnockbackVelocity;
        if (!useKinematicMotor || rb.bodyType != RigidbodyType2D.Kinematic)
        {
            rb.velocity = velocity;
            currentMoveVelocity = rb.velocity;
            if (resolveWallOverlaps) ResolveWallOverlaps();
            return;
        }

        var desiredMove = velocity * Time.fixedDeltaTime;
        if (desiredMove.sqrMagnitude <= 0.0000001f)
        {
            rb.velocity = Vector2.zero;
            currentMoveVelocity = Vector2.zero;
            return;
        }

        var dir = desiredMove.normalized;
        var moveDistance = desiredMove.magnitude;
        var filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.useLayerMask = true;
        filter.layerMask = FloorLayerUtility.GetMovementCollisionMaskForCurrentFloor(transform, movementCollisionMask);

        var hitCount = rb.Cast(dir, filter, castResults, moveDistance + Mathf.Max(0f, movementSkin));
        var allowedDistance = moveDistance;
        for (var i = 0; i < hitCount; i++)
        {
            var hit = castResults[i];
            if (hit.collider == null) continue;
            allowedDistance = Mathf.Min(allowedDistance, Mathf.Max(0f, hit.distance - Mathf.Max(0f, movementSkin)));
        }

        var appliedMove = dir * allowedDistance;
        rb.MovePosition(rb.position + appliedMove);
        if (resolveWallOverlaps) ResolveWallOverlaps();
        rb.velocity = Vector2.zero;
        currentMoveVelocity = appliedMove / Time.fixedDeltaTime;
    }

    private readonly RaycastHit2D[] castResults = new RaycastHit2D[8];
    private readonly Collider2D[] overlapResults = new Collider2D[8];

    private Vector2 ConvertMoveInput(Vector2 inputRaw)
    {
        if (!useOblique45Movement)
        {
            return inputRaw.sqrMagnitude > 1f ? inputRaw.normalized : inputRaw;
        }

        var up = obliqueUpAxis.sqrMagnitude <= 0.0001f ? new Vector2(1f, 1f) : obliqueUpAxis.normalized;
        var right = obliqueRightAxis.sqrMagnitude <= 0.0001f ? new Vector2(1f, -1f) : obliqueRightAxis.normalized;
        var mapped = right * inputRaw.x + up * inputRaw.y;
        return mapped.sqrMagnitude > 1f ? mapped.normalized : mapped;
    }

    private void ResolveWallOverlaps()
    {
        if (rb == null || primaryCollider == null) return;

        var filter = new ContactFilter2D();
        filter.useLayerMask = true;
        filter.useTriggers = false;
        filter.layerMask = FloorLayerUtility.GetWallOnlyMaskForCurrentFloor(transform);

        var iterations = Mathf.Max(1, maxOverlapResolveIterations);
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var overlapCount = primaryCollider.OverlapCollider(filter, overlapResults);
            var totalSeparation = Vector2.zero;
            var overlapFound = false;

            for (var i = 0; i < overlapCount; i++)
            {
                var other = overlapResults[i];
                if (other == null || other == primaryCollider) continue;

                var distance = primaryCollider.Distance(other);
                if (!distance.isOverlapped) continue;

                overlapFound = true;
                totalSeparation += distance.normal * (distance.distance - Mathf.Max(0f, overlapResolvePadding));
            }

            if (!overlapFound || totalSeparation.sqrMagnitude <= 0.0000001f) return;
            rb.position += totalSeparation;
        }
    }

    private void UpdateLookDirection()
    {
        Vector2 desiredDir;
        if (aimWithMouse)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            var screen = Input.mousePosition;
            screen.z = Mathf.Abs(mainCamera.transform.position.z - transform.position.z);
            var mouseWorld = mainCamera.ScreenToWorldPoint(screen);
            var delta = mouseWorld - transform.position;
            delta.z = 0f;
            if (delta.sqrMagnitude <= 0.0001f) return;

            desiredDir = ((Vector2)delta).normalized;
        }
        else
        {
            if (!aimWithMoveDirectionWhenNoMouse) return;

            desiredDir = moveInput.sqrMagnitude > 0.0001f
                ? moveInput.normalized
                : (keepLastMoveDirectionForAnimation ? lastMoveDirection : Vector2.zero);

            if (desiredDir.sqrMagnitude <= 0.0001f) return;
        }

        lookDirection = desiredDir;

        if (rotateTransformToAim)
        {
            var angle = Mathf.Atan2(lookDirection.y, lookDirection.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
        }
    }

    private void HandleAttackInput()
    {
        if (isDead) return;
        if (Input.GetMouseButtonDown(0))
        {
            TryLightAttack();
        }

        if (Input.GetMouseButtonDown(1))
        {
            if (survivalSystem != null && !survivalSystem.CanUseChargeAttack) return;
            isChargingHeavy = true;
            heavyChargeStartTime = Time.time;
        }

        if (Input.GetMouseButtonUp(1))
        {
            ReleaseHeavyAttack();
        }
    }

    private void TryLightAttack()
    {
        if (Time.time < nextLightAttackTime) return;
        nextLightAttackTime = Time.time + Mathf.Max(0.01f, CurrentLightAttackCooldown);

        if (characterAnimator != null && hasLightAttackTrigger) characterAnimator.SetTrigger("LightAttack");

        if (currentWeapon != null && currentWeapon.type == WeaponType.Ranged && currentWeapon.ranged.spawnOnAnimationEvent)
        {
            pendingProjectileShot = true;
            return;
        }

        PerformPrimaryAttack();
    }

    private void ReleaseHeavyAttack()
    {
        if (!isChargingHeavy) return;
        isChargingHeavy = false;

        var t = HeavyChargeNormalized;
        // Mouse up 时 HeavyChargeNormalized 已变 0，因此按时间重算一次。
        var elapsed = Time.time - heavyChargeStartTime;
        t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, CurrentHeavyChargeDuration));
        var damage = Mathf.Lerp(defaultHeavyAttackMinDamage, defaultHeavyAttackMaxDamage, t);
        if (characterAnimator != null && hasHeavyAttackTrigger) characterAnimator.SetTrigger("HeavyAttack");
        PerformMeleeAttack(damage, true);
    }

    public void AnimationEvent_FireProjectile()
    {
        if (!pendingProjectileShot) return;
        pendingProjectileShot = false;
        if (currentWeapon == null || currentWeapon.type != WeaponType.Ranged) return;
        FireProjectile(currentWeapon.ranged);
    }

    private void UpdateAnimatorParams()
    {
        if (characterAnimator == null) return;

        var moveDir = moveInput.sqrMagnitude > 0.0001f
            ? moveInput.normalized
            : (keepLastMoveDirectionForAnimation ? lastMoveDirection : Vector2.zero);

        if (hasMoveX) characterAnimator.SetFloat("MoveX", moveDir.x, animatorDirectionDamp, Time.deltaTime);
        if (hasMoveY) characterAnimator.SetFloat("MoveY", moveDir.y, animatorDirectionDamp, Time.deltaTime);
        if (hasSpeed) characterAnimator.SetFloat("Speed", currentMoveVelocity.magnitude);
        if (hasFacingX) characterAnimator.SetFloat("FacingX", lookDirection.x, animatorDirectionDamp, Time.deltaTime);
        if (hasFacingY) characterAnimator.SetFloat("FacingY", lookDirection.y, animatorDirectionDamp, Time.deltaTime);
        if (hasIsMoving) characterAnimator.SetBool("IsMoving", moveInput.sqrMagnitude > 0.0001f);
        if (hasIsChargingHeavy) characterAnimator.SetBool("IsChargingHeavy", isChargingHeavy);
        if (hasHeavyCharge) characterAnimator.SetFloat("HeavyCharge", HeavyChargeNormalized);
        if (hasMoveDir8) characterAnimator.SetInteger("MoveDir8", DirectionTo8Way(moveDir));
        if (hasFaceDir8) characterAnimator.SetInteger("FaceDir8", DirectionTo8Way(lookDirection));
        if (hasWeaponPose) characterAnimator.SetInteger("WeaponPose", currentWeapon != null ? (int)currentWeapon.pose : 0);
        if (hasIsDead) characterAnimator.SetBool("IsDead", isDead);
    }

    /// <summary>
    /// 检测当前 Animator Controller 支持的参数，后续写入前据此安全过滤。
    /// </summary>
    private void CacheAnimatorParameters()
    {
        hasMoveX = AnimatorParamUtil2D.HasParameter(characterAnimator, "MoveX", AnimatorControllerParameterType.Float);
        hasMoveY = AnimatorParamUtil2D.HasParameter(characterAnimator, "MoveY", AnimatorControllerParameterType.Float);
        hasSpeed = AnimatorParamUtil2D.HasParameter(characterAnimator, "Speed", AnimatorControllerParameterType.Float);
        hasFacingX = AnimatorParamUtil2D.HasParameter(characterAnimator, "FacingX", AnimatorControllerParameterType.Float);
        hasFacingY = AnimatorParamUtil2D.HasParameter(characterAnimator, "FacingY", AnimatorControllerParameterType.Float);
        hasIsMoving = AnimatorParamUtil2D.HasParameter(characterAnimator, "IsMoving", AnimatorControllerParameterType.Bool);
        hasIsChargingHeavy = AnimatorParamUtil2D.HasParameter(characterAnimator, "IsChargingHeavy", AnimatorControllerParameterType.Bool);
        hasHeavyCharge = AnimatorParamUtil2D.HasParameter(characterAnimator, "HeavyCharge", AnimatorControllerParameterType.Float);
        hasMoveDir8 = AnimatorParamUtil2D.HasParameter(characterAnimator, "MoveDir8", AnimatorControllerParameterType.Int);
        hasFaceDir8 = AnimatorParamUtil2D.HasParameter(characterAnimator, "FaceDir8", AnimatorControllerParameterType.Int);
        hasWeaponPose = AnimatorParamUtil2D.HasParameter(characterAnimator, "WeaponPose", AnimatorControllerParameterType.Int);
        hasIsDead = AnimatorParamUtil2D.HasParameter(characterAnimator, "IsDead", AnimatorControllerParameterType.Bool);
        hasLightAttackTrigger = AnimatorParamUtil2D.HasParameter(characterAnimator, "LightAttack", AnimatorControllerParameterType.Trigger);
        hasHeavyAttackTrigger = AnimatorParamUtil2D.HasParameter(characterAnimator, "HeavyAttack", AnimatorControllerParameterType.Trigger);
        hasHitTrigger = !string.IsNullOrWhiteSpace(hitTriggerParam) &&
                        AnimatorParamUtil2D.HasParameter(characterAnimator, hitTriggerParam, AnimatorControllerParameterType.Trigger);
    }

    private static int DirectionTo8Way(Vector2 dir)
    {
        if (dir.sqrMagnitude <= 0.0001f) return 0; // N
        var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;
        var sector = Mathf.RoundToInt(angle / 45f) % 8;
        // 约定：0:E,1:NE,2:N,3:NW,4:W,5:SW,6:S,7:SE
        return sector;
    }

    private Vector3 SnapWorldPositionToPixelGrid(Vector3 worldPosition)
    {
        var ppu = Mathf.Max(1, pixelsPerUnit);
        var unit = 1f / ppu;
        worldPosition.x = Mathf.Round(worldPosition.x / unit) * unit;
        worldPosition.y = Mathf.Round(worldPosition.y / unit) * unit;
        return worldPosition;
    }

    private void PerformPrimaryAttack()
    {
        if (isDead) return;
        if (currentWeapon == null)
        {
            PerformMeleeAttack(defaultLightAttackDamage, false);
            return;
        }

        if (currentWeapon.type == WeaponType.Melee)
        {
            PerformMeleeAttack(Mathf.Max(0f, currentWeapon.melee.damage), false);
            return;
        }

        FireProjectile(currentWeapon.ranged);
    }

    private void PerformMeleeAttack(float damage, bool isHeavy)
    {
        if (isDead) return;
        damage *= survivalSystem != null ? survivalSystem.DamageMultiplier : 1f;
        var range = CurrentAttackRange;
        var radius = CurrentAttackRadius;
        var hitMask = FloorLayerUtility.GetActorHitMaskForCurrentFloor(transform, CurrentHitMask);
        var knockback = 0.9f;
        if (currentWeapon != null && currentWeapon.type == WeaponType.Melee && currentWeapon.melee.knockback > 0f)
        {
            knockback = currentWeapon.melee.knockback;
        }
        // 击退优先级：WeaponDefinition.melee.knockback > 0 时覆盖默认值；否则使用默认值（当前为 0.9）。
        // 近战命中判定：以 origin 为圆心做 OverlapCircle，origin = playerPos + lookDir * range；radius 为判定半径。
        var origin = (Vector2)transform.position + lookDirection * range;
        var hitCount = 0;
        var hits = Physics2D.OverlapCircleAll(origin, radius, hitMask);
        for (var i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;
            if (col.attachedRigidbody != null && col.attachedRigidbody.gameObject == gameObject) continue;
            if (!FloorLayerUtility.AreOnSameFloor(transform, col.transform)) continue;
            if (!FloorLayerUtility.AreHostile(transform, col.transform)) continue;
            hitCount++;

            if (global::HitReceiverUtil.TryGet(col, out var hitReceiver))
            {
                var dir = (Vector2)(col.transform.position - transform.position);
                hitReceiver.ApplyHit(new global::DamageInfo(damage, transform.position, dir, knockback));
                continue;
            }

            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable != null) damageable.ApplyDamage(damage);
        }

        if (debugLogAttack)
        {
            var attackType = isHeavy ? "Heavy" : "Light";
            Debug.Log($"[PlayerAttack] {attackType} damage={damage:F1}, hits={hitCount}");
        }

        OnAttackPerformed?.Invoke(damage, isHeavy);
    }

    private void FireProjectile(RangedAttackSettings ranged)
    {
        if (ranged.projectilePrefab == null) return;
        var survivalDamageMultiplier = survivalSystem != null ? survivalSystem.DamageMultiplier : 1f;
        var spawn = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;
        var projectile = Instantiate(ranged.projectilePrefab, spawn, Quaternion.identity);
        var dir = SnapToCardinal(lookDirection);
        projectile.SetOwner(transform);
        projectile.Initialize(
            dir,
            Mathf.Max(0.01f, ranged.projectileSpeed),
            Mathf.Max(0.01f, ranged.projectileLifetime),
            Mathf.Max(0f, ranged.damage) * survivalDamageMultiplier,
            Mathf.Max(0f, ranged.knockback),
            FloorLayerUtility.GetProjectileHitMaskForCurrentFloor(
                transform,
                ranged.hitMask.value == 0 ? defaultHitMask : ranged.hitMask));
    }

    private void UpdateProjectileSpawnPoint()
    {
        if (!autoMoveProjectileSpawnPoint) return;
        if (projectileSpawnPoint == null) return;
        if (projectileSpawnPoint.parent != transform) return;

        var dir = SnapToCardinal(lookDirection);
        var offset = projectileSpawnBaseLocalOffset + new Vector2(
            dir.x * projectileSpawnDirectionalLocalOffset.x,
            dir.y * projectileSpawnDirectionalLocalOffset.y);
        projectileSpawnPoint.localPosition = new Vector3(offset.x, offset.y, projectileSpawnPoint.localPosition.z);
    }

    private static Vector2 SnapToCardinal(Vector2 dir)
    {
        if (dir.sqrMagnitude <= 0.0001f) return Vector2.right;
        return Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
            ? new Vector2(Mathf.Sign(dir.x), 0f)
            : new Vector2(0f, Mathf.Sign(dir.y));
    }

    private void OnDrawGizmosSelected()
    {
        var dir = Application.isPlaying ? lookDirection : Vector2.up;
        var origin = (Vector2)transform.position + dir * (Application.isPlaying ? CurrentAttackRange : defaultAttackRange);
        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(origin, Application.isPlaying ? CurrentAttackRadius : defaultAttackRadius);
    }

    public void EquipWeapon(WeaponDefinition weapon)
    {
        if (isDead) return;
        currentWeapon = weapon;
        pendingProjectileShot = false;

        if (characterAnimator != null)
        {
            if (weapon != null && weapon.animatorController != null)
            {
                characterAnimator.runtimeAnimatorController = weapon.animatorController;
                CacheAnimatorParameters();
            }
        }
    }

    private void HandleWeaponQuickSwitch()
    {
        if (isDead) return;
        if (!enableQuickSwitch) return;
        if (quickSwitchWeapons == null || quickSwitchWeapons.Length <= 0) return;

        for (var i = 0; i < quickSwitchWeapons.Length && i < 9; i++)
        {
            var alphaKey = (KeyCode)((int)KeyCode.Alpha1 + i);
            var keypadKey = (KeyCode)((int)KeyCode.Keypad1 + i);
            if (!Input.GetKeyDown(alphaKey) && !Input.GetKeyDown(keypadKey)) continue;
            var weapon = quickSwitchWeapons[i];
            if (weapon == null) continue;
            EquipWeapon(weapon);
            if (debugLogWeaponSwitch) Debug.Log($"[WeaponSwitch] slot={i + 1}, weaponId={weapon.weaponId}");
            break;
        }
    }

    private float CurrentLightAttackCooldown =>
        currentWeapon != null
            ? (currentWeapon.type == WeaponType.Melee ? currentWeapon.melee.cooldown : currentWeapon.ranged.cooldown)
            : defaultLightAttackCooldown;

    private float CurrentHeavyChargeDuration => defaultHeavyChargeDuration;

    private float CurrentAttackRange =>
        currentWeapon != null && currentWeapon.type == WeaponType.Melee
            ? Mathf.Max(0f, currentWeapon.melee.range)
            : defaultAttackRange;

    private float CurrentAttackRadius =>
        currentWeapon != null && currentWeapon.type == WeaponType.Melee
            ? Mathf.Max(0f, currentWeapon.melee.radius)
            : defaultAttackRadius;

    private LayerMask CurrentHitMask =>
        currentWeapon != null && currentWeapon.type == WeaponType.Melee && currentWeapon.melee.hitMask.value != 0
            ? currentWeapon.melee.hitMask
            : defaultHitMask;

    private bool UpdateDeathState()
    {
        if (isDead) return true;
        if (survivalSystem == null) return false;
        if (survivalSystem.hp > deathHpThreshold) return false;

        isDead = true;
        isChargingHeavy = false;
        pendingProjectileShot = false;
        moveInput = Vector2.zero;
        moveInputRaw = Vector2.zero;
        if (rb != null) rb.velocity = Vector2.zero;
        if (characterAnimator != null)
        {
            if (hasLightAttackTrigger) characterAnimator.ResetTrigger("LightAttack");
            if (hasHeavyAttackTrigger) characterAnimator.ResetTrigger("HeavyAttack");
        }
        return true;
    }

    public void ApplyDamage(float amount)
    {
        if (survivalSystem == null) return;
        survivalSystem.ApplyDamage(amount);
    }

    public void ApplyHit(global::DamageInfo hit)
    {
        if (isDead) return;
        ApplyDamage(hit.Damage);
        if (isDead) return;

        if (characterAnimator != null && hasHitTrigger) characterAnimator.SetTrigger(hitTriggerParam);

        var dir = hit.Direction.sqrMagnitude > 0.0001f
            ? hit.Direction.normalized
            : ((Vector2)transform.position - hit.SourcePosition).normalized;

        var distance = hitKnockbackDistance;
        if (hit.Knockback > 0f) distance = hit.Knockback;

        var duration = Mathf.Max(0.01f, hitKnockbackDurationSeconds);
        var speed = Mathf.Min(maxHitKnockbackSpeed, distance / duration);
        hitKnockbackVelocity = dir * Mathf.Max(0f, speed);
        hitKnockbackEndTime = Time.time + duration;

        if (enableHitFlash) StartHitFlash();
        if (enableHitstop) global::HitFeedbackSystem2D.TriggerHitstop(hitstopDurationSeconds);
        if (enableCameraShake) global::HitFeedbackSystem2D.TriggerCameraShake(cameraShakeDurationSeconds, cameraShakeMagnitude);
    }

    private void StartHitFlash()
    {
        if (hitFlashRoutine != null) StopCoroutine(hitFlashRoutine);
        hitFlashRoutine = StartCoroutine(HitFlashCoroutine());
    }

    private IEnumerator HitFlashCoroutine()
    {
        var sprites = GetComponentsInChildren<SpriteRenderer>(true);
        if (sprites == null || sprites.Length == 0) yield break;

        var colors = new Color[sprites.Length];
        for (var i = 0; i < sprites.Length; i++)
        {
            colors[i] = sprites[i] != null ? sprites[i].color : Color.white;
        }

        for (var i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] != null) sprites[i].color = hitFlashColor;
        }

        yield return new WaitForSeconds(Mathf.Max(0.01f, hitFlashDurationSeconds));

        for (var i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] != null) sprites[i].color = colors[i];
        }
    }

    public void AnimationEvent_DeathFinished()
    {
        if (!isDead) return;
        if (isDeathAnimationFinished) return;
        isDeathAnimationFinished = true;
        StartCorpseFadeIfNeeded();
        if (disableControllerAfterDeathAnimation && !fadeCorpseAfterDeath) enabled = false;
    }

    private void StartCorpseFadeIfNeeded()
    {
        if (!fadeCorpseAfterDeath) return;
        if (corpseFadeStarted) return;
        corpseFadeStarted = true;
        StartCoroutine(FadeOutSpritesCoroutine());
    }

    private IEnumerator FadeOutSpritesCoroutine()
    {
        if (corpseFadeDelaySeconds > 0f) yield return new WaitForSeconds(corpseFadeDelaySeconds);

        var sprites = GetComponentsInChildren<SpriteRenderer>(true);
        if (sprites == null || sprites.Length == 0) yield break;

        var colors = new Color[sprites.Length];
        for (var i = 0; i < sprites.Length; i++)
        {
            colors[i] = sprites[i] != null ? sprites[i].color : Color.white;
        }

        var duration = Mathf.Max(0.01f, corpseFadeDurationSeconds);
        var t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            var k = 1f - Mathf.Clamp01(t / duration);
            for (var i = 0; i < sprites.Length; i++)
            {
                var sr = sprites[i];
                if (sr == null) continue;
                var c = colors[i];
                c.a *= k;
                sr.color = c;
            }
            yield return null;
        }

        if (destroyAfterFade)
        {
            Destroy(gameObject);
            yield break;
        }

        for (var i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] != null) sprites[i].enabled = false;
        }
    }
}
