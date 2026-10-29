using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// AOT 生存系统到 HotUpdate.dll 生存规则的稳定桥接器。
/// 高频 Tick 使用复用的 float 数组传递状态，避免每帧创建 JSON 和临时 DTO。
/// </summary>
public static class HotUpdateSurvivalBridge
{
    private const string AssemblyName = "HotUpdate";
    private const string ServiceTypeName = "SurvivalRuleService";

    // state 数组协议（输入与输出共用）。已发布客户端依赖这些位置，只能在末尾追加字段，不能换序或复用旧位置。
    internal const int StateHunger = 0;                 // 当前饥饿值，0 表示极度饥饿。
    internal const int StateThirst = 1;                 // 当前口渴值，0 表示严重缺水。
    internal const int StateFatigue = 2;                // 当前精力值，0 表示精疲力竭。
    internal const int StateHp = 3;                     // 当前生命值。
    internal const int StateInfected = 4;               // 感染状态：1=true，0=false。
    internal const int StateMaxHunger = 5;              // 饥饿值上限。
    internal const int StateMaxThirst = 6;              // 口渴值上限。
    internal const int StateMaxFatigue = 7;             // 精力值上限。
    internal const int StateMaxHp = 8;                  // 生命值上限。
    internal const int StateMoveSpeedMultiplier = 9;    // 输出：移动速度倍率。
    internal const int StateDamageMultiplier = 10;      // 输出：伤害倍率。
    internal const int StateStaminaMultiplier = 11;     // 输出：耐力消耗/恢复相关倍率。
    internal const int StateCanUseChargeAttack = 12;    // 输出：能否蓄力攻击，1=true，0=false。
    internal const int StateLength = 13;                // 当前协议要求的最小 state 长度。

    // settings 数组协议（只读输入）。这些字段由 SurvivalSystem 每帧填写，热更新层只读取。
    internal const int SettingDeltaTime = 0;            // 本帧秒数，来自 Time.deltaTime。
    internal const int SettingAdvanceSurvival = 1;      // 当前流程是否允许衰减，1=允许，0=暂停。
    internal const int SettingHungerDecay = 2;          // 每分钟饥饿衰减量。
    internal const int SettingThirstDecay = 3;          // 每分钟口渴衰减量。
    internal const int SettingFatigueDecay = 4;         // 每分钟精力衰减量。
    internal const int SettingWarningThreshold = 5;     // 警告阈值，触发移动/伤害/耐力减益。
    internal const int SettingCriticalThreshold = 6;    // 危险阈值，触发扣血或禁用蓄力攻击。
    internal const int SettingHungryHpLoss = 7;         // 严重饥饿时每分钟生命损失。
    internal const int SettingThirstyHpLoss = 8;        // 严重缺水时每分钟生命损失。
    internal const int SettingInfectedHpLoss = 9;       // 感染时每分钟生命损失。
    internal const int SettingLength = 10;              // 当前协议要求的最小 settings 长度。

    [Serializable]
    private sealed class EffectDto
    {
        public string type;
        public float value;
    }

    [Serializable]
    private sealed class EffectListDto
    {
        public List<EffectDto> effects = new List<EffectDto>();
    }

    private static readonly float[] StateBuffer = new float[StateLength];
    private static readonly object[] TickArguments = new object[2];
    private static readonly object[] EffectArguments = new object[2];

    private static MethodInfo tickMethod;
    private static MethodInfo hasUsefulEffectMethod;
    private static MethodInfo applyEffectsMethod;
    private static bool resolutionWarningLogged;
    private static bool invocationWarningLogged;
    private static bool bridgeDisabled;

    /// <summary>
    /// 尝试用热更新规则推进一次生存状态。settings 的固定布局由 SurvivalSystem 填写。
    /// </summary>
    public static bool TryTick(SurvivalSystem system, float[] settings)
    {
        if (system == null || settings == null || !TryResolveMethods())
            return false;

        FillState(system);
        TickArguments[0] = StateBuffer;
        TickArguments[1] = settings;

        try
        {
            tickMethod.Invoke(null, TickArguments);
            system.ApplyHotUpdateState(StateBuffer);
            return true;
        }
        catch (Exception exception)
        {
            LogInvocationFailure(exception);
            return false;
        }
    }

