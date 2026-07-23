using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 场景中可直接编辑的世界交互提示 UI。只负责显示，不在运行时创建任何 UI 节点。
/// </summary>
public class WorldInteractionPromptUI : MonoBehaviour
{
    [Tooltip("提示面板自己的嵌套 Canvas。开启 Override Sorting 后可确保提示显示在背包和容器 UI 上方。")]
    [SerializeField] private Canvas promptCanvas;
    [Tooltip("控制整组提示显隐的 CanvasGroup。通常就是本对象上的 CanvasGroup。")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("显示交互按键和动作名称的文本，例如：[F] 搜索 物资容器。")]
    [SerializeField] private Text promptText;
    [Tooltip("搜索或拾取需要持续按键时显示的进度条根节点。")]
    [SerializeField] private GameObject progressRoot;
    [Tooltip("进度条的填充图片。运行时只修改它的横向填充比例。")]
    [SerializeField] private Image progressFill;
    [Tooltip("进度百分比文本。")]
    [SerializeField] private Text progressText;
    [Tooltip("拾取成功、背包已满等短暂反馈文本。")]
    [SerializeField] private Text feedbackText;

    private bool targetVisible;
    private float feedbackUntil;
    private RectTransform progressFillRect;

    private void Awake()
    {
        if (promptCanvas == null) promptCanvas = GetComponent<Canvas>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (promptCanvas != null)
        {
            promptCanvas.overrideSorting = true;
            promptCanvas.sortingOrder = 500;
            promptCanvas.enabled = true;
        }
        if (progressFill != null)
        {
            progressFill.type = Image.Type.Simple;
            progressFillRect = progressFill.rectTransform;
            SetProgressFill(0f);
        }
        targetVisible = false;
        if (progressRoot != null) progressRoot.SetActive(false);
        if (feedbackText != null) feedbackText.gameObject.SetActive(false);
        RefreshVisibility();
    }

    private void Update()
    {
        if (feedbackText == null || !feedbackText.gameObject.activeSelf) return;
        if (Time.unscaledTime < feedbackUntil) return;
        feedbackText.gameObject.SetActive(false);
        RefreshVisibility();
    }

    /// <summary>
    /// 显示当前交互目标。保留目标描述和距离参数是为了兼容调用方，正式提示只显示按键与动作。
    /// </summary>
    public void ShowTarget(string keyName, string actionPrompt, string targetDescription, float distance, float range, float progress, bool showProgress)
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        transform.SetAsLastSibling();
        if (promptCanvas != null) promptCanvas.enabled = true;
        targetVisible = true;
        if (promptText != null)
        {
            promptText.gameObject.SetActive(true);
            promptText.text = $"[{keyName}] {actionPrompt}";
        }

        if (progressRoot != null) progressRoot.SetActive(showProgress);
        if (showProgress)
        {
            progress = Mathf.Clamp01(progress);
            if (progressFill != null)
            {
                SetProgressFill(progress);
            }
            if (progressText != null) progressText.text = $"搜索进度 {progress:P0}";
        }
        RefreshVisibility();
    }

    /// <summary>通过 RectTransform 宽度表现进度，确保没有 Sprite 时也能正常从 0 填充到 100%。</summary>
    private void SetProgressFill(float progress)
    {
        if (progressFill == null) return;
        if (progressFillRect == null) progressFillRect = progressFill.rectTransform;
        progress = Mathf.Clamp01(progress);

        // 不依赖 Sprite 的 Filled 模式，直接改变右侧锚点，纯色 Image 也能稳定显示 0~100%。
        progressFillRect.anchorMin = new Vector2(0f, 0f);
        progressFillRect.anchorMax = new Vector2(progress, 1f);
        progressFillRect.pivot = new Vector2(0f, 0.5f);
        progressFillRect.anchoredPosition = new Vector2(2f, 0f);
        progressFillRect.sizeDelta = new Vector2(-4f, -4f);
    }

    /// <summary>隐藏目标提示和按住交互的进度条，但不影响短暂反馈文字。</summary>
    public void HideTarget()
    {
        targetVisible = false;
        if (progressRoot != null) progressRoot.SetActive(false);
        RefreshVisibility();
    }

    /// <summary>显示拾取成功、背包已满等限时反馈。</summary>
    public void ShowFeedback(string message, float duration)
    {
        if (feedbackText == null || string.IsNullOrWhiteSpace(message)) return;
        feedbackText.text = message;
        feedbackText.gameObject.SetActive(true);
        feedbackUntil = Time.unscaledTime + Mathf.Max(0.1f, duration);
        RefreshVisibility();
    }

    /// <summary>根据目标提示和反馈文字的状态统一控制整组 UI 透明度。</summary>
    private void RefreshVisibility()
    {
        if (canvasGroup == null) return;
        var feedbackVisible = feedbackText != null && feedbackText.gameObject.activeSelf;
        var visible = targetVisible || feedbackVisible;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }
}
