using Gameplay.Combat;
using UnityEngine;

public class DebugDamageableTarget : MonoBehaviour, IDamageable
{
    [SerializeField] private float hp = 100f;

    public void ApplyDamage(float amount)
    {
        hp -= amount;
        Debug.Log($"[Target] {name} took damage={amount}, hp={hp}");
    }
}