    /// <summary>
    /// 询问热更新规则：当前状态下至少有一个物品效果是否有实际作用。
    /// </summary>
    public static bool TryHasUsefulEffect(
        SurvivalSystem system,
        IReadOnlyList<GridItemUseEffect> effects,
        out bool hasUsefulEffect)
    {
        hasUsefulEffect = false;
        if (system == null || effects == null || !TryResolveMethods())
            return false;

        FillState(system);
        EffectArguments[0] = StateBuffer;
        EffectArguments[1] = SerializeEffects(effects);

        try
        {
            object result = hasUsefulEffectMethod.Invoke(null, EffectArguments);
            if (!(result is bool boolResult))
                throw new InvalidOperationException("热更新生存规则没有返回 bool 结果。");

            hasUsefulEffect = boolResult;
            return true;
        }
        catch (Exception exception)
        {
            LogInvocationFailure(exception);
            return false;
        }
    }

    /// <summary>
    /// 尝试由热更新规则施加全部消耗品效果，并把结果写回 SurvivalSystem。
    /// </summary>
    public static bool TryApplyEffects(
        SurvivalSystem system,
        IReadOnlyList<GridItemUseEffect> effects)
    {
        if (system == null || effects == null || !TryResolveMethods())
            return false;

        FillState(system);
        EffectArguments[0] = StateBuffer;
        EffectArguments[1] = SerializeEffects(effects);

        try
        {
            applyEffectsMethod.Invoke(null, EffectArguments);
            system.ApplyHotUpdateState(StateBuffer);
            return true;
        }
        catch (Exception exception)
        {
            LogInvocationFailure(exception);
            return false;
        }
    }

    private static bool TryResolveMethods()
    {
        if (bridgeDisabled)
            return false;

        if (tickMethod != null && hasUsefulEffectMethod != null && applyEffectsMethod != null)
            return true;

        Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => candidate.GetName().Name == AssemblyName);
        Type serviceType = assembly?.GetType(ServiceTypeName);
        tickMethod = FindMethod(serviceType, "Tick", typeof(void), typeof(float[]), typeof(float[]));
        hasUsefulEffectMethod = FindMethod(
            serviceType,
            "HasUsefulEffect",
            typeof(bool),
            typeof(float[]),
            typeof(string));
        applyEffectsMethod = FindMethod(
            serviceType,
            "ApplyEffects",
            typeof(void),
            typeof(float[]),
            typeof(string));

        bool succeeded = tickMethod != null &&
                         hasUsefulEffectMethod != null &&
                         applyEffectsMethod != null;
        // 程序集已经加载但协议不完整时，本次进程不再每帧重复反射查找。
        if (assembly != null && !succeeded)
            bridgeDisabled = true;
        if (!succeeded && !resolutionWarningLogged)
        {
            resolutionWarningLogged = true;
            Debug.LogWarning(
                "尚未找到完整的 HotUpdate.SurvivalRuleService，生存系统将使用客户端内置规则。");
        }

        return succeeded;
    }

    private static MethodInfo FindMethod(
        Type type,
        string methodName,
        Type returnType,
        params Type[] parameterTypes)
    {
        MethodInfo method = type?.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            null,
            parameterTypes,
            null);
        return method != null && method.ReturnType == returnType ? method : null;
    }

    private static void FillState(SurvivalSystem system)
    {
        StateBuffer[StateHunger] = system.hunger;
        StateBuffer[StateThirst] = system.thirst;
        StateBuffer[StateFatigue] = system.fatigue;
        StateBuffer[StateHp] = system.hp;
        StateBuffer[StateInfected] = system.infected ? 1f : 0f;
        StateBuffer[StateMaxHunger] = system.MaxHunger;
        StateBuffer[StateMaxThirst] = system.MaxThirst;
        StateBuffer[StateMaxFatigue] = system.MaxFatigue;
        StateBuffer[StateMaxHp] = system.MaxHp;
        StateBuffer[StateMoveSpeedMultiplier] = system.MoveSpeedMultiplier;
        StateBuffer[StateDamageMultiplier] = system.DamageMultiplier;
        StateBuffer[StateStaminaMultiplier] = system.StaminaMultiplier;
        StateBuffer[StateCanUseChargeAttack] = system.CanUseChargeAttack ? 1f : 0f;
    }

    private static string SerializeEffects(IReadOnlyList<GridItemUseEffect> effects)
    {
        var dto = new EffectListDto();
        for (int index = 0; index < effects.Count; index++)
        {
            dto.effects.Add(new EffectDto
            {
                type = effects[index].type,
                value = effects[index].value
            });
        }
        return JsonUtility.ToJson(dto);
    }

    private static void LogInvocationFailure(Exception exception)
    {
        if (invocationWarningLogged)
            return;

        invocationWarningLogged = true;
        bridgeDisabled = true;
        Exception actualException = exception is TargetInvocationException targetException &&
                                    targetException.InnerException != null
            ? targetException.InnerException
            : exception;
        Debug.LogWarning(
            "调用 HotUpdate 生存规则失败，本次运行将退回内置规则：\n" + actualException);
    }
}
