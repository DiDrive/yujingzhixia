using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// AOT 基础层到 HotUpdate.dll 掉落算法的稳定桥接器。
/// 本文件进入客户端基础包；以后算法变化只修改 HotUpdate/Loot/LootRollService.cs。
/// </summary>
public static class HotUpdateLootBridge
{
    // 这三个名称构成基础客户端查找热更新实现的协议。
    // 已发布客户端无法修改它们，因此热更新侧应长期保留兼容的类型名和方法签名。
    private const string AssemblyName = "HotUpdate";
    private const string ServiceTypeName = "LootRollService";
    private const string RollMethodName = "Roll";

    /// <summary>
    /// 与热更新层 RollItem 的 JSON 字段保持一致。
    /// 本 DTO 属于 Assembly-CSharp，可安全转换成客户端已有的 ItemStack。
    /// </summary>
    [Serializable]
    private sealed class RollItemDto
    {
        public string itemId;
        public int quantity;
    }

    /// <summary>
    /// 对应热更新层返回的 JSON 根对象。
    /// </summary>
    [Serializable]
    private sealed class RollResultDto
    {
        public List<RollItemDto> items = new List<RollItemDto>();
    }

    // 反射查询只成功执行一次，后续抽取直接复用 MethodInfo。
    private static MethodInfo rollMethod;

    // 相同故障每个进程只提示一次，避免失败回退时持续刷屏。
    private static bool resolutionWarningLogged;
    private static bool invocationWarningLogged;

    /// <summary>
    /// 尝试调用 HotUpdate.dll 中的掉落算法。
    /// 返回 true 表示热更新算法已正常执行，即使本次结果列表为空也属于成功；
    /// 返回 false 表示调用不可用，调用方应改用客户端内置保底算法。
    /// </summary>
    /// <param name="lootTablesJson">完整的掉落配置 JSON。</param>
    /// <param name="tableId">需要抽取的掉落表 ID。</param>
    /// <param name="minRollsOverride">最小抽取次数覆盖值，-1 表示使用配置。</param>
    /// <param name="maxRollsOverride">最大抽取次数覆盖值，-1 表示使用配置。</param>
    /// <param name="result">成功时返回转换后的 ItemStack 列表。</param>
    public static bool TryRoll(
        string lootTablesJson,
        string tableId,
        int minRollsOverride,
        int maxRollsOverride,
        out List<ItemStack> result)
    {
        result = null;
        if (!TryResolveRollMethod())
            return false;

        try
        {
            // 只跨程序集传递 string/int 等稳定基础类型，避免 AOT 与热更新程序集类型耦合。
            object rawResult = rollMethod.Invoke(
                null,
                new object[]
                {
                    lootTablesJson,
                    tableId,
                    minRollsOverride,
                    maxRollsOverride
                });

            if (!(rawResult is string resultJson) || string.IsNullOrWhiteSpace(resultJson))
                throw new InvalidOperationException("热更新掉落算法没有返回有效 JSON。");

            RollResultDto dto = JsonUtility.FromJson<RollResultDto>(resultJson);
            result = new List<ItemStack>();
            if (dto?.items == null)
                return true;

            for (int index = 0; index < dto.items.Count; index++)
            {
                RollItemDto item = dto.items[index];
                if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.quantity <= 0)
                    continue;
                result.Add(new ItemStack(item.itemId, item.quantity));
            }

            return true;
        }
        catch (Exception exception)
        {
            if (!invocationWarningLogged)
            {
                invocationWarningLogged = true;
                Exception actualException = exception is TargetInvocationException targetException &&
                                            targetException.InnerException != null
                    ? targetException.InnerException
                    : exception;
                Debug.LogWarning(
                    "调用 HotUpdate 掉落算法失败，本次运行将退回内置算法：\n" + actualException);
            }
            return false;
        }
    }

    /// <summary>
    /// 从当前 AppDomain 中寻找已由 LoadDll 加载的 HotUpdate 程序集和固定 Roll 方法。
    /// 如果调用发生得太早，下一次仍会继续尝试解析，不会永久锁定失败状态。
    /// </summary>
    private static bool TryResolveRollMethod()
    {
        if (rollMethod != null)
            return true;

        Assembly hotUpdateAssembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == AssemblyName);
        Type serviceType = hotUpdateAssembly?.GetType(ServiceTypeName);
        rollMethod = serviceType?.GetMethod(
            RollMethodName,
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string), typeof(string), typeof(int), typeof(int) },
            null);

        if (rollMethod == null && !resolutionWarningLogged)
        {
            resolutionWarningLogged = true;
            Debug.LogWarning(
                "尚未找到 HotUpdate.LootRollService.Roll，掉落系统将暂时使用客户端内置算法。");
        }

        return rollMethod != null;
    }
}
