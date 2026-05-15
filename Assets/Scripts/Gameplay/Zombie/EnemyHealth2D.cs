using System;
using Gameplay.Combat;
using UnityEngine;

public class EnemyHealth2D : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [Tooltip("最大生命值。")]
    [SerializeField] protected float maxHp = 60f;
    [Tooltip("当前生命值。<=0 时死亡。")]
    [SerializeField] protected float currentHp = 60f;

    public float Hp => currentHp;
    public float MaxHp => maxHp;
    public bool IsDead => currentHp <= 0f;
    public event Action Died;
    public event Action<float> Damaged;

    protected virtual void Awake()
    {
        maxHp = Mathf.Max(1f, maxHp);
        currentHp = Mathf.Clamp(currentHp <= 0f ? maxHp : currentHp, 0f, maxHp);
    }

    public virtual void ApplyDamage(float amount)
    {
        if (IsDead) return;

        var dmg = ModifyIncomingDamage(amount);
        if (dmg <= 0f) return;

        currentHp = Mathf.Clamp(currentHp - dmg, 0f, maxHp);
        Damaged?.Invoke(dmg);
        OnDamaged(dmg);

        if (!IsDead) return;
        OnDied();
        Died?.Invoke();
    }

    /// <summary>
    /// 修改传入的伤害值。
    /// </summary>
    /// <param name="rawDamage">原始伤害值。</param>
    /// <returns>修改后的伤害值。</returns>
    protected virtual float ModifyIncomingDamage(float rawDamage)
    {
        return Mathf.Max(0f, rawDamage);
    }
    /// <summary>
    /// 敌人被伤害时调用。
    /// </summary>
    /// <param name="appliedDamage">实际伤害值。</param>
    protected virtual void OnDamaged(float appliedDamage)
    {
    }

    /// <summary>
    /// 敌人死亡时调用。
    /// </summary>
    protected virtual void OnDied()
    {
    }
}
