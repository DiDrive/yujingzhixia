using UnityEngine;

public abstract class EnemyAttack2D : MonoBehaviour
{
    /// <summary>
    /// 进入攻击状态所需的距离阈值（通常：distance <= AttackRange 时攻击）。
    /// </summary>
    public abstract float AttackRange { get; }

    /// <summary>
    /// 是否希望开始攻击。默认使用 AttackRange，可覆盖实现更复杂的判断（夹角/视野/冷却/蓄力等）。
    /// </summary>
    public virtual bool WantsToAttack(float distance) => distance <= Mathf.Max(0.01f, AttackRange);

    /// <summary>
    /// 每帧在 Attack 状态下调用。典型行为：检查冷却、触发动画或发射投射物。
    /// </summary>
    public abstract void TickAttack(Transform self, Transform target);

    /// <summary>
    /// 攻击动画命中帧事件回调。用于“按动画帧结算伤害/生成特效”。
    /// </summary>
    public abstract void OnAnimationEvent_AttackHit(Transform self, Transform target);

    /// <summary>
    /// 是否处于“攻击锁定期”。为 true 时建议保持 Attack 状态，不要切回 Chase（避免边攻击边追逐）。
    /// </summary>
    public virtual bool IsAttacking => false;
}
