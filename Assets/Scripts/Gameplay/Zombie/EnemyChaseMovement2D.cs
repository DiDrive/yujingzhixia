using UnityEngine;

public class EnemyChaseMovement2D : EnemyMovement2D
{
    [Header("Move")]
    [Tooltip("追击移动速度。")]
    [SerializeField] private float moveSpeed = 1.8f;
    [Tooltip("停止距离。小于等于该距离时返回 0 速度，避免贴脸推挤玩家。建议略大于攻击距离或两者碰撞体半径之和。")]
    [SerializeField] private float stopDistance = 1.0f;

    public override Vector2 GetVelocity(Transform self, Transform target, Vector2 toTarget, float distance)
    {
        if (distance <= 0.0001f) return Vector2.zero;
        if (stopDistance > 0f && distance <= stopDistance) return Vector2.zero;
        return toTarget.normalized * Mathf.Max(0f, moveSpeed);
    }
}
