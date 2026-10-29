using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// HotUpdate.dll 的可追踪版本信息。
/// BusinessVersion 表示整套热更新业务代码版本；ApiVersion 表示它与基础客户端之间的调用协议版本。
/// 仅修改热更新内部实现时递增 BusinessVersion；破坏反射方法签名时才递增 ApiVersion。
/// </summary>
public static class HotUpdateRuntimeInfo
{
    public const string BusinessVersion = "1.1.0";
    public const int ApiVersion = 2;
}

/// <summary>
/// 热更新业务进入场景前的启动自检。
/// 自检只依赖 HotUpdate.dll 内部类型，不要求基础客户端增加新的直接程序集引用。
/// </summary>
public static class HotUpdateStartupSelfCheck
{
    /// <summary>
    /// AOT 层 HotUpdateLootBridge 已发布并固定使用的 Roll 方法签名。
    /// 自检使用完全相同的参数列表，防止后续重构误改协议后仍进入游戏。
    /// </summary>
    private static readonly Type[] LootRollParameterTypes =
    {
        typeof(string),
        typeof(string),
        typeof(int),
        typeof(int)
    };

    /// <summary>
    /// 执行所有关键热更新服务检查。
    /// 返回 false 时，GameEntry 会停止加载业务场景，让基础客户端显示进入游戏失败。
    /// </summary>
    public static bool Run(out string error)
    {
        error = null;

        // 使用纯 ASCII 日志标签，避免某些 Windows 日志查看器用错误编码显示中文乱码。
        Debug.Log($"[HotUpdate] BusinessVersion={HotUpdateRuntimeInfo.BusinessVersion}");
        Debug.Log($"[HotUpdate] ApiVersion={HotUpdateRuntimeInfo.ApiVersion}");

        if (!ValidateLootRollService(out error))
        {
            Debug.LogError($"[HotUpdate] SelfCheck=Failed; Reason={error}");
            return false;
        }

        if (!ValidateSurvivalRuleService(out error))
        {
            Debug.LogError($"[HotUpdate] SelfCheck=Failed; Reason={error}");
            return false;
        }

        Debug.Log("[HotUpdate] SelfCheck=Passed");
        return true;
    }

    /// <summary>
    /// 验证生存规则的三个 AOT 调用入口，防止以后修改签名导致桥接器运行时失败。
    /// </summary>
    private static bool ValidateSurvivalRuleService(out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(SurvivalRuleService.ImplementationVersion))
        {
            error = "SurvivalRuleService.ImplementationVersion is empty.";
            return false;
        }

        Type serviceType = typeof(SurvivalRuleService);
        if (!HasMethod(serviceType, "Tick", typeof(void), typeof(float[]), typeof(float[])) ||
            !HasMethod(serviceType, "HasUsefulEffect", typeof(bool), typeof(float[]), typeof(string)) ||
            !HasMethod(serviceType, "ApplyEffects", typeof(void), typeof(float[]), typeof(string)))
        {
            error = "SurvivalRuleService public API does not match ApiVersion 2.";
            return false;
        }

        Debug.Log(
            $"[HotUpdate] SurvivalRuleService=OK; Implementation={SurvivalRuleService.ImplementationVersion}");
        return true;
    }

    private static bool HasMethod(
        Type serviceType,
        string methodName,
        Type returnType,
        params Type[] parameterTypes)
    {
        MethodInfo method = serviceType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            null,
            parameterTypes,
            null);
        return method != null && method.ReturnType == returnType;
    }

    /// <summary>
    /// 验证掉落服务类型、算法版本标识，以及基础客户端会反射调用的公开静态方法签名。
    /// </summary>
    private static bool ValidateLootRollService(out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(LootRollService.ImplementationVersion))
        {
            error = "LootRollService.ImplementationVersion is empty.";
            return false;
        }

        MethodInfo rollMethod = typeof(LootRollService).GetMethod(
            "Roll",
            BindingFlags.Public | BindingFlags.Static,
            null,
            LootRollParameterTypes,
            null);

        if (rollMethod == null)
        {
            error = "LootRollService.Roll(string, string, int, int) was not found.";
            return false;
        }

        if (rollMethod.ReturnType != typeof(string))
        {
            error = $"LootRollService.Roll return type is {rollMethod.ReturnType}, expected System.String.";
            return false;
        }

        Debug.Log(
            $"[HotUpdate] LootRollService=OK; Implementation={LootRollService.ImplementationVersion}");
        return true;
    }
}
