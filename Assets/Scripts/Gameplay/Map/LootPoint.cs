using UnityEngine;

public class LootPoint : MonoBehaviour
{
    [Tooltip("掉落表或物资组 ID。后续可接 LootSpawner 或自定义掉落系统。")]
    [SerializeField] private string lootTableId = "survival_basic";
    [Tooltip("最少生成数量。")]
    [SerializeField] private int minCount = 1;
    [Tooltip("最多生成数量。")]
    [SerializeField] private int maxCount = 3;
    [Tooltip("生成半径。")]
    [SerializeField] private float radius = 0.35f;

    public string LootTableId => lootTableId;
    public int MinCount => Mathf.Max(0, minCount);
    public int MaxCount => Mathf.Max(MinCount, maxCount);
    public float Radius => Mathf.Max(0f, radius);

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.82f, 0.22f, 0.9f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * Mathf.Max(0.25f, Radius * 2f));
    }
}
