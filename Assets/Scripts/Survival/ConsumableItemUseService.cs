using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 消耗品使用流程：验证选中物品、推进使用时间、施加生存效果，最后精确扣除该物品实例一个数量。
/// 挂在场景中的 Core_Inventory 上，背包 UI 只负责发起请求和显示进度。
/// </summary>
public class ConsumableItemUseService : MonoBehaviour
{
    [Tooltip("唯一的网格背包数据服务。为空时运行时自动查找。")]
    [SerializeField] private GridInventoryService gridInventoryService;
    [Tooltip("玩家生存属性系统。为空时运行时自动查找。")]
    [SerializeField] private SurvivalSystem survivalSystem;
    [Tooltip("使用完成或失败后的提示保留秒数。")]
    [Min(0f)] [SerializeField] private float resultMessageDuration = 1.8f;

    private string currentInstanceId;
    private string currentDisplayName;
    private float currentDuration;
    private float elapsed;
    private float messageHideTime;

    public bool IsUsing => !string.IsNullOrWhiteSpace(currentInstanceId);
    public float Progress => IsUsing
        ? currentDuration <= 0.01f ? 1f : Mathf.Clamp01(elapsed / currentDuration)
        : 0f;
    public string StatusMessage { get; private set; } = string.Empty;

    private void Awake()
    {
        ResolveServices();
    }

    private void Update()
    {
        if (!IsUsing)
        {
            if (!string.IsNullOrWhiteSpace(StatusMessage) && Time.unscaledTime >= messageHideTime)
                StatusMessage = string.Empty;
            return;
        }

        // 使用期间若实例被移动不影响操作；若已被删除或不再属于玩家，则取消。
        if (gridInventoryService == null ||
            !gridInventoryService.TryGetItemInstance(currentInstanceId, out _) ||
            !gridInventoryService.IsPlayerOwnedInstance(currentInstanceId))
        {
            FinishWithMessage("使用已取消：物品不存在");
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        StatusMessage = $"正在使用 {currentDisplayName}  {Progress:P0}";
        if (elapsed >= currentDuration) CompleteCurrentUse();
    }

    /// <summary>验证并开始使用指定物品实例。同一时间只允许使用一件物品。</summary>
    public bool TryBeginUse(string instanceId, out string message)
    {
        ResolveServices();
        if (IsUsing)
        {
            message = $"正在使用 {currentDisplayName}";
            SetTransientMessage(message);
            return false;
        }
        if (gridInventoryService == null || survivalSystem == null)
        {
            message = "使用失败：背包或生存系统未绑定";
            SetTransientMessage(message);
            return false;
        }
        if (!gridInventoryService.TryGetItemInstance(instanceId, out var instance) ||
            !gridInventoryService.IsPlayerOwnedInstance(instanceId))
        {
            message = "只能使用玩家背包中的物品";
            SetTransientMessage(message);
            return false;
        }
        if (!gridInventoryService.TryGetItemDefinition(instance.itemId, out var definition) ||
            !definition.usable || definition.useEffects == null || definition.useEffects.Count == 0)
        {
            message = "该物品不能使用";
            SetTransientMessage(message);
            return false;
        }
        if (!HasAnyUsefulEffect(definition.useEffects))
        {
            message = "当前状态不需要使用该物品";
            SetTransientMessage(message);
            return false;
        }

        currentInstanceId = instanceId;
        currentDisplayName = string.IsNullOrWhiteSpace(definition.displayName)
            ? definition.itemId
            : definition.displayName;
        currentDuration = Mathf.Max(0f, definition.useDuration);
        elapsed = 0f;
        message = currentDuration <= 0.01f
            ? $"使用 {currentDisplayName}"
            : $"正在使用 {currentDisplayName}";
        StatusMessage = message;

        if (currentDuration <= 0.01f) CompleteCurrentUse();
        return true;
    }

    /// <summary>主动取消当前使用动作，不扣除物品。</summary>
    public void CancelUse(string reason = "使用已取消")
    {
        if (!IsUsing) return;
        FinishWithMessage(reason);
    }

    private void CompleteCurrentUse()
    {
        var instanceId = currentInstanceId;
        var displayName = currentDisplayName;
        if (!gridInventoryService.TryGetItemInstance(instanceId, out var instance) ||
            !gridInventoryService.TryGetItemDefinition(instance.itemId, out var definition) ||
            !HasAnyUsefulEffect(definition.useEffects))
        {
            FinishWithMessage("使用失败：状态或物品已变化");
            return;
        }

        // 先确保能够精确扣除，再施加效果；正常流程中实例已在开始和 Update 阶段反复验证。
        if (!gridInventoryService.TryConsumeFromInstance(instanceId, 1))
        {
            FinishWithMessage("使用失败：物品扣除失败");
            return;
        }

        ApplyEffects(definition.useEffects);
        FinishWithMessage($"已使用 {displayName}");
    }

    private bool HasAnyUsefulEffect(IReadOnlyList<GridItemUseEffect> effects)
    {
        if (survivalSystem == null || effects == null) return false;
        for (var i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            var type = NormalizeType(effect.type);
            switch (type)
            {
                case "health":
                case "hp":
                    if (WouldChange(survivalSystem.hp, survivalSystem.MaxHp, effect.value)) return true;
                    break;
                case "hunger":
                    if (WouldChange(survivalSystem.hunger, survivalSystem.MaxHunger, effect.value)) return true;
                    break;
                case "thirst":
                    if (WouldChange(survivalSystem.thirst, survivalSystem.MaxThirst, effect.value)) return true;
                    break;
                case "fatigue":
                case "energy":
                    if (WouldChange(survivalSystem.fatigue, survivalSystem.MaxFatigue, effect.value)) return true;
                    break;
                case "cureinfection":
                    if (survivalSystem.infected) return true;
                    break;
                case "infection":
                    if (!survivalSystem.infected) return true;
                    break;
            }
        }
        return false;
    }

    private void ApplyEffects(IReadOnlyList<GridItemUseEffect> effects)
    {
        if (effects == null) return;
        for (var i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            switch (NormalizeType(effect.type))
            {
                case "health":
                case "hp":
                    if (effect.value >= 0f) survivalSystem.Heal(effect.value);
                    else survivalSystem.ApplyDamage(-effect.value);
                    break;
                case "hunger":
                    survivalSystem.Eat(effect.value);
                    break;
                case "thirst":
                    survivalSystem.Drink(effect.value);
                    break;
                case "fatigue":
                case "energy":
                    survivalSystem.Sleep(effect.value);
                    break;
                case "cureinfection":
                    survivalSystem.CureInfection();
                    break;
                case "infection":
                    survivalSystem.SetInfected(true);
                    break;
            }
        }
    }

    private void ResolveServices()
    {
        if (gridInventoryService == null) gridInventoryService = FindObjectOfType<GridInventoryService>();
        if (survivalSystem == null) survivalSystem = FindObjectOfType<SurvivalSystem>();
    }

    private void FinishWithMessage(string message)
    {
        currentInstanceId = null;
        currentDisplayName = null;
        currentDuration = 0f;
        elapsed = 0f;
        SetTransientMessage(message);
    }

    private void SetTransientMessage(string message)
    {
        StatusMessage = message ?? string.Empty;
        messageHideTime = Time.unscaledTime + Mathf.Max(0f, resultMessageDuration);
    }

    private static bool WouldChange(float current, float maximum, float delta)
    {
        if (Mathf.Approximately(delta, 0f)) return false;
        return delta > 0f ? current < maximum - 0.001f : current > 0.001f;
    }

    private static string NormalizeType(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
    }
}
