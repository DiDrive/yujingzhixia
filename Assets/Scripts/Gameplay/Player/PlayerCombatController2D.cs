using System;
using UnityEngine;
using Gameplay.Combat;

/// <summary>
/// 2D 角色控制：WASD 移动、鼠标朝向、左键轻击、右键蓄力重击。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerCombatController2D : MonoBehaviour
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

    [Header("Animation")]
    [Tooltip("角色动画器（为空时自动从当前物体获取）。")]
    [SerializeField] private Animator characterAnimator;
    [Tooltip("移动输入很小时，仍保持上一次移动方向用于动画朝向。")]
    [SerializeField] private bool keepLastMoveDirectionForAnimation = true;
    [Tooltip("动画方向平滑时间（秒）。")]
    [SerializeField] private float animatorDirectionDamp = 0.08f;

    private Rigidbody2D rb;
    private Camera mainCamera;
    private Vector2 moveInput;
    private Vector2 moveInputRaw;
    private Vector2 lookDirection = Vector2.right;
    private Vector2 lastMoveDirection = Vector2.down;
    private float nextLightAttackTime;
    private bool isChargingHeavy;
    private float heavyChargeStartTime;
    private WeaponDefinition currentWeapon;
    private bool pendingProjectileShot;
    private bool isDead;
    private bool isDeathAnimationFinished;

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
        mainCamera = Camera.main;
        if (characterAnimator == null) characterAnimator = GetComponent<Animator>();
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
        if (isDead)
        {
            if (rb != null) rb.velocity = Vector2.zero;
            return;
        }
        MoveCharacter();
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
        var velocity = moveInput * moveSpeed * Mathf.Max(0f, moveMultiplier);
        rb.velocity = velocity;
    }

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

        if (characterAnimator != null) characterAnimator.SetTrigger("LightAttack");

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
        if (characterAnimator != null) characterAnimator.SetTrigger("HeavyAttack");
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

        characterAnimator.SetFloat("MoveX", moveDir.x, animatorDirectionDamp, Time.deltaTime);
        characterAnimator.SetFloat("MoveY", moveDir.y, animatorDirectionDamp, Time.deltaTime);
        characterAnimator.SetFloat("Speed", rb != null ? rb.velocity.magnitude : moveInput.magnitude);
        characterAnimator.SetFloat("FacingX", lookDirection.x, animatorDirectionDamp, Time.deltaTime);
        characterAnimator.SetFloat("FacingY", lookDirection.y, animatorDirectionDamp, Time.deltaTime);
        characterAnimator.SetBool("IsMoving", moveInput.sqrMagnitude > 0.0001f);
        characterAnimator.SetBool("IsChargingHeavy", isChargingHeavy);
        characterAnimator.SetFloat("HeavyCharge", HeavyChargeNormalized);
        characterAnimator.SetInteger("MoveDir8", DirectionTo8Way(moveDir));
        characterAnimator.SetInteger("FaceDir8", DirectionTo8Way(lookDirection));
        characterAnimator.SetInteger("WeaponPose", currentWeapon != null ? (int)currentWeapon.pose : 0);
        characterAnimator.SetBool("IsDead", isDead);
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
        var range = CurrentAttackRange;
        var radius = CurrentAttackRadius;
        var hitMask = CurrentHitMask;
        var origin = (Vector2)transform.position + lookDirection * range;
        var hitCount = 0;
        var hits = Physics2D.OverlapCircleAll(origin, radius, hitMask);
        for (var i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;
            if (col.attachedRigidbody != null && col.attachedRigidbody.gameObject == gameObject) continue;
            hitCount++;

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
        var spawn = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;
        var projectile = Instantiate(ranged.projectilePrefab, spawn, Quaternion.identity);
        var dir = SnapToCardinal(lookDirection);
        projectile.SetOwner(transform);
        projectile.Initialize(
            dir,
            Mathf.Max(0.01f, ranged.projectileSpeed),
            Mathf.Max(0.01f, ranged.projectileLifetime),
            Mathf.Max(0f, ranged.damage),
            ranged.hitMask.value == 0 ? defaultHitMask : ranged.hitMask);
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
            characterAnimator.ResetTrigger("LightAttack");
            characterAnimator.ResetTrigger("HeavyAttack");
        }
        return true;
    }

    public void AnimationEvent_DeathFinished()
    {
        if (!isDead) return;
        if (isDeathAnimationFinished) return;
        isDeathAnimationFinished = true;
        if (disableControllerAfterDeathAnimation) enabled = false;
    }
}
