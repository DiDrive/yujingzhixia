using System.Collections;
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
    private ZombieState lastState;
    private float hitStunUntilTime;
    private Coroutine hitFlashRoutine;

    public ZombieState State { get; private set; } = ZombieState.Idle;
    public Transform Target => target;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
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
        if (Time.time < hitStunUntilTime) return;
        ApplyMovement();
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

        if (enableKnockback && rb != null && rb.bodyType == RigidbodyType2D.Dynamic && hit.Knockback > 0f)
        {
            var dir = hit.Direction.sqrMagnitude > 0.0001f
                ? hit.Direction.normalized
                : ((Vector2)transform.position - hit.SourcePosition).normalized;
            rb.AddForce(dir * (hit.Knockback * Mathf.Max(0f, knockbackImpulseScale)), ForceMode2D.Impulse);
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
        if (rb != null) rb.velocity = Vector2.zero;
        if (col != null) col.enabled = false;
        if (animator != null && hasIsDead) animator.SetBool(isDeadParam, true);
        StartCorpseFadeIfNeeded(corpseFadeDelaySeconds);
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
