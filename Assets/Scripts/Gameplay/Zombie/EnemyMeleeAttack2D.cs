using Gameplay.Combat;
using UnityEngine;

public class EnemyMeleeAttack2D : EnemyAttack2D
{
    [Header("Melee")]
    [Tooltip("近战攻击距离。距离小于等于该值时进入攻击状态。")]
    [SerializeField] private float attackRange = 1.1f;
    [Tooltip("每次攻击造成的伤害。目标需要实现 IDamageable 才会扣血。")]
    [SerializeField] private float attackDamage = 10f;
    [Tooltip("攻击冷却（秒）。")]
    [SerializeField] private float attackCooldownSeconds = 0.9f;
    [Tooltip("命中时对目标施加的击退强度（由目标自己解释：可能是距离或冲量）。")]
    [SerializeField] private float knockback = 0.7f;
    [Tooltip("为 true 时：只在动画事件命中帧结算伤害；为 false 时：触发攻击时立刻结算伤害。")]
    [SerializeField] private bool dealDamageOnAnimationEvent = true;

    [Header("Animation")]
    [Tooltip("攻击动画所在的 Animator。为空时会自动从子物体中查找。")]
    [SerializeField] private Animator animator;
    [Tooltip("触发攻击动画的 Trigger 名称。")]
    [SerializeField] private string attackTrigger = "Attack";
    [Tooltip("当依赖动画事件结算伤害但动画没配置事件时，备用的命中延迟（秒）。")]
    [SerializeField] private float fallbackHitDelaySeconds = 0.2f;
    [Tooltip("攻击开始后锁定在 Attack 状态的时间（秒）。用于避免玩家后退时出现“边追边砍”的观感。")]
    [SerializeField] private float attackLockSeconds = 0.35f;

    private float nextAttackTime;
    private bool pendingDamage;
    private Transform pendingTarget;
    private Transform pendingSelf;
    private bool hasAttackTrigger;
    private float attackLockUntilTime;

    public override float AttackRange => attackRange;
    public override bool IsAttacking => Time.time < attackLockUntilTime;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        hasAttackTrigger = animator != null && AnimatorParamUtil2D.HasParameter(animator, attackTrigger, AnimatorControllerParameterType.Trigger);
    }

    public override void TickAttack(Transform self, Transform target)
    {
        if (target == null) return;
        if (Vector2.Distance(self.position, target.position) > attackRange) return;
        if (Time.time < nextAttackTime) return;
        nextAttackTime = Time.time + Mathf.Max(0.01f, attackCooldownSeconds);
        attackLockUntilTime = Time.time + Mathf.Max(0.01f, attackLockSeconds);

        CancelInvoke(nameof(DealPendingDamage));
        pendingDamage = true;
        pendingTarget = target;
        pendingSelf = self;
        if (animator != null && hasAttackTrigger)
        {
            animator.SetTrigger(attackTrigger);
            if (!dealDamageOnAnimationEvent)
            {
                DealPendingDamage();
                return;
            }

            Invoke(nameof(DealPendingDamage), Mathf.Max(0.01f, fallbackHitDelaySeconds));
            return;
        }

        DealPendingDamage();
    }

    public override void OnAnimationEvent_AttackHit(Transform self, Transform target)
    {
        CancelInvoke(nameof(DealPendingDamage));
        DealPendingDamage();
    }

    private void DealPendingDamage()
    {
        if (!pendingDamage) return;
        pendingDamage = false;
        if (pendingTarget == null) return;
        if (pendingSelf == null) return;

        var player = pendingTarget.GetComponentInParent<PlayerCombatController2D>();
        if (player != null && player.IsDead) return;
        if (Vector2.Distance(pendingSelf.position, pendingTarget.position) > attackRange) return;

        if (global::HitReceiverUtil.TryGet(pendingTarget, out var hitReceiver))
        {
            var dir = (Vector2)(pendingTarget.position - pendingSelf.position);
            hitReceiver.ApplyHit(new global::DamageInfo(attackDamage, pendingSelf.position, dir, knockback));
            return;
        }

        var damageable = pendingTarget.GetComponentInParent<IDamageable>();
        if (damageable == null) damageable = pendingTarget.GetComponentInChildren<IDamageable>();
        if (damageable == null) return;
        damageable.ApplyDamage(attackDamage);
    }
}
