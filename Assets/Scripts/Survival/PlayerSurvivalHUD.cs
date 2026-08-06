using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 人物生存 HUD。
/// UI 层级由编辑器工具预先创建并保存在场景中，本组件在运行时只绑定数据和刷新显示。
/// </summary>
[ExecuteAlways]
public class PlayerSurvivalHUD : MonoBehaviour
{
    [Header("数据源")]
    [Tooltip("场景中的生存属性系统。为空时仅在运行时自动查找，不会创建新对象。")]
    [SerializeField] private SurvivalSystem survivalSystem;

    [Header("生命")]
    [Tooltip("生命条的填充图片。组件通过该图片 RectTransform 的横向宽度显示比例。")]
    [SerializeField] private Image hpFill;
    [Tooltip("生命数值文本。")]
    [SerializeField] private Text hpValueText;

    [Header("饥饿")]
    [Tooltip("饥饿条的填充图片。")]
    [SerializeField] private Image hungerFill;
    [Tooltip("饥饿数值文本。")]
    [SerializeField] private Text hungerValueText;

    [Header("口渴")]
    [Tooltip("口渴条的填充图片。")]
    [SerializeField] private Image thirstFill;
    [Tooltip("口渴数值文本。")]
    [SerializeField] private Text thirstValueText;

    [Header("精力")]
    [Tooltip("疲劳/精力条的填充图片。数值越高表示精力越充足。")]
    [SerializeField] private Image fatigueFill;
    [Tooltip("疲劳/精力数值文本。")]
    [SerializeField] private Text fatigueValueText;

    [Header("状态")]
    [Tooltip("感染状态文字。未感染时可隐藏，感染时显示。")]
    [SerializeField] private Text infectionText;
    [Tooltip("未感染时是否隐藏感染状态文字。")]
    [SerializeField] private bool hideHealthyInfectionState = true;

    [Header("编辑器预览")]
    [Tooltip("非运行状态下用于预览生命条的数值。")]
    [Range(0f, 100f)] [SerializeField] private float previewHp = 82f;
    [Tooltip("非运行状态下用于预览饥饿条的数值。")]
    [Range(0f, 100f)] [SerializeField] private float previewHunger = 68f;
    [Tooltip("非运行状态下用于预览口渴条的数值。")]
    [Range(0f, 100f)] [SerializeField] private float previewThirst = 52f;
    [Tooltip("非运行状态下用于预览精力条的数值。")]
    [Range(0f, 100f)] [SerializeField] private float previewFatigue = 74f;
    [Tooltip("非运行状态下是否预览感染状态。")]
    [SerializeField] private bool previewInfected;

    /// <summary>
    /// 编辑器构建工具用来判断当前 HUD 是否已完整绑定。
    /// </summary>
    public bool HasRequiredReferences =>
        hpFill != null && hpValueText != null &&
        hungerFill != null && hungerValueText != null &&
        thirstFill != null && thirstValueText != null &&
        fatigueFill != null && fatigueValueText != null &&
        infectionText != null;

    private void Awake()
    {
        if (Application.isPlaying && survivalSystem == null)
            survivalSystem = FindObjectOfType<SurvivalSystem>();
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void OnValidate()
    {
        // ExecuteAlways 的 Update 会在下一次编辑器刷新时更新预览。
        // 不在 OnValidate 内改 RectTransform，避免 Unity 的 CheckConsistency 阶段触发 SendMessage 错误。
    }

    /// <summary>
    /// 由编辑器构建工具绑定所有可视控件。
    /// </summary>
    public void Configure(
        Image hp, Text hpText,
        Image hunger, Text hungerText,
        Image thirst, Text thirstText,
        Image fatigue, Text fatigueText,
        Text infection)
    {
        hpFill = hp;
        hpValueText = hpText;
        hungerFill = hunger;
        hungerValueText = hungerText;
        thirstFill = thirst;
        thirstValueText = thirstText;
        fatigueFill = fatigue;
        fatigueValueText = fatigueText;
        infectionText = infection;
        Refresh();
    }

    /// <summary>
    /// 把当前生存数据同步到填充条和文本。
    /// </summary>
    public void Refresh()
    {
        // 跨场景切换后，场景 HUD 原来的序列化引用可能指向已清理的重复服务；自动绑定常驻实例。
        if (Application.isPlaying && survivalSystem == null)
            survivalSystem = FindObjectOfType<SurvivalSystem>();

        if (Application.isPlaying && survivalSystem != null)
        {
            SetRow(hpFill, hpValueText, survivalSystem.HpNormalized, survivalSystem.hp, survivalSystem.MaxHp);
            SetRow(hungerFill, hungerValueText, survivalSystem.HungerNormalized, survivalSystem.hunger, survivalSystem.MaxHunger);
            SetRow(thirstFill, thirstValueText, survivalSystem.ThirstNormalized, survivalSystem.thirst, survivalSystem.MaxThirst);
            SetRow(fatigueFill, fatigueValueText, survivalSystem.FatigueNormalized, survivalSystem.fatigue, survivalSystem.MaxFatigue);
            SetInfection(survivalSystem.infected);
            return;
        }

        SetRow(hpFill, hpValueText, previewHp / 100f, previewHp, 100f);
        SetRow(hungerFill, hungerValueText, previewHunger / 100f, previewHunger, 100f);
        SetRow(thirstFill, thirstValueText, previewThirst / 100f, previewThirst, 100f);
        SetRow(fatigueFill, fatigueValueText, previewFatigue / 100f, previewFatigue, 100f);
        SetInfection(previewInfected);
    }

    private static void SetRow(Image fill, Text valueText, float normalized, float current, float maximum)
    {
        if (fill != null)
        {
            var value = Mathf.Clamp01(normalized);
            // 统一使用 RectTransform 宽度，不依赖 Image 是否配置 Sprite。
            // 这样纯色 Image、九宫格 Sprite 或后续替换的美术图都能得到一致的横向血条效果。
            fill.fillAmount = 1f;
            var rect = fill.rectTransform;
            var anchorMin = rect.anchorMin;
            var anchorMax = rect.anchorMax;
            anchorMin.x = 0f;
            anchorMax.x = value;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;

            // 横向不留固定像素边距，避免数值接近 0 时产生负宽度。
            var offsetMin = rect.offsetMin;
            var offsetMax = rect.offsetMax;
            offsetMin.x = 0f;
            offsetMax.x = 0f;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
        if (valueText != null) valueText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
    }

    private void SetInfection(bool isInfected)
    {
        if (infectionText == null) return;
        infectionText.text = isInfected ? "感染" : "状态正常";
        infectionText.color = isInfected
            ? new Color(0.82f, 0.24f, 0.18f, 1f)
            : new Color(0.55f, 0.7f, 0.58f, 1f);
        infectionText.gameObject.SetActive(isInfected || !hideHealthyInfectionState);
    }
}
