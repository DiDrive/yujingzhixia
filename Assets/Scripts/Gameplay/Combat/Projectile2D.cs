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

        [Header("Hit")]
        [Tooltip("命中时是否播放受击/破碎动画。")]
        [SerializeField] private bool playHitAnimation = false;
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

        public void Initialize(Vector2 directionNormalized, float speedUnitsPerSecond, float lifetimeSeconds, float damageAmount, LayerMask hitMaskValue)
        {
            direction = directionNormalized.sqrMagnitude > 0.0001f ? directionNormalized.normalized : Vector2.right;
            speed = speedUnitsPerSecond;
            lifetime = lifetimeSeconds;
            damage = damageAmount;
            hitMask = hitMaskValue;

            projectileAnimator = GetComponentInChildren<Animator>(true);
            rb = GetComponent<Rigidbody2D>();

            ApplyDirectionToAnimator();
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
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            projectileAnimator = GetComponentInChildren<Animator>(true);
            triggerCollider = GetComponent<Collider2D>();
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
            if (rb != null) return;
            transform.position += (Vector3)(direction * speed * Time.deltaTime);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (isHit) return;
            if (other == null) return;
            if (ownerRoot != null && other.transform != null && other.transform.IsChildOf(ownerRoot)) return;
            if (((1 << other.gameObject.layer) & hitMask.value) == 0) return;

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
            if (damageable != null)
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
    }
}
