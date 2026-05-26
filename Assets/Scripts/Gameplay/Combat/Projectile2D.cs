using UnityEngine;

namespace Gameplay.Combat
{
    /// <summary>
    /// 简单 2D 投射物：按方向直线飞行，命中目标层后对 IDamageable 造成伤害并销毁。
    /// 可在预制体上挂 Animator 来播放飞行动画（序列帧）。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Projectile2D : MonoBehaviour
    {
        private float speed;
        private float lifetime;
        private float damage;
        private LayerMask hitMask;
        [Tooltip("命中后是否立刻销毁投射物。")]
        [SerializeField] private bool destroyOnHit = true;

        [Header("Animation")]
        [Tooltip("可选：投射物动画器（用于按方向切换 Up/Down/Left/Right 动画）。为空时自动查找。")]
        [SerializeField] private Animator projectileAnimator;
        [Tooltip("可选：Animator 的方向参数名 X（Float）。")]
        [SerializeField] private string dirXParam = "MoveX";
        [Tooltip("可选：Animator 的方向参数名 Y（Float）。")]
        [SerializeField] private string dirYParam = "MoveY";
        [Tooltip("是否将方向吸附到 4 方向（Up/Down/Left/Right）后再驱动动画。")]
        [SerializeField] private bool snapDirectionTo4WayForAnimation = true;

        [Header("Rendering")]
        [Tooltip("为 true 时：投射物会自动继承发射者当前楼层的 Actor_Fx Sorting Layer，避免上楼后仍停留在旧楼层的 fly 层里。")]
        [SerializeField] private bool syncSortingWithOwnerFloor = true;
        [Tooltip("按 Y 计算排序时，每 1 个世界单位对应多少 sortingOrder。建议与 SpriteSortByY 保持一致。")]
        [SerializeField] private int sortingOrderPerUnit = 100;
        [Tooltip("投射物排序的额外偏移。可用于让投射物略微显示在角色前面。")]
        [SerializeField] private int sortingOrderOffset = 5;

        [Header("Hit")]
        [Tooltip("命中时是否播放受击/破碎动画。")]
        [SerializeField] private bool playHitAnimation = false;
        [Tooltip("发射后的短暂保护时间（秒）。在这段时间内忽略墙体命中，避免生成点贴墙时刚出生就撞毁。")]
        [SerializeField] private float wallHitGraceSeconds = 0.06f;
        [Tooltip("发射后至少飞行多少距离才允许撞到墙体。用于避免二楼/楼梯附近生成点轻微嵌进墙体时立即命中。")]
        [SerializeField] private float wallHitGraceDistance = 0.12f;
        [Tooltip("命中时对目标施加的击退强度（由目标自己解释：可能是距离或冲量）。如果发射该投射物的 WeaponDefinition.ranged.knockback > 0，则会覆盖此默认值。")]
        [SerializeField] private float knockback = 0.4f;
        [Tooltip("命中时触发的动画 Trigger 名称。")]
        [SerializeField] private string hitTrigger = "Hit";
        [Tooltip("命中动画播放时长（秒），到时间后销毁投射物。")]
        [SerializeField] private float hitDespawnDelay = 0.12f;
        [Tooltip("命中时将投射物位置回退一点，避免受击/破碎动画嵌进目标内部。")]
        [SerializeField] private float hitBackoffDistance = 0.02f;

        private Rigidbody2D rb;
        private Vector2 direction = Vector2.right;
        private Collider2D triggerCollider;
        private bool isHit;
        private Transform ownerRoot;
        private Collider2D[] ownerColliders;
        private Vector3 spawnPosition;
        private float spawnTime;
        private SpriteRenderer[] projectileRenderers;

        public void Initialize(Vector2 directionNormalized, float speedUnitsPerSecond, float lifetimeSeconds, float damageAmount, float knockbackOverride, LayerMask hitMaskValue)
        {
            direction = directionNormalized.sqrMagnitude > 0.0001f ? directionNormalized.normalized : Vector2.right;
            speed = speedUnitsPerSecond;
            lifetime = lifetimeSeconds;
            damage = damageAmount;
            // 击退优先级：WeaponDefinition.ranged.knockback > 0 时覆盖 Projectile2D 预制体上的 knockback；否则使用 Projectile2D 的默认 knockback。
            if (knockbackOverride > 0f) knockback = knockbackOverride;
            hitMask = hitMaskValue;
            spawnPosition = transform.position;
            spawnTime = Time.time;

            projectileAnimator = GetComponentInChildren<Animator>(true);
            rb = GetComponent<Rigidbody2D>();

            ApplyDirectionToAnimator();
            ApplyRenderingSorting();
            ApplyVelocity();
            RescheduleDespawn();
        }

        public void SetOwner(Transform owner)
        {
            ownerRoot = owner;
            ownerColliders = ownerRoot != null ? ownerRoot.GetComponentsInChildren<Collider2D>(true) : null;

            if (triggerCollider == null || ownerColliders == null) return;
            for (var i = 0; i < ownerColliders.Length; i++)
            {
                var col = ownerColliders[i];
                if (col == null) continue;
                Physics2D.IgnoreCollision(triggerCollider, col, true);
            }

            ApplyRenderingSorting();
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            projectileAnimator = GetComponentInChildren<Animator>(true);
            triggerCollider = GetComponent<Collider2D>();
            projectileRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        private void OnEnable()
        {
            RescheduleDespawn();
        }

        private void OnDisable()
        {
            CancelInvoke();
        }

        private void Start()
        {
            ApplyVelocity();
        }

        private void Update()
        {
            ApplyRenderingSorting();
            if (rb != null) return;
            transform.position += (Vector3)(direction * speed * Time.deltaTime);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (isHit) return;
            if (other == null) return;
            if (ownerRoot != null && other.transform != null && other.transform.IsChildOf(ownerRoot)) return;
            if (((1 << other.gameObject.layer) & hitMask.value) == 0) return;
            if (ownerRoot != null && !FloorLayerUtility.AreOnSameFloor(ownerRoot, other.transform)) return;
            if (ownerRoot != null && IsActorTarget(other.transform) && !FloorLayerUtility.AreHostile(ownerRoot, other.transform)) return;
            if (ShouldIgnoreWallHit(other)) return;

            var closest = other.ClosestPoint(transform.position);
            if (hitBackoffDistance > 0f)
            {
                transform.position = (Vector3)closest - (Vector3)(direction * hitBackoffDistance);
            }
            else
            {
                transform.position = closest;
            }

            var damageable = other.GetComponentInParent<IDamageable>();
            if (global::HitReceiverUtil.TryGet(other, out var hitReceiver))
            {
                hitReceiver.ApplyHit(new global::DamageInfo(damage, transform.position, direction, knockback));
            }
            else if (damageable != null)
            {
                damageable.ApplyDamage(damage);
            }

            if (playHitAnimation && projectileAnimator != null && !string.IsNullOrWhiteSpace(hitTrigger))
            {
                isHit = true;
                if (triggerCollider != null) triggerCollider.enabled = false;
                if (rb != null) rb.velocity = Vector2.zero;
                CancelInvoke();
                projectileAnimator.SetTrigger(hitTrigger);
                Invoke(nameof(Despawn), Mathf.Max(0.01f, hitDespawnDelay));
                return;
            }

            if (destroyOnHit) Despawn();
        }

        private void Despawn()
        {
            Destroy(gameObject);
        }

        private void RescheduleDespawn()
        {
            CancelInvoke();
            if (lifetime > 0f) Invoke(nameof(Despawn), lifetime);
        }

        private void ApplyVelocity()
        {
            if (rb == null) return;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.velocity = direction * speed;
        }

        private void ApplyDirectionToAnimator()
        {
            if (projectileAnimator == null) return;
            if (string.IsNullOrWhiteSpace(dirXParam) || string.IsNullOrWhiteSpace(dirYParam)) return;
            var d = snapDirectionTo4WayForAnimation ? SnapToCardinal(direction) : direction;
            projectileAnimator.SetFloat(dirXParam, d.x);
            projectileAnimator.SetFloat(dirYParam, d.y);
        }

        private static Vector2 SnapToCardinal(Vector2 dir)
        {
            if (dir.sqrMagnitude <= 0.0001f) return Vector2.right;
            return Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
                ? new Vector2(Mathf.Sign(dir.x), 0f)
                : new Vector2(0f, Mathf.Sign(dir.y));
        }

        private static bool IsActorTarget(Transform target)
        {
            if (target == null) return false;
            return target.GetComponentInParent<PlayerCombatController2D>() != null ||
                   target.GetComponentInParent<ZombieController2D>() != null;
        }

        private bool ShouldIgnoreWallHit(Collider2D other)
        {
            if (!FloorLayerUtility.IsWallLayer(other.gameObject.layer)) return false;

            var elapsed = Time.time - spawnTime;
            var travelled = Vector2.Distance(spawnPosition, transform.position);
            return elapsed < Mathf.Max(0f, wallHitGraceSeconds) || travelled < Mathf.Max(0f, wallHitGraceDistance);
        }

        private void ApplyRenderingSorting()
        {
            if (!syncSortingWithOwnerFloor) return;
            if (projectileRenderers == null || projectileRenderers.Length == 0) return;

            var floorIndex = ownerRoot != null ? FloorLayerUtility.GetFloorIndex(ownerRoot) : -1;
            if (floorIndex < 0) return;

            var sortingLayerName = FloorLayerUtility.GetActorSortingLayerName(floorIndex);
            var order = sortingOrderOffset - Mathf.RoundToInt(transform.position.y * Mathf.Max(1, sortingOrderPerUnit));
            for (var i = 0; i < projectileRenderers.Length; i++)
            {
                var renderer = projectileRenderers[i];
                if (renderer == null) continue;
                renderer.sortingLayerName = sortingLayerName;
                renderer.sortingOrder = order;
            }
        }
    }
}
