using System.Collections;
using System;
using Gameplay.Combat;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class ZombieController2D : MonoBehaviour, IDamageable, global::IHitReceiver
{
    /// <summary>
    /// 僵尸的高层状态：由与目标距离/是否可攻击等条件驱动。
    /// </summary>
    public enum ZombieState
    {
        Idle = 0,
        Chase = 1,
        Attack = 2,
        Dead = 3
    }

    [Header("Target")]
    [Tooltip("攻击/追击的目标。为空且启用自动查找时，会在场景中查找 PlayerCombatController2D。")]
    [SerializeField] private Transform target;
    [Tooltip("进入探测范围后才会追击/攻击；超出该范围回到 Idle。")]
    [SerializeField] private float detectRange = 6f;
    [Tooltip("为 true 时：target 为空会自动查找场景中的 PlayerCombatController2D 作为目标。")]
    [SerializeField] private bool autoFindPlayerTarget = true;
    [Tooltip("为 true 时：只有与目标之间没有墙体遮挡时，僵尸才会锁定、追击和攻击。")]
    [SerializeField] private bool requireLineOfSight = true;
    [Tooltip("阻挡视线的基础层。运行时会自动补当前楼层的 Wall_Fx。建议至少勾选 Wall。")]
    [SerializeField] private LayerMask lineOfSightBlockMask = ~0;

    [Header("Animation")]
    [Tooltip("角色 Animator。为空时会自动从子物体中查找。")]
    [SerializeField] private Animator animator;
    [Tooltip("Animator 的方向参数 X（Float）。用于驱动上下左右/8方向移动动画。")]
    [SerializeField] private string moveXParam = "MoveX";
    [Tooltip("Animator 的方向参数 Y（Float）。用于驱动上下左右/8方向移动动画。")]
    [SerializeField] private string moveYParam = "MoveY";
    [Tooltip("Animator 的移动标记（Bool）。为 true 时进入移动动画。")]
    [SerializeField] private string isMovingParam = "IsMoving";
    [Tooltip("Animator 的死亡标记（Bool）。为 true 时进入死亡动画。")]
    [SerializeField] private string isDeadParam = "IsDead";
    [Tooltip("Animator 的受击触发器（Trigger）。命中时触发，用于播放受击/硬直动画。为空则不触发。")]
    [SerializeField] private string hitTriggerParam = "Hit";

    [Header("Behaviours")]
    [Tooltip("为 true 时：如果未挂载移动/攻击/血量模块，会在运行时自动添加默认实现（追击移动/近战攻击/基础血量）。")]
    [SerializeField] private bool autoAddDefaultBehaviours = true;
    [Tooltip("移动行为模块：决定追击时如何计算速度（直线追击/巡逻/绕路/冲刺等）。可为空；为空时默认直线朝目标移动。")]
    [SerializeField] private EnemyMovement2D movement;
    [Tooltip("攻击行为模块：决定何时攻击、播放什么攻击动画、如何结算伤害（近战/远程/范围等）。建议必挂一个具体实现（如 EnemyMeleeAttack2D）。")]
    [SerializeField] private EnemyAttack2D attack;
    [Tooltip("生命值模块：负责HP与死亡事件。建议必挂（如 EnemyHealth2D）；为空时本体受到任何伤害会直接死亡。")]
    [SerializeField] private EnemyHealth2D health;
    [Tooltip("死亡后是否让尸体逐渐隐去。")]
    [SerializeField] private bool fadeCorpseAfterDeath = true;
    [Tooltip("死亡触发后延迟多少秒开始淡出（可设为死亡动画时长）。")]
    [SerializeField] private float corpseFadeDelaySeconds = 0.8f;
    [Tooltip("淡出持续时间（秒）。")]
    [SerializeField] private float corpseFadeDurationSeconds = 1.2f;
    [Tooltip("淡出结束后是否销毁物体。为 false 时仅隐藏 SpriteRenderer。")]
    [SerializeField] private bool destroyAfterFade = false;

    [Header("Physics")]
    [Tooltip("切换到 Attack/Idle 时是否立刻清零速度，避免出现“攻击时滑动”。")]
    [SerializeField] private bool stopImmediatelyWhenNotChasing = true;
    [Tooltip("把僵尸刚体切换为 Kinematic，彻底禁止玩家通过普通身体碰撞推动僵尸。AI 移动和武器击退仍由脚本驱动。")]
    [SerializeField] private bool preventCharacterPushing = true;
    [Tooltip("是否启用受击击退。")]
    [SerializeField] private bool enableKnockback = true;
    [Tooltip("击退强度缩放（将 DamageInfo.Knockback 转成 Rigidbody2D 的冲量）。")]
    [SerializeField] private float knockbackImpulseScale = 6f;
    [Tooltip("受击后硬直时长（秒）。硬直期间不追击不攻击。")]
    [SerializeField] private float hitStunSeconds = 0.12f;
    [Tooltip("是否启用受击闪白。")]
    [SerializeField] private bool enableHitFlash = true;
    [Tooltip("受击闪白颜色。")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.45f, 0.45f, 1f);
    [Tooltip("受击闪白持续时间（秒）。")]
    [SerializeField] private float hitFlashDurationSeconds = 0.08f;
    [Tooltip("受击时是否触发 Hitstop。")]
    [SerializeField] private bool enableHitstop = true;
    [Tooltip("Hitstop 时长（秒）。")]
    [SerializeField] private float hitstopDurationSeconds = 0.05f;
    [Tooltip("受击时是否触发相机震动。")]
    [SerializeField] private bool enableCameraShake = true;
    [Tooltip("相机震动时长（秒）。")]
    [SerializeField] private float cameraShakeDurationSeconds = 0.08f;
    [Tooltip("相机震动强度。")]
    [SerializeField] private float cameraShakeMagnitude = 0.12f;

    private Rigidbody2D rb;
    private Collider2D col;
    private Vector2 lastMoveDir = Vector2.down;
    private Vector2 currentMoveVelocity;
    private bool hasMoveX;
    private bool hasMoveY;
    private bool hasIsMoving;
    private bool hasIsDead;
    private bool hasHitTrigger;
    private bool corpseFadeStarted;
    private bool corpseFadeBlockedForLoot;
    private ZombieState lastState;
    private float hitStunUntilTime;
    private float scriptedKnockbackUntilTime;
    private Vector2 scriptedKnockbackVelocity;
    private Coroutine hitFlashRoutine;
    private SpriteRenderer[] hitFlashSprites;
    private Color[] hitFlashOriginalColors;

    public ZombieState State { get; private set; } = ZombieState.Idle;
    public Transform Target => target;
    /// <summary>僵尸第一次进入死亡状态时触发，供尸体掉落等外部系统接入。</summary>
    public event Action Died;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        // Kinematic 刚体仍可由 velocity 驱动和阻挡 Dynamic 玩家，但不会接受玩家碰撞产生的反向冲量。
        if (preventCharacterPushing && rb != null) rb.bodyType = RigidbodyType2D.Kinematic;
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (movement == null) movement = GetComponent<EnemyMovement2D>();
        // 这里做“自动装配”：你既可以手动拖引用，也可以直接把模块挂在同一个僵尸物体上让它自动找到。
        if (attack == null) attack = GetComponent<EnemyAttack2D>();
        if (health == null) health = GetComponent<EnemyHealth2D>();
        if (autoAddDefaultBehaviours)
        {
            if (movement == null) movement = gameObject.AddComponent<EnemyChaseMovement2D>();
            if (attack == null) attack = gameObject.AddComponent<EnemyMeleeAttack2D>();
            if (health == null) health = gameObject.AddComponent<EnemyHealth2D>();
        }
        if (health != null) health.Died += HandleDied;
        if (health != null) health.Damaged += HandleDamaged;

        CacheAnimatorParams();
        lastState = State;
    }

    private void OnEnable()
    {
        EnsureUnpushableBodyType();
    }


    private void Update()
    {
        if (State == ZombieState.Dead) return;
        if (Time.time < hitStunUntilTime)
        {
            State = ZombieState.Idle;
            UpdateAnimator();
            lastState = State;
            return;
        }
        EnsureTarget();
        TickState();
        if (stopImmediatelyWhenNotChasing && lastState != State && State != ZombieState.Chase && rb != null) rb.velocity = Vector2.zero;
        if (State == ZombieState.Attack && attack != null) attack.TickAttack(transform, target);
        UpdateAnimator();
        lastState = State;
    }

    private void FixedUpdate()
    {
        if (State == ZombieState.Dead) return;
        EnsureUnpushableBodyType();

        // Kinematic 模式下用短时脚本速度表达武器击退，避免依赖 Dynamic 刚体的 AddForce。
        if (Time.time < scriptedKnockbackUntilTime)
        {
            if (rb != null) rb.velocity = scriptedKnockbackVelocity;
            return;
        }
        if (scriptedKnockbackVelocity.sqrMagnitude > 0f)
        {
            scriptedKnockbackVelocity = Vector2.zero;
            if (rb != null) rb.velocity = Vector2.zero;
        }
        if (Time.time < hitStunUntilTime) return;
        ApplyMovement();
    }

    /// <summary>防止 Prefab/场景覆盖或其他初始化代码把僵尸重新改回可被推动的 Dynamic 刚体。</summary>
    private void EnsureUnpushableBodyType()
    {
        if (!preventCharacterPushing || rb == null) return;
        if (rb.bodyType != RigidbodyType2D.Kinematic) rb.bodyType = RigidbodyType2D.Kinematic;
    }

    private void EnsureTarget()
    {
        if (target != null) return;
        if (!autoFindPlayerTarget) return;
        var player = FindObjectOfType<PlayerCombatController2D>(true);
        if (player != null && !player.IsDead) target = player.transform;
    }

    private void TickState()
    {
        if (target == null)
        {
            State = ZombieState.Idle;
            return;
        }

        var player = target.GetComponentInParent<PlayerCombatController2D>();
        if (player != null && player.IsDead)
        {
            State = ZombieState.Idle;
            return;
        }

        if (!FloorLayerUtility.AreOnSameFloor(transform, target))
        {
            State = ZombieState.Idle;
            return;
        }

        if (requireLineOfSight && !HasLineOfSight(target))
        {
            State = ZombieState.Idle;
            return;
        }

        var dist = Vector2.Distance(transform.position, target.position);
        if (dist > detectRange)
        {
            State = ZombieState.Idle;
            return;
        }

        if (attack != null && attack.IsAttacking)
        {
            State = ZombieState.Attack;
            return;
        }

        if (attack != null && attack.WantsToAttack(dist))
        {
            State = ZombieState.Attack;
            return;
        }

        State = ZombieState.Chase;
    }

    private bool HasLineOfSight(Transform targetTransform)
    {
        if (targetTransform == null) return false;

        var blockerMask = FloorLayerUtility.GetSightBlockMaskForCurrentFloor(transform, lineOfSightBlockMask);
        if (blockerMask.value == 0) return true;

        var origin = (Vector2)transform.position;
        var destination = (Vector2)targetTransform.position;
        var hits = Physics2D.LinecastAll(origin, destination, blockerMask);
        for (var i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null) continue;
            if (hits[i].transform == null) continue;
            if (hits[i].transform.IsChildOf(transform)) continue;
            if (hits[i].transform.IsChildOf(targetTransform)) continue;
            return false;
        }

        return true;
    }

    private void ApplyMovement()
    {
        if (rb == null) return;
        if (State != ZombieState.Chase || target == null)
        {
            rb.velocity = Vector2.zero;
            currentMoveVelocity = Vector2.zero;
            return;
        }

        var delta = (Vector2)(target.position - transform.position);
        var dist = delta.magnitude;
        if (dist <= 0.0001f)
        {
            rb.velocity = Vector2.zero;
            return;
        }

        var velocity = movement != null
            ? movement.GetVelocity(transform, target, delta, dist)
            : delta.normalized;

        rb.velocity = velocity;
        if (rb.velocity.sqrMagnitude > 0.0001f) lastMoveDir = rb.velocity.normalized;
        currentMoveVelocity = rb.velocity;
    }

    public void AnimationEvent_AttackHit()
    {
        if (State == ZombieState.Dead) return;
        if (attack == null) return;
        if (target == null) return;
        attack.OnAnimationEvent_AttackHit(transform, target);
    }

    public void AnimationEvent_DeathFinished()
    {
        if (State != ZombieState.Dead) return;
        StartCorpseFadeIfNeeded(0f);
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;

        var moving = State == ZombieState.Chase && currentMoveVelocity.sqrMagnitude > 0.0001f;
        var facing = lastMoveDir;
        if (State == ZombieState.Attack && target != null)
        {
            var d = (Vector2)(target.position - transform.position);
            if (d.sqrMagnitude > 0.0001f) facing = d.normalized;
        }

        if (hasMoveX) animator.SetFloat(moveXParam, facing.x);
        if (hasMoveY) animator.SetFloat(moveYParam, facing.y);
        if (hasIsMoving) animator.SetBool(isMovingParam, moving);
        if (hasIsDead) animator.SetBool(isDeadParam, State == ZombieState.Dead);
    }

    public void ApplyDamage(float amount)
    {
        if (State == ZombieState.Dead) return;
        if (health != null)
        {
            health.ApplyDamage(amount);
            if (health.IsDead) HandleDied();
            return;
        }

        HandleDied();
    }

    public void ApplyHit(global::DamageInfo hit)
    {
        if (State == ZombieState.Dead) return;
        ApplyDamage(hit.Damage);
        if (State == ZombieState.Dead) return;

        if (enableKnockback && rb != null && hit.Knockback > 0f)
        {
            var dir = hit.Direction.sqrMagnitude > 0.0001f
                ? hit.Direction.normalized
                : ((Vector2)transform.position - hit.SourcePosition).normalized;
            if (preventCharacterPushing || rb.bodyType == RigidbodyType2D.Kinematic)
            {
                scriptedKnockbackVelocity = dir * (hit.Knockback * Mathf.Max(0f, knockbackImpulseScale));
                scriptedKnockbackUntilTime = Time.time + Mathf.Max(0.02f, hitStunSeconds);
            }
            else if (rb.bodyType == RigidbodyType2D.Dynamic)
            {
                rb.AddForce(
                    dir * (hit.Knockback * Mathf.Max(0f, knockbackImpulseScale)),
                    ForceMode2D.Impulse);
            }
        }

        if (hitStunSeconds > 0f) hitStunUntilTime = Mathf.Max(hitStunUntilTime, Time.time + hitStunSeconds);
        if (enableHitFlash) StartHitFlash();
        if (enableHitstop) global::HitFeedbackSystem2D.TriggerHitstop(hitstopDurationSeconds);
        if (enableCameraShake) global::HitFeedbackSystem2D.TriggerCameraShake(cameraShakeDurationSeconds, cameraShakeMagnitude);
    }

    private void HandleDied()
    {
        if (State == ZombieState.Dead) return;
        State = ZombieState.Dead;
        if (movement != null) movement.enabled = false;
        if (attack != null) attack.enabled = false;
        scriptedKnockbackVelocity = Vector2.zero;
        scriptedKnockbackUntilTime = 0f;
        if (rb != null) rb.velocity = Vector2.zero;
        if (col != null) col.enabled = false;
        // 死亡前若仍处于受击闪红，必须先恢复基础颜色，避免死亡动画和尸体永久继承红色。
        CancelHitFlashAndRestoreColors();
        if (animator != null && hasIsDead) animator.SetBool(isDeadParam, true);
        Died?.Invoke();
        StartCorpseFadeIfNeeded(corpseFadeDelaySeconds);
    }

    /// <summary>
    /// 尸体仍可搜索或仍有物品时阻止自动淡出。尸体容器应在僵尸死亡前调用本方法。
    /// </summary>
    public void BlockCorpseFadeForLoot()
    {
        corpseFadeBlockedForLoot = true;
    }

    /// <summary>
    /// 尸体被搜空并关闭 UI 后恢复淡出。可选择在淡出结束时销毁整个僵尸对象。
    /// </summary>
    public void ReleaseCorpseFadeAfterLoot(float delaySeconds, bool destroyWhenFinished)
    {
        corpseFadeBlockedForLoot = false;
        destroyAfterFade = destroyWhenFinished;
        StartCorpseFadeIfNeeded(Mathf.Max(0f, delaySeconds));
    }
    

    private void HandleDamaged(float amount)
    {
        if (State == ZombieState.Dead) return;
        if (animator == null) return;
        if (!hasHitTrigger) return;
        animator.SetTrigger(hitTriggerParam);
    }

    private void StartHitFlash()
    {
        // 连续受击时先恢复上一次缓存颜色，禁止把“红色”再次缓存成新的基础颜色。
        CancelHitFlashAndRestoreColors();

        hitFlashSprites = GetComponentsInChildren<SpriteRenderer>(true);
        if (hitFlashSprites == null || hitFlashSprites.Length == 0) return;

        hitFlashOriginalColors = new Color[hitFlashSprites.Length];
        for (var i = 0; i < hitFlashSprites.Length; i++)
        {
            var sprite = hitFlashSprites[i];
            hitFlashOriginalColors[i] = sprite != null ? sprite.color : Color.white;
            if (sprite != null) sprite.color = hitFlashColor;
        }

        hitFlashRoutine = StartCoroutine(HitFlashCoroutine());
    }

    private IEnumerator HitFlashCoroutine()
    {
        yield return new WaitForSeconds(Mathf.Max(0.01f, hitFlashDurationSeconds));
        RestoreHitFlashColors();
        hitFlashRoutine = null;
    }

    /// <summary>中断受击闪烁并恢复闪烁开始前的 Renderer 颜色。</summary>
    private void CancelHitFlashAndRestoreColors()
    {
        if (hitFlashRoutine != null) StopCoroutine(hitFlashRoutine);
        hitFlashRoutine = null;
        RestoreHitFlashColors();
    }

    private void RestoreHitFlashColors()
    {
        if (hitFlashSprites != null && hitFlashOriginalColors != null)
        {
            var count = Mathf.Min(hitFlashSprites.Length, hitFlashOriginalColors.Length);
            for (var i = 0; i < count; i++)
            {
                if (hitFlashSprites[i] != null) hitFlashSprites[i].color = hitFlashOriginalColors[i];
            }
        }

        hitFlashSprites = null;
        hitFlashOriginalColors = null;
    }

    private void CacheAnimatorParams()
    {
        if (animator == null) return;
        hasMoveX = AnimatorParamUtil2D.HasParameter(animator, moveXParam, AnimatorControllerParameterType.Float);
        hasMoveY = AnimatorParamUtil2D.HasParameter(animator, moveYParam, AnimatorControllerParameterType.Float);
        hasIsMoving = AnimatorParamUtil2D.HasParameter(animator, isMovingParam, AnimatorControllerParameterType.Bool);
        hasIsDead = AnimatorParamUtil2D.HasParameter(animator, isDeadParam, AnimatorControllerParameterType.Bool);
        hasHitTrigger = AnimatorParamUtil2D.HasParameter(animator, hitTriggerParam, AnimatorControllerParameterType.Trigger);
    }

    private void StartCorpseFadeIfNeeded(float delaySeconds)
    {
        if (!fadeCorpseAfterDeath) return;
        if (corpseFadeBlockedForLoot) return;
        if (corpseFadeStarted) return;
        corpseFadeStarted = true;
        StartCoroutine(FadeOutSpritesCoroutine(Mathf.Max(0f, delaySeconds)));
    }

    private IEnumerator FadeOutSpritesCoroutine(float delaySeconds)
    {
        if (delaySeconds > 0f) yield return new WaitForSeconds(delaySeconds);

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
