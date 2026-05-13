using UnityEngine;

namespace Gameplay.Combat
{
    /// <summary>
    /// 武器攻击类型：近战使用范围检测；远程发射投射物，Melee近战，Ranged远程。
    /// </summary>
    public enum WeaponType
    {
        Melee = 0,      
        Ranged = 1
    }

    /// <summary>
    /// 角色持握姿势分组，用于 Animator 里切换“持刀/持枪/拉弓”等动作集合
    /// </summary>
    public enum WeaponPose
    {
        Unarmed = 0,
        Melee1H = 1,
        Melee2H = 2,
        Pistol = 3,
        Rifle = 4,
        Bow = 5
    }

    /// <summary>
    /// 受伤接口：被近战/投射物命中时调用。
    /// </summary>
    public interface IDamageable
    {
        void ApplyDamage(float amount);
    }

    [System.Serializable]
    public struct MeleeAttackSettings
    {
        [Tooltip("近战基础伤害。")]
        public float damage;
        [Tooltip("攻击冷却（秒）。")]
        public float cooldown;
        [Tooltip("判定中心距离（沿朝向偏移的距离，单位：世界单位）。")]
        public float range;
        [Tooltip("判定半径（OverlapCircle 半径，单位：世界单位）。")]
        public float radius;
        [Tooltip("可命中的层（建议只包含敌人/可破坏物）。")]
        public LayerMask hitMask;
    }

    [System.Serializable]
    public struct RangedAttackSettings
    {
        [Tooltip("远程基础伤害。")]
        public float damage;
        [Tooltip("攻击冷却（秒）。")]
        public float cooldown;
        [Tooltip("要发射的投射物预制体（子弹/箭矢）。")]
        public Projectile2D projectilePrefab;
        [Tooltip("投射物飞行速度（单位：世界单位/秒）。")]
        public float projectileSpeed;
        [Tooltip("投射物存在时间（秒）。")]
        public float projectileLifetime;
        [Tooltip("可命中的层（建议只包含敌人/可破坏物）。")]
        public LayerMask hitMask;
        [Tooltip("为 true 时：按下攻击只触发动画，等动画事件调用 AnimationEvent_FireProjectile 再真正发射。")]
        public bool spawnOnAnimationEvent;
    }

    /// <summary>
    /// 武器定义（ScriptableObject 数据资产）。
    /// 在 Project 里创建该资产并填写参数，然后拖到 PlayerCombatController2D 的 startingWeapon 即可生效。
    /// </summary>
    [CreateAssetMenu(menuName = "Gameplay/Weapon Definition", fileName = "WeaponDefinition")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("武器唯一ID（用于保存/掉落/调试）。")]
        public string weaponId = "weapon";
        [Tooltip("近战或远程。")]
        public WeaponType type = WeaponType.Melee;
        [Tooltip("角色姿势分组：用于 Animator 参数 WeaponPose。")]
        public WeaponPose pose = WeaponPose.Unarmed;

        [Header("Animation")]
        [Tooltip("可选：用于替换角色 Animator 的 Controller（可拖 Animator Controller 或 Animator Override Controller）。为空则不替换。")]
        public RuntimeAnimatorController animatorController;

        [Header("Primary Attack")]
        [Tooltip("近战参数（type = Melee 时使用）。")]
        public MeleeAttackSettings melee;
        [Tooltip("远程参数（type = Ranged 时使用）。")]
        public RangedAttackSettings ranged;
    }
}
