using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可热更新的生存数值规则。AOT 层负责生命周期和场景引用，本类只负责纯数值计算。
/// </summary>
/// <remarks>
/// 后续主要修改本文件中的计算公式即可热更新，例如衰减算法、危险状态扣血和属性倍率。
/// settings 中的基础衰减与阈值来自 survival_rules.json；消耗品恢复量来自
/// item_definitions.json。修改数值时应修改对应配置来源，而不是数组下标。
/// </remarks>
public static class SurvivalRuleService
{
    /// <summary>
    /// 生存规则实现版本。只修改内部公式且不修改公开方法签名时，更新该标识用于验收。
    /// </summary>
    public const string ImplementationVersion = "hot-survival-v1";

    // state 数组必须与 AOT 的 HotUpdateSurvivalBridge 保持完全一致。
    // 已发布客户端不会随 DLL 改变这些下标：只能在末尾追加，不能换序、删除或改变旧字段含义。
    private const int Hunger = 0;                 // 输入/输出：当前饥饿值。
    private const int Thirst = 1;                 // 输入/输出：当前口渴值。
    private const int Fatigue = 2;                // 输入/输出：当前精力值。
    private const int Hp = 3;                     // 输入/输出：当前生命值。
    private const int Infected = 4;               // 输入/输出：感染状态，1=true，0=false。
    private const int MaxHunger = 5;              // 输入：饥饿值上限。
    private const int MaxThirst = 6;              // 输入：口渴值上限。
    private const int MaxFatigue = 7;             // 输入：精力值上限。
    private const int MaxHp = 8;                  // 输入：生命值上限。
    private const int MoveSpeedMultiplier = 9;    // 输出：移动速度倍率。
    private const int DamageMultiplier = 10;      // 输出：伤害倍率。
    private const int StaminaMultiplier = 11;     // 输出：耐力倍率。
    private const int CanUseChargeAttack = 12;    // 输出：能否蓄力攻击，1=true，0=false。
    private const int StateLength = 13;            // 当前协议要求的最小 state 长度。

    // settings 是 AOT 每帧提供的只读配置，也属于已发布协议。
    private const int DeltaTime = 0;               // 本帧经过的秒数。
    private const int AdvanceSurvival = 1;         // 是否推进衰减，1=推进，0=暂停。
    private const int HungerDecay = 2;             // 每分钟饥饿衰减量。
    private const int ThirstDecay = 3;             // 每分钟口渴衰减量。
    private const int FatigueDecay = 4;            // 每分钟精力衰减量。
    private const int WarningThreshold = 5;        // 警告阈值，触发属性倍率减益。
    private const int CriticalThreshold = 6;       // 危险阈值，触发扣血/禁用蓄力攻击。
    private const int HungryHpLoss = 7;            // 严重饥饿时每分钟生命损失。
    private const int ThirstyHpLoss = 8;           // 严重缺水时每分钟生命损失。
    private const int InfectedHpLoss = 9;          // 感染时每分钟生命损失。
    private const int SettingsLength = 10;          // 当前协议要求的最小 settings 长度。

    [Serializable]
    private sealed class EffectDto
    {
        // 支持 health/hp、hunger、thirst、fatigue/energy、infection、cureinfection。
        public string type;
        // 数值属性中正数表示恢复、负数表示降低；感染开关类忽略此值。
        public float value;
    }

    [Serializable]
    private sealed class EffectListDto
    {
        public List<EffectDto> effects = new List<EffectDto>();
    }

    private static bool versionLogged;

    /// <summary>
    /// 推进自然衰减、危险状态扣血并计算战斗修正值。结果原地写回 state。
    /// </summary>
    /// <param name="state">
    /// 长度至少为 13 的状态数组：0~4 是当前生存状态，5~8 是属性上限，
    /// 9~12 是本方法计算后写回的移动、伤害、耐力和蓄力攻击结果。
    /// 完整下标含义见本类顶部常量或《热更新生存规则协议说明》文档。
    /// </param>
    /// <param name="settings">
    /// 长度至少为 10 的只读配置数组：包含 deltaTime、是否允许衰减、三项每分钟衰减量、
    /// 警告/危险阈值，以及饥饿、缺水、感染三项每分钟生命损失。
    /// </param>
    public static void Tick(float[] state, float[] settings)
    {
        ValidateState(state);
        if (settings == null || settings.Length < SettingsLength)
            throw new ArgumentException($"settings 至少需要 {SettingsLength} 个元素。", nameof(settings));

        LogVersionOnce();
        ClampState(state);

        float deltaTime = Mathf.Max(0f, settings[DeltaTime]);
        if (settings[AdvanceSurvival] > 0.5f)
        {
            float minutes = deltaTime / 60f;
            state[Hunger] -= Mathf.Max(0f, settings[HungerDecay]) * minutes;
            state[Thirst] -= Mathf.Max(0f, settings[ThirstDecay]) * minutes;
            state[Fatigue] -= Mathf.Max(0f, settings[FatigueDecay]) * minutes;
            ClampState(state);

            float hpLossPerMinute = 0f;
            if (state[Hunger] <= settings[CriticalThreshold])
                hpLossPerMinute += Mathf.Max(0f, settings[HungryHpLoss]);
            if (state[Thirst] <= settings[CriticalThreshold])
                hpLossPerMinute += Mathf.Max(0f, settings[ThirstyHpLoss]);
            if (state[Infected] > 0.5f)
                hpLossPerMinute += Mathf.Max(0f, settings[InfectedHpLoss]);
            state[Hp] -= hpLossPerMinute * minutes;
        }

        ClampState(state);
        UpdateModifiers(state, settings[WarningThreshold], settings[CriticalThreshold]);
    }

