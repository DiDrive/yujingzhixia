using UnityEngine;

public abstract class EnemyMovement2D : MonoBehaviour
{
    /// <summary>
    /// 追击时返回希望的速度（写给 Rigidbody2D.velocity）。
    /// </summary>
    public abstract Vector2 GetVelocity(Transform self, Transform target, Vector2 toTarget, float distance);
}
