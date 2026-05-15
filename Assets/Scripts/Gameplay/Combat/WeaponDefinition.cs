using System.Collections;
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
        [Tooltip("命中时施加给目标的击退强度（由目标自己解释：可能是距离或冲量）。为 0 时：不覆盖默认击退（当前默认值在 PlayerCombatController2D 里）。")]
        public float knockback;
        [Tooltip("判定中心距离（沿朝向偏移的距离，单位：世界单位）。计算方式：origin = playerPos + lookDir * range。range 越大，命中圆心离角色越远。")]
        public float range;
        [Tooltip("判定半径（OverlapCircle 半径，单位：世界单位）。以 origin 为圆心、radius 为半径做命中检测。radius 越大，命中范围越大。")]
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
        [Tooltip("命中时施加给目标的击退强度（由目标自己解释：可能是距离或冲量）。优先级：>0 时覆盖 Projectile2D 预制体上的 knockback；为 0 时使用 Projectile2D 的默认 knockback。")]
        public float knockback;
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

public readonly struct DamageInfo
{
    public readonly float Damage;
    public readonly Vector2 SourcePosition;
    public readonly Vector2 Direction;
    public readonly float Knockback;

    public DamageInfo(float damage, Vector2 sourcePosition, Vector2 direction, float knockback)
    {
        Damage = Mathf.Max(0f, damage);
        SourcePosition = sourcePosition;
        Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
        Knockback = Mathf.Max(0f, knockback);
    }
}

public interface IHitReceiver
{
    void ApplyHit(DamageInfo hit);
}

public static class HitReceiverUtil
{
    public static bool TryGet(Component from, out IHitReceiver receiver)
    {
        receiver = null;
        if (from == null) return false;
        var behaviours = from.GetComponentsInParent<MonoBehaviour>(true);
        for (var i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (b is IHitReceiver r)
            {
                receiver = r;
                return true;
            }
        }
        return false;
    }
}

public class HitFeedbackSystem2D : MonoBehaviour
{
    [Header("Hitstop")]
    [SerializeField] private bool enableHitstop = true;
    [SerializeField] private float hitstopTimeScale = 0.05f;

    [Header("Camera Shake")]
    [SerializeField] private bool enableCameraShake = true;
    [SerializeField] private float shakeFrequency = 28f;

    private static HitFeedbackSystem2D instance;
    private Coroutine hitstopRoutine;
    private Coroutine shakeRoutine;

    public static void TriggerHitstop(float durationSeconds)
    {
        var inst = EnsureInstance();
        if (inst == null) return;
        inst.StartHitstop(durationSeconds);
    }

    public static void TriggerCameraShake(float durationSeconds, float magnitude)
    {
        var inst = EnsureInstance();
        if (inst == null) return;
        inst.StartCameraShake(durationSeconds, magnitude);
    }

    private static HitFeedbackSystem2D EnsureInstance()
    {
        if (instance != null) return instance;
        instance = FindObjectOfType<HitFeedbackSystem2D>(true);
        if (instance != null) return instance;

        var go = new GameObject(nameof(HitFeedbackSystem2D));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HitFeedbackSystem2D>();
        return instance;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void StartHitstop(float durationSeconds)
    {
        if (!enableHitstop) return;
        var duration = Mathf.Max(0f, durationSeconds);
        if (duration <= 0f) return;
        if (hitstopRoutine != null) StopCoroutine(hitstopRoutine);
        hitstopRoutine = StartCoroutine(HitstopCoroutine(duration));
    }

    private IEnumerator HitstopCoroutine(float durationSeconds)
    {
        var prevTimeScale = Time.timeScale;
        var prevFixedDelta = Time.fixedDeltaTime;
        var scale = Mathf.Clamp(hitstopTimeScale, 0f, 1f);
        Time.timeScale = scale;
        Time.fixedDeltaTime = prevFixedDelta * scale;

        var end = Time.unscaledTime + durationSeconds;
        while (Time.unscaledTime < end) yield return null;

        Time.timeScale = prevTimeScale;
        Time.fixedDeltaTime = prevFixedDelta;
        hitstopRoutine = null;
    }

    private void StartCameraShake(float durationSeconds, float magnitude)
    {
        if (!enableCameraShake) return;
        var duration = Mathf.Max(0f, durationSeconds);
        if (duration <= 0f) return;
        if (magnitude <= 0f) return;
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(CameraShakeCoroutine(duration, magnitude));
    }

    private IEnumerator CameraShakeCoroutine(float durationSeconds, float magnitude)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            shakeRoutine = null;
            yield break;
        }

        var t = 0f;
        var seed = Random.value * 1000f;
        var tf = cam.transform;
        var basePos = tf.localPosition;
        var freq = Mathf.Max(1f, shakeFrequency);

        while (t < durationSeconds)
        {
            t += Time.unscaledDeltaTime;
            var k = 1f - Mathf.Clamp01(t / Mathf.Max(0.01f, durationSeconds));
            var nx = Mathf.PerlinNoise(seed, Time.unscaledTime * freq) * 2f - 1f;
            var ny = Mathf.PerlinNoise(seed + 11.3f, Time.unscaledTime * freq) * 2f - 1f;
            tf.localPosition = basePos + new Vector3(nx, ny, 0f) * (magnitude * k);
            yield return null;
        }

        tf.localPosition = basePos;
        shakeRoutine = null;
    }
}
