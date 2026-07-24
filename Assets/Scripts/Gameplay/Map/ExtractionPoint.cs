using UnityEngine;

/// <summary>
/// 地图撤离点。玩家进入半径后自动开始倒计时，离开范围则取消，倒计时结束后通知场景流程完成撤离。
/// 使用半径检测而不是依赖触发器，因此点位本身不需要额外创建碰撞器。
/// </summary>
public class ExtractionPoint : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("撤离点唯一 ID。为空时使用 GameObject 名称。")]
    [SerializeField] private string extractionId;
    [Tooltip("显示在撤离倒计时 UI 上的名称。")]
    [SerializeField] private string displayName = "撤离点";
    [Tooltip("撤离后进入的目标场景或流程 ID。")]
    [SerializeField] private string targetId = "Shelter";
    [Header("Extraction")]
    [Tooltip("玩家进入这个世界坐标半径后开始撤离倒计时。")]
    [SerializeField] private float radius = 1f;
    [Tooltip("玩家持续留在范围内多少秒后成功撤离。")]
    [Min(0.1f)] [SerializeField] private float countdownDuration = 5f;
    [Tooltip("是否启用该撤离点。关闭后不会检测玩家，也不会显示倒计时。")]
    [SerializeField] private bool available = true;
    [Header("Scene References")]
    [Tooltip("玩家对象。为空时运行时查找 PlayerCombatController2D。")]
    [SerializeField] private PlayerCombatController2D player;
    [Tooltip("场景 Canvas 下可编辑的交互提示 UI。为空时查找 WorldInteractionPromptUI。")]
    [SerializeField] private WorldInteractionPromptUI progressUi;
    [Tooltip("场景流程入口。为空时查找 GameBootstrap。")]
    [SerializeField] private GameBootstrap gameBootstrap;
    [Header("Runtime Status (运行时状态)")]
    [Tooltip("玩家当前是否位于撤离范围内。")]
    [SerializeField] private bool playerInside;
    [Tooltip("本次撤离已累计的秒数。离开范围后归零。")]
    [SerializeField] private float elapsed;
    [Tooltip("该撤离点本局是否已经完成。")]
    [SerializeField] private bool completed;

    // 多个撤离点可以同时摆在地图上，但同一时刻只允许一个点驱动公共倒计时 UI。
    private static ExtractionPoint activePoint;

    public string ExtractionId => string.IsNullOrWhiteSpace(extractionId) ? name : extractionId;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public string TargetId => targetId;
    public float Radius => Mathf.Max(0f, radius);
    public float CountdownDuration => Mathf.Max(0.1f, countdownDuration);
    public bool IsAvailable => available && isActiveAndEnabled && !completed;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        elapsed = 0f;
        playerInside = false;
    }

    private void Update()
    {
        if (!IsAvailable)
        {
            if (activePoint == this) CancelExtraction();
            return;
        }

        if (player == null)
        {
            ResolveReferences();
            if (player == null) return;
        }

        var offset = (Vector2)player.transform.position - (Vector2)transform.position;
        var inside = offset.sqrMagnitude <= Radius * Radius;
        playerInside = inside;
        if (!inside)
        {
            if (activePoint == this) CancelExtraction();
            return;
        }

        if (activePoint != null && activePoint != this) return;
        activePoint = this;
        elapsed += Time.deltaTime;
        if (progressUi == null) ResolveReferences();
        progressUi?.ShowExtraction(DisplayName, elapsed, CountdownDuration);

        if (elapsed >= CountdownDuration) CompleteExtraction();
    }

    private void OnDisable()
    {
        if (activePoint == this) CancelExtraction();
    }

    /// <summary>新一局开始时恢复可用状态。</summary>
    public void ResetForNewRun()
    {
        completed = false;
        playerInside = false;
        elapsed = 0f;
        if (activePoint == this) activePoint = null;
        progressUi?.HideExtraction();
    }

    /// <summary>玩家离开范围时取消撤离，并把倒计时归零。</summary>
    private void CancelExtraction()
    {
        elapsed = 0f;
        playerInside = false;
        if (activePoint == this) activePoint = null;
        progressUi?.HideExtraction();
    }

    /// <summary>完成倒计时并把撤离结果交给场景主流程。</summary>
    private void CompleteExtraction()
    {
        elapsed = CountdownDuration;
        completed = true;
        playerInside = true;
        if (activePoint == this) activePoint = null;
        progressUi?.HideExtraction();

        if (gameBootstrap == null) ResolveReferences();
        if (gameBootstrap != null)
        {
            gameBootstrap.CompleteExtraction(this);
            return;
        }

        Debug.LogWarning($"撤离点 {ExtractionId} 已完成倒计时，但场景中没有 GameBootstrap。", this);
    }

    /// <summary>补齐玩家、场景流程和编辑器 UI 引用。</summary>
    private void ResolveReferences()
    {
        if (player == null) player = FindObjectOfType<PlayerCombatController2D>();
        if (progressUi == null) progressUi = FindObjectOfType<WorldInteractionPromptUI>(true);
        if (gameBootstrap == null) gameBootstrap = FindObjectOfType<GameBootstrap>();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = available
            ? new Color(0.25f, 1f, 0.45f, 0.9f)
            : new Color(0.45f, 0.45f, 0.45f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.25f, Radius));
#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * (Mathf.Max(0.25f, Radius) + 0.15f),
            $"撤离：{DisplayName}\n{CountdownDuration:0.0} 秒");
#endif
    }
}
