using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 热更新掉落抽取服务。
/// AOT 层只通过反射传入 JSON、表 ID 和覆盖次数，不直接引用本类型。
/// 因此以后修改这里的算法，只需要重新发布 HotUpdate.dll 和 YooAsset 资源。
/// </summary>
public static class LootRollService
{
    /// <summary>
    /// 用于日志和验收的算法版本标识。
    /// 修改此值并只发布新的 HotUpdate.dll，可验证旧客户端是否执行了新代码。
    /// </summary>
    public const string ImplementationVersion = "hot-loot-v1";

    /// <summary>
    /// 对应 loot_tables.json 中 entries 数组的单项结构。
    /// 这里单独定义 DTO，是为了避免 HotUpdate 程序集依赖 Assembly-CSharp 的类型。
    /// </summary>
    [Serializable]
    private sealed class LootEntry
    {
        public string itemId;
        public int minQuantity = 1; //最小数量
        public int maxQuantity = 1;
        public int weight = 1;  //权重
    }

    /// <summary>
    /// 对应 loot_tables.json 中的一张掉落表。
    /// </summary>
    [Serializable]
    private sealed class LootTable
    {
        public string tableId;
        public int minRolls = 1;    // 最少抽取次数
        public int maxRolls = 3;    //
        public bool allowDuplicates = true;
        public List<LootEntry> entries = new List<LootEntry>();
    }

    /// <summary>
    /// JsonUtility 解析所需的根对象，对应 JSON 最外层的 tables 字段。
    /// </summary>
    [Serializable]
    private sealed class LootRoot
    {
        public List<LootTable> tables = new List<LootTable>();
    }

    /// <summary>
    /// 返回给 AOT 桥接层的单个掉落结果。
    /// </summary>
    [Serializable]
    private sealed class RollItem
    {
        public string itemId;
        public int quantity;
    }

    /// <summary>
    /// JsonUtility 不能直接序列化顶层 List，因此用此对象包装返回列表。
    /// </summary>
    [Serializable]
    private sealed class RollResult
    {
        public List<RollItem> items = new List<RollItem>();
    }

    // 同一份配置在一次游戏进程内会被多次抽取。缓存解析结果，避免每次 Roll 都解析 JSON。
    private static string cachedJson;
    private static readonly Dictionary<string, LootTable> TableById =
        new Dictionary<string, LootTable>(StringComparer.Ordinal);

    // 版本日志每个进程只打印一次，避免频繁搜索物资时刷屏。
    private static bool versionLogged;

    /// <summary>
    /// 根据指定掉落表执行随机抽取。
    /// 该签名是 AOT 层与 HotUpdate.dll 之间的稳定协议，不要随意修改参数顺序和返回类型。
    /// 返回值使用 JSON，是为了避免热更新程序集直接依赖 Assembly-CSharp 中的 ItemStack。
    /// </summary>
    /// <param name="lootTablesJson">完整的 loot_tables.json 文本。</param>
    /// <param name="tableId">需要抽取的掉落表 ID。</param>
    /// <param name="minRollsOverride">大于等于 0 时覆盖表中的最小抽取次数。</param>
    /// <param name="maxRollsOverride">大于等于 0 时覆盖表中的最大抽取次数。</param>
    /// <returns>包含 itemId 和 quantity 的 JSON 结果。</returns>
    public static string Roll(
        string lootTablesJson,
        string tableId,
        int minRollsOverride,
        int maxRollsOverride)
    {
        var result = new RollResult();
        if (string.IsNullOrWhiteSpace(lootTablesJson) || string.IsNullOrWhiteSpace(tableId))
            return JsonUtility.ToJson(result);

        EnsureTablesLoaded(lootTablesJson);
        if (!TableById.TryGetValue(tableId, out LootTable table) ||
            table == null || table.entries == null || table.entries.Count == 0)
        {
            return JsonUtility.ToJson(result);
        }

        if (!versionLogged)
        {
            versionLogged = true;
            Debug.Log($"[HotUpdate] 掉落算法已启用：{ImplementationVersion}");
        }

        int minRolls = minRollsOverride >= 0 ? minRollsOverride : table.minRolls;
        int maxRolls = maxRollsOverride >= 0 ? maxRollsOverride : table.maxRolls;
        minRolls = Mathf.Max(0, minRolls);
        maxRolls = Mathf.Max(minRolls, maxRolls);

        // Unity 的整数 Random.Range 上限不包含，因此需要 +1 才能取到 maxRolls。
        int rollCount = UnityEngine.Random.Range(minRolls, maxRolls + 1);

        // 使用副本是为了在禁止重复时安全移除项目，不修改缓存中的原始配置。
        var pool = new List<LootEntry>(table.entries);

        for (int index = 0; index < rollCount && pool.Count > 0; index++)
        {
            LootEntry selected = PickWeighted(pool);
            if (selected == null)
                break;

            if (!string.IsNullOrWhiteSpace(selected.itemId))
            {
                int minQuantity = Mathf.Max(1, selected.minQuantity);
                int maxQuantity = Mathf.Max(minQuantity, selected.maxQuantity);
                result.items.Add(new RollItem
                {
                    itemId = selected.itemId,
                    quantity = UnityEngine.Random.Range(minQuantity, maxQuantity + 1)
                });
            }

            if (!table.allowDuplicates)
                pool.Remove(selected);
        }

        return JsonUtility.ToJson(result);
    }

    /// <summary>
    /// 当配置文本发生变化时重新解析并建立 tableId 索引；文本未变化时直接复用缓存。
    /// </summary>
    private static void EnsureTablesLoaded(string json)
    {
        if (string.Equals(cachedJson, json, StringComparison.Ordinal))
            return;

        cachedJson = json;
        TableById.Clear();

        LootRoot root;
        try
        {
            root = JsonUtility.FromJson<LootRoot>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError("[HotUpdate] 解析掉落表失败：" + exception.Message);
            return;
        }

        if (root?.tables == null)
            return;

        for (int index = 0; index < root.tables.Count; index++)
        {
            LootTable table = root.tables[index];
            if (table == null || string.IsNullOrWhiteSpace(table.tableId))
                continue;
            if (!TableById.ContainsKey(table.tableId))
                TableById.Add(table.tableId, table);
        }
    }

    /// <summary>
    /// 按正权重随机选择一项。权重小于 0 按 0 处理；总权重为 0 时返回 null。
    /// </summary>
    private static LootEntry PickWeighted(List<LootEntry> pool)
    {
        int totalWeight = 0;
        for (int index = 0; index < pool.Count; index++)
            totalWeight += Mathf.Max(0, pool[index]?.weight ?? 0);

        if (totalWeight <= 0)
            return null;

        int roll = UnityEngine.Random.Range(1, totalWeight + 1);
        int cursor = 0;
        for (int index = 0; index < pool.Count; index++)
        {
            cursor += Mathf.Max(0, pool[index]?.weight ?? 0);
            if (roll <= cursor)
                return pool[index];
        }

        return pool[pool.Count - 1];
    }
}