    /// <summary>判断一组物品效果在当前状态下是否至少有一项会改变状态。</summary>
    /// <param name="state">
    /// AOT 层传入的当前状态数组。本方法只读取它，不会修改玩家状态。
    /// </param>
    /// <param name="effectsJson">
    /// 消耗品效果 JSON，根字段为 effects；每项包含 type 和 value。
    /// type 支持 health/hp、hunger、thirst、fatigue/energy、infection、cureinfection。
    /// </param>
    /// <returns>至少有一个效果能改变当前状态时返回 true，否则返回 false。</returns>
    public static bool HasUsefulEffect(float[] state, string effectsJson)
    {
        ValidateState(state);
        EffectListDto root = ParseEffects(effectsJson);
        if (root?.effects == null)
            return false;

        for (int index = 0; index < root.effects.Count; index++)
        {
            EffectDto effect = root.effects[index];
            if (effect == null)
                continue;

            switch (NormalizeType(effect.type))
            {
                case "health":
                case "hp":
                    if (WouldChange(state[Hp], state[MaxHp], effect.value)) return true;
                    break;
                case "hunger":
                    if (WouldChange(state[Hunger], state[MaxHunger], effect.value)) return true;
                    break;
                case "thirst":
                    if (WouldChange(state[Thirst], state[MaxThirst], effect.value)) return true;
                    break;
                case "fatigue":
                case "energy":
                    if (WouldChange(state[Fatigue], state[MaxFatigue], effect.value)) return true;
                    break;
                case "cureinfection":
                    if (state[Infected] > 0.5f) return true;
                    break;
                case "infection":
                    if (state[Infected] <= 0.5f) return true;
                    break;
            }
        }

        return false;
    }

    /// <summary>依次施加一组消耗品效果，结果原地写回 state。</summary>
    /// <param name="state">
    /// AOT 层传入的状态数组。方法会修改生命、饥饿、口渴、精力或感染字段并执行上下限裁剪。
    /// </param>
    /// <param name="effectsJson">
    /// 与 HasUsefulEffect 相同格式的消耗品效果 JSON。恢复类 value 可为正数或负数；
    /// infection 与 cureinfection 忽略 value。
    /// </param>
    public static void ApplyEffects(float[] state, string effectsJson)
    {
        ValidateState(state);
        EffectListDto root = ParseEffects(effectsJson);
        if (root?.effects == null)
            return;

        LogVersionOnce();
        for (int index = 0; index < root.effects.Count; index++)
        {
            EffectDto effect = root.effects[index];
            if (effect == null)
                continue;

            switch (NormalizeType(effect.type))
            {
                case "health":
                case "hp":
                    state[Hp] += effect.value;
                    break;
                case "hunger":
                    state[Hunger] += effect.value;
                    break;
                case "thirst":
                    state[Thirst] += effect.value;
                    break;
                case "fatigue":
                case "energy":
                    state[Fatigue] += effect.value;
                    break;
                case "cureinfection":
                    state[Infected] = 0f;
                    break;
                case "infection":
                    state[Infected] = 1f;
                    break;
            }
        }

        ClampState(state);
    }

    private static EffectListDto ParseEffects(string json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonUtility.FromJson<EffectListDto>(json);
    }

    private static void ValidateState(float[] state)
    {
        if (state == null || state.Length < StateLength)
            throw new ArgumentException($"state 至少需要 {StateLength} 个元素。", nameof(state));
    }

    private static void ClampState(float[] state)
    {
        state[MaxHunger] = Mathf.Max(1f, state[MaxHunger]);
        state[MaxThirst] = Mathf.Max(1f, state[MaxThirst]);
        state[MaxFatigue] = Mathf.Max(1f, state[MaxFatigue]);
        state[MaxHp] = Mathf.Max(1f, state[MaxHp]);
        state[Hunger] = Mathf.Clamp(state[Hunger], 0f, state[MaxHunger]);
        state[Thirst] = Mathf.Clamp(state[Thirst], 0f, state[MaxThirst]);
        state[Fatigue] = Mathf.Clamp(state[Fatigue], 0f, state[MaxFatigue]);
        state[Hp] = Mathf.Clamp(state[Hp], 0f, state[MaxHp]);
        state[Infected] = state[Infected] > 0.5f ? 1f : 0f;
    }

    
    private static void UpdateModifiers(float[] state, float warningThreshold, float criticalThreshold)
    {
        state[MoveSpeedMultiplier] = state[Thirst] <= warningThreshold ? 0.7f : 1f;
        state[DamageMultiplier] = state[Hunger] <= warningThreshold ? 0.8f : 1f;
        state[StaminaMultiplier] = state[Fatigue] <= warningThreshold ? 0.5f : 1f;
        state[CanUseChargeAttack] = state[Fatigue] > criticalThreshold ? 1f : 0f;
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
            : value.Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .Replace(" ", string.Empty)
                .ToLowerInvariant();
    }

    private static void LogVersionOnce()
    {
        if (versionLogged)
            return;

        versionLogged = true;
        Debug.Log($"[HotUpdate] 生存规则已启用：{ImplementationVersion}");
    }
}
