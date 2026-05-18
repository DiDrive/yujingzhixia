using UnityEngine;

public class ExtractionPoint : MonoBehaviour
{
    [Tooltip("撤离点 ID。")]
    [SerializeField] private string extractionId;
    [Tooltip("撤离后进入的目标场景或流程 ID。")]
    [SerializeField] private string targetId = "Shelter";
    [Tooltip("触发半径。")]
    [SerializeField] private float radius = 1f;

    public string ExtractionId => string.IsNullOrWhiteSpace(extractionId) ? name : extractionId;
    public string TargetId => targetId;
    public float Radius => Mathf.Max(0f, radius);

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.25f, 1f, 0.45f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.25f, Radius));
    }
}
