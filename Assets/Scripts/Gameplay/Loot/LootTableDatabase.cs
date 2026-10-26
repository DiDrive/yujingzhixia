using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class LootTableJsonEntry
{
    public string itemId;
    public int minQuantity = 1;
    public int maxQuantity = 1;
    public int weight = 1;
}

[Serializable]
public class LootTableJsonDefinition
{
    [Tooltip("仅用于说明这张掉落表的用途，不参与掉落计算。")]
    public string description;
    public string tableId;
    public int minRolls = 1;
    public int maxRolls = 3;
    public bool allowDuplicates = true;
    public List<LootTableJsonEntry> entries = new();
}

[Serializable]
public class LootTableJsonRoot
{
    [Tooltip("掉落表格式说明，仅供策划和开发人员阅读。")]
    public string documentation;
    public List<LootTableJsonDefinition> tables = new();
}

public class LootTableDatabase : MonoBehaviour
{
    [Tooltip("可选的掉落表 JSON 文件。为空时自动读取 Resources/Data/loot_tables.json。")]
    [SerializeField] private TextAsset lootTablesJson;
    [Tooltip("JSON 中没有对应 tableId 时可使用的 Inspector 备用掉落表。")]
    [SerializeField] private List<LootTableJsonDefinition> fallbackTables = new();
    private readonly Dictionary<string, LootTableJsonDefinition> tableById = new();

    private void Awake()
    {
        Reload();
    }

    public void Reload()
    {
        tableById.Clear();
        var json = GameContentService.LootTablesJson;

        #if UNITY_EDITOR
        // 仅用于直接打开业务场景调试。
        if (string.IsNullOrWhiteSpace(json))
        {
            var source = lootTablesJson != null
                ? lootTablesJson
                : Resources.Load<TextAsset>("Data/loot_tables");

            if (source != null)
                json = source.text;
        }
        #endif

        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError("无法读取掉落表 JSON");
        }
        else
        {
            var root =
                JsonUtility.FromJson<LootTableJsonRoot>(json);

            RegisterTables(root?.tables);
        }
    }

    public List<ItemStack> Roll(string tableId, int minRollsOverride = -1, int maxRollsOverride = -1)
    {
        var result = new List<ItemStack>();
        if (string.IsNullOrWhiteSpace(tableId) || !tableById.TryGetValue(tableId, out var table)) return result;
        if (table.entries == null || table.entries.Count == 0) return result;

        var minRolls = minRollsOverride >= 0 ? minRollsOverride : table.minRolls;
        var maxRolls = maxRollsOverride >= 0 ? maxRollsOverride : table.maxRolls;
        minRolls = Mathf.Max(0, minRolls);
        maxRolls = Mathf.Max(minRolls, maxRolls);
        var rollCount = UnityEngine.Random.Range(minRolls, maxRolls + 1);
        var pool = new List<LootTableJsonEntry>(table.entries);

        for (var i = 0; i < rollCount && pool.Count > 0; i++)
        {
            var selected = PickWeighted(pool);
            if (selected == null || string.IsNullOrWhiteSpace(selected.itemId)) continue;
            var minQuantity = Mathf.Max(1, selected.minQuantity);
            var maxQuantity = Mathf.Max(minQuantity, selected.maxQuantity);
            result.Add(new ItemStack(selected.itemId, UnityEngine.Random.Range(minQuantity, maxQuantity + 1)));
            if (!table.allowDuplicates) pool.Remove(selected);
        }

        return result;
    }

    /// <summary>
    /// 从指定 JSON 掉落表抽取物品并发放到统一背包入口。
    /// 返回成功写入背包的堆叠数量；背包空间不足的堆叠不会计入。
    /// </summary>
    public int GrantToInventory(
        InventoryService inventoryService,
        string tableId,
        int minRollsOverride = -1,
        int maxRollsOverride = -1)
    {
        if (inventoryService == null || string.IsNullOrWhiteSpace(tableId)) return 0;

        var stacks = Roll(tableId, minRollsOverride, maxRollsOverride);
        var grantedStackCount = 0;
        for (var i = 0; i < stacks.Count; i++)
        {
            var stack = stacks[i];
            if (inventoryService.AddItem(stack.itemId, stack.quantity))
                grantedStackCount++;
        }

        EventBus.Publish(new LootGrantedEvent(tableId, grantedStackCount));
        return grantedStackCount;
    }

    private void RegisterTables(List<LootTableJsonDefinition> tables)
    {
        if (tables == null) return;
        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            if (table == null || string.IsNullOrWhiteSpace(table.tableId)) continue;
            if (!tableById.ContainsKey(table.tableId)) tableById.Add(table.tableId, table);
        }
    }

    private static LootTableJsonEntry PickWeighted(List<LootTableJsonEntry> pool)
    {
        var totalWeight = 0;
        for (var i = 0; i < pool.Count; i++) totalWeight += Mathf.Max(0, pool[i]?.weight ?? 0);
        if (totalWeight <= 0) return null;

        var roll = UnityEngine.Random.Range(1, totalWeight + 1);
        var cursor = 0;
        for (var i = 0; i < pool.Count; i++)
        {
            cursor += Mathf.Max(0, pool[i]?.weight ?? 0);
            if (roll <= cursor) return pool[i];
        }

        return pool[pool.Count - 1];
    }
}
