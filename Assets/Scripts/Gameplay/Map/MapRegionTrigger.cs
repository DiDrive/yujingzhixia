using UnityEngine;
using UnityEngine.Events;

public enum MapRegionKind
{
    SafeZone,
    LootZone,
    CombatZone,
    StoryZone,
    Custom
}

[RequireComponent(typeof(Collider2D))]
public class MapRegionTrigger : MonoBehaviour
{
    [Tooltip("区域 ID。")]
    [SerializeField] private string regionId;
    [Tooltip("区域类型。")]
    [SerializeField] private MapRegionKind regionKind = MapRegionKind.Custom;
    [Tooltip("进入区域时触发。")]
    [SerializeField] private UnityEvent<Collider2D> onEnter;
    [Tooltip("离开区域时触发。")]
    [SerializeField] private UnityEvent<Collider2D> onExit;

    public string RegionId => string.IsNullOrWhiteSpace(regionId) ? name : regionId;
    public MapRegionKind RegionKind => regionKind;

    private void Reset()
    {
        var trigger = GetComponent<Collider2D>();
        if (trigger != null) trigger.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        onEnter?.Invoke(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        onExit?.Invoke(other);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.55f);
        Gizmos.DrawWireCube(transform.position, transform.lossyScale);
    }
}
