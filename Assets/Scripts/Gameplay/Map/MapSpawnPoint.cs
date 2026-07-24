using UnityEngine;

public enum MapSpawnKind
{
    Player,
    Zombie,
    Neutral
}

public class MapSpawnPoint : MonoBehaviour
{
    [Tooltip("点位 ID，用于脚本查找或策划记录。为空时可直接使用对象名。")]
    [SerializeField] private string spawnId;
    [Tooltip("出生点类型。")]
    [SerializeField] private MapSpawnKind spawnKind = MapSpawnKind.Player;
    [Tooltip("刷出半径。为 0 时精确使用 Transform 位置。")]
    [SerializeField] private float radius = 0.5f;

    public string SpawnId => string.IsNullOrWhiteSpace(spawnId) ? name : spawnId;
    public MapSpawnKind SpawnKind => spawnKind;
    public float Radius => Mathf.Max(0f, radius);

    public Vector3 GetRandomPosition()
    {
        if (Radius <= 0f) return transform.position;
        var offset = Random.insideUnitCircle * Radius;
        return transform.position + new Vector3(offset.x, offset.y, 0f);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = spawnKind switch
        {
            MapSpawnKind.Player => new Color(0.2f, 0.7f, 1f, 0.85f),
            MapSpawnKind.Zombie => new Color(1f, 0.25f, 0.2f, 0.85f),
            _ => new Color(0.9f, 0.9f, 0.9f, 0.85f)
        };
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.1f, Radius));
#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * (Mathf.Max(0.1f, Radius) + 0.15f),
            $"出生点：{SpawnId}\n{spawnKind}");
#endif
    }
}
