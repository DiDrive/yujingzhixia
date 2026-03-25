using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class LootEntry
{
    [PropertyTooltip("箱体类型。仅在匹配该类型时参与抽取。")]
    public LootChestType chestType;
    [PropertyTooltip("掉落物品 ID。")]
    public string itemId;
    [PropertyTooltip("单次掉落最小数量。")]
    public int minQuantity = 1;
    [PropertyTooltip("单次掉落最大数量。")]
    public int maxQuantity = 1;
    [PropertyTooltip("权重值。越大越容易被抽中。")]
    public int weight = 1;
}

/// <summary>
/// 掉落生成器：按箱体类型和权重随机发放道具。
/// </summary>
public class LootSpawner : MonoBehaviour
{
    [PropertyTooltip("掉落条目列表。按箱体类型过滤后进行权重抽取。")]
    [SerializeField] private List<LootEntry> entries = new();

    /// <summary>
    /// 按箱体类型进行掉落抽取。
    /// </summary>
    public List<ItemStack> RollByChestType(LootChestType chestType, int rollCount = 1)
    {
        var results = new List<ItemStack>();
        if (rollCount <= 0) return results;

        var pool = CollectPool(chestType);
        if (pool.Count == 0) return results;

        for (var i = 0; i < rollCount; i++)
        {
            var selected = PickWeighted(pool);
            if (selected == null || string.IsNullOrWhiteSpace(selected.itemId)) continue;
            var qty = UnityEngine.Random.Range(selected.minQuantity, selected.maxQuantity + 1);
            results.Add(new ItemStack(selected.itemId, Mathf.Max(1, qty)));
        }

        return results;
    }

    /// <summary>
    /// 生成并直接发放掉落到背包。
    /// </summary>
    public bool GrantLoot(InventoryService inventoryService, LootChestType chestType, int rollCount = 1)
    {
        if (inventoryService == null) return false;
        var stacks = RollByChestType(chestType, rollCount);
        if (stacks.Count == 0) return false;

        var hasAnyGranted = false;
        for (var i = 0; i < stacks.Count; i++)
        {
            var granted = inventoryService.AddItem(stacks[i].itemId, stacks[i].quantity);
            hasAnyGranted = hasAnyGranted || granted;
        }

        EventBus.Publish(new LootGrantedEvent(chestType, stacks.Count));
        return hasAnyGranted;
    }

    /// <summary>
    /// 收集指定箱体类型的掉落池。
    /// </summary>
    private List<LootEntry> CollectPool(LootChestType chestType)//收集指定箱体类型的掉落池
    {
        var pool = new List<LootEntry>();
        for (var i = 0; i < entries.Count; i++)
        {
            var item = entries[i];
            if (item == null) continue;
            if (item.chestType != chestType) continue;
            if (item.weight <= 0) continue;
            pool.Add(item);
        }
        return pool;
    }
    /// <summary>
    /// 从掉落池中按权重随机抽取一个物品。
    /// </summary>
    private LootEntry PickWeighted(List<LootEntry> pool)
    {
        var totalWeight = 0;
        for (var i = 0; i < pool.Count; i++)
        {
            totalWeight += pool[i].weight;
        }

        if (totalWeight <= 0) return null;

        var roll = UnityEngine.Random.Range(1, totalWeight + 1);
        var cursor = 0;
        for (var i = 0; i < pool.Count; i++)
        {
            cursor += pool[i].weight;
            if (roll <= cursor) return pool[i];
        }

        return pool[pool.Count - 1];
    }
}
