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
    public string tableId;
    public int minRolls = 1;
    public int maxRolls = 3;
    public bool allowDuplicates = true;
    public List<LootTableJsonEntry> entries = new();
}

[Serializable]
public class LootTableJsonRoot
{
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
        var source = lootTablesJson != null ? lootTablesJson : Resources.Load<TextAsset>("Data/loot_tables");
        if (source != null && !string.IsNullOrWhiteSpace(source.text))
        {
            var root = JsonUtility.FromJson<LootTableJsonRoot>(source.text);
            RegisterTables(root?.tables);
        }

        RegisterTables(fallbackTables);
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
