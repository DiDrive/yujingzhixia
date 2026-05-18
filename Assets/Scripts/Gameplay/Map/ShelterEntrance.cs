using UnityEngine;

public class ShelterEntrance : MonoBehaviour
{
    [Tooltip("庇护所入口 ID。")]
    [SerializeField] private string entranceId = "shelter_main";
    [Tooltip("交互半径。")]
    [SerializeField] private float radius = 1f;

    public string EntranceId => entranceId;
    public float Radius => Mathf.Max(0f, radius);

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.65f, 0.45f, 1f, 0.9f);
        Gizmos.DrawWireCube(transform.position, new Vector3(Radius * 2f, Radius * 2f, 0.1f));
    }
}
