using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BootScene 的资源更新界面。界面节点由编辑器工具创建，运行时只负责显示状态。
/// </summary>
public sealed class BootUpdateUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup rootGroup;
    [SerializeField] private Text titleText;
    [SerializeField] private Text statusText;
    [SerializeField] private Text detailText;
    [SerializeField] private Text versionText;
    [SerializeField] private Image progressFill;
    [SerializeField] private Button confirmUpdateButton;
    [SerializeField] private Button cancelUpdateButton;
    [SerializeField] private Button enterButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button exitButton;

    private MyYooAsset updater;

    private void Awake()
    {
        if (rootGroup == null)
            rootGroup = GetComponent<CanvasGroup>();

        confirmUpdateButton?.onClick.AddListener(OnConfirmUpdateClicked);
        cancelUpdateButton?.onClick.AddListener(OnCancelUpdateClicked);
        enterButton?.onClick.AddListener(OnEnterClicked);
        retryButton?.onClick.AddListener(OnRetryClicked);
        exitButton?.onClick.AddListener(OnExitClicked);
        ShowPreparing();
    }

    private void OnDestroy()
    {
        confirmUpdateButton?.onClick.RemoveListener(OnConfirmUpdateClicked);
        cancelUpdateButton?.onClick.RemoveListener(OnCancelUpdateClicked);
        enterButton?.onClick.RemoveListener(OnEnterClicked);
        retryButton?.onClick.RemoveListener(OnRetryClicked);
        exitButton?.onClick.RemoveListener(OnExitClicked);
    }

    public void Bind(MyYooAsset target)
    {
        updater = target;
    }

    public void ShowPreparing()
    {
        SetVisible(true);
        SetText(titleText, "余烬之下");
        SetText(statusText, "正在准备资源更新");
        SetText(detailText, "请稍候……");
        SetProgress(0f);
        HideButtons();
    }

    public void ShowStage(string status, string detail = null)
    {
        SetVisible(true);
        SetText(statusText, status);
        SetText(detailText, detail ?? string.Empty);
        HideButtons();
    }

    public void ShowVersion(string version)
    {
        SetText(versionText, string.IsNullOrWhiteSpace(version)
            ? "资源版本：--"
            : $"资源版本：{version}");
    }

    /// <summary>
    /// 显示本次更新量并等待玩家选择。返回 false 表示确认按钮没有正确绑定，
    /// 调用方应采用兼容策略继续下载，避免启动流程永久卡住。
    /// </summary>
    public bool ShowDownloadConfirmation(int fileCount, long totalBytes)
    {
        ShowStage(
            "发现资源更新",
            $"本次需要下载 {fileCount} 个文件，共 {FormatBytes(totalBytes)}。是否立即更新？");
        SetProgress(0f);

        if (confirmUpdateButton != null)
            confirmUpdateButton.gameObject.SetActive(true);
        if (cancelUpdateButton != null)
            cancelUpdateButton.gameObject.SetActive(true);

        return confirmUpdateButton != null && cancelUpdateButton != null;
    }

    public void ShowDownloadProgress(
        int currentCount,
        int totalCount,
        long currentBytes,
        long totalBytes,
        float progress)
    {
        SetText(statusText, $"正在更新资源  {Mathf.Clamp01(progress):P0}");
        SetText(
            detailText,
            $"{currentCount}/{totalCount} 个文件    " +
            $"{FormatBytes(currentBytes)} / {FormatBytes(totalBytes)}");
        SetProgress(progress);
    }

    public void ShowError(string stage, string error)
    {
        SetVisible(true);
        SetText(statusText, $"{stage}失败");
        SetText(detailText, string.IsNullOrWhiteSpace(error) ? "发生未知错误" : error);
        HideButtons();
        if (retryButton != null)
            retryButton.gameObject.SetActive(true);
        if (exitButton != null)
            exitButton.gameObject.SetActive(true);
    }

    public bool ShowReadyToEnter()
    {
        ShowStage("更新完成", "点击按钮进入游戏");
        SetProgress(1f);
        if (enterButton != null)
            enterButton.gameObject.SetActive(true);
        return enterButton != null;
    }

    public void ShowEnteringGame()
    {
        ShowStage("正在进入游戏", "请稍候……");
        SetProgress(1f);
    }

    private void SetVisible(bool visible)
    {
        if (rootGroup == null) return;
        rootGroup.alpha = visible ? 1f : 0f;
        rootGroup.interactable = visible;
        rootGroup.blocksRaycasts = visible;
    }

    private void SetProgress(float progress)
    {
        if (progressFill == null)
            return;

        var value = Mathf.Clamp01(progress);
        var rect = progressFill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(value, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void HideButtons()
    {
        if (confirmUpdateButton != null)
            confirmUpdateButton.gameObject.SetActive(false);
        if (cancelUpdateButton != null)
            cancelUpdateButton.gameObject.SetActive(false);
        if (enterButton != null)
            enterButton.gameObject.SetActive(false);
        if (retryButton != null)
            retryButton.gameObject.SetActive(false);
        if (exitButton != null)
            exitButton.gameObject.SetActive(false);
    }

    private void OnConfirmUpdateClicked()
    {
        HideButtons();
        updater?.ConfirmDownload();
    }

    private void OnCancelUpdateClicked()
    {
        HideButtons();

        if (updater != null)
        {
            updater.CancelUpdateAndExit();
            return;
        }

        QuitApplication();
    }

    private void OnEnterClicked()
    {
        HideButtons();
        updater?.EnterGame();
    }

    private void OnRetryClicked()
    {
        updater?.RetryUpdate();
    }

    private static void OnExitClicked()
    {
        QuitApplication();
    }

    private static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void SetText(Text target, string value)
    {
        if (target != null)
            target.text = value;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
            return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
        if (bytes >= 1024L * 1024L)
            return $"{bytes / (1024f * 1024f):F2} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024f:F1} KB";
        return $"{bytes} B";
    }
}
