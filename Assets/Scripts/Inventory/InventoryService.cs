using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
/// <summary>
/// 背包堆叠结构，保存物品ID与数量。
/// </summary>
public struct ItemStack
{
    [PropertyTooltip("物品唯一标识，例如 food_canned。")]
    public string itemId;
    [PropertyTooltip("该堆叠中的物品数量。")]
    public int quantity;

    public ItemStack(string itemId, int quantity)
    {
        this.itemId = itemId;
        this.quantity = quantity;
    }
}

/// <summary>
/// 背包服务：负责物品增加、消耗与计数。
/// </summary>
public class InventoryService : MonoBehaviour
{
    [PropertyTooltip("是否启用网格背包后端。启用后 Add/Consume/Count 将委托给 GridInventoryService。")]
    [SerializeField] private bool useGridInventoryBackend;
    [PropertyTooltip("网格背包服务引用。")]
    [SerializeField] private GridInventoryService gridInventoryService;
    [PropertyTooltip("背包最大格子数。达到上限后无法新增新堆叠。")]
    [SerializeField] private int maxSlots = 20;
    [PropertyTooltip("单个堆叠的数量上限。")]
    [SerializeField] private int defaultStackLimit = 99;
    [PropertyTooltip("当前背包堆叠列表。")]
    [SerializeField] private List<ItemStack> stacks = new();

    public IReadOnlyList<ItemStack> Stacks => stacks;

    public void BindGridBackend(GridInventoryService service, bool enableBackend)
    {
        gridInventoryService = service;
        var shouldUseGridBackend = enableBackend && gridInventoryService != null;
        if (shouldUseGridBackend && !useGridInventoryBackend && stacks.Count > 0)
        {
            gridInventoryService.EnsureInitialized();
            MigrateStacksToGridBackend();
        }

        useGridInventoryBackend = shouldUseGridBackend;
    }

    /// <summary>
    /// 向背包添加物品，返回是否完全添加成功。
    /// </summary>
    public bool AddItem(string itemId, int amount)
    {
        if (useGridInventoryBackend && gridInventoryService != null)
        {
            return gridInventoryService.AddItem(itemId, amount);
        }

        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0) return false;

        for (var i = 0; i < stacks.Count; i++)
        {
            if (stacks[i].itemId != itemId) continue;
            var canAdd = defaultStackLimit - stacks[i].quantity;
            if (canAdd <= 0) continue;
            var add = Mathf.Min(canAdd, amount);
            stacks[i] = new ItemStack(itemId, stacks[i].quantity + add);
            amount -= add;
            if (amount == 0) return true;
        }

        while (amount > 0 && stacks.Count < maxSlots)
        {
            var add = Mathf.Min(defaultStackLimit, amount);
            stacks.Add(new ItemStack(itemId, add));
            amount -= add;
        }

        return amount == 0;
    }

    /// <summary>
    /// 从背包消耗物品，返回是否扣除成功。
    /// </summary>
    public bool ConsumeItem(string itemId, int amount)
    {
        if (useGridInventoryBackend && gridInventoryService != null)
        {
            return gridInventoryService.ConsumeItem(itemId, amount);
        }

        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0) return false;
        var left = amount;

        for (var i = stacks.Count - 1; i >= 0; i--)
        {
            if (stacks[i].itemId != itemId) continue;
            var take = Mathf.Min(left, stacks[i].quantity);
            var remain = stacks[i].quantity - take;
            left -= take;

            if (remain <= 0) stacks.RemoveAt(i);
            else stacks[i] = new ItemStack(itemId, remain);

            if (left == 0) return true;
        }

        return false;
    }

    /// <summary>
    /// 统计指定物品总数量。
    /// </summary>
    public int CountItem(string itemId)
    {
        if (useGridInventoryBackend && gridInventoryService != null)
        {
            return gridInventoryService.CountItem(itemId);
        }

        var total = 0;
        for (var i = 0; i < stacks.Count; i++)
        {
            if (stacks[i].itemId == itemId) total += stacks[i].quantity;
        }
        return total;
    }

    private void MigrateStacksToGridBackend()
    {
        if (gridInventoryService == null) return;

        var remainingStacks = new List<ItemStack>();
        for (var i = 0; i < stacks.Count; i++)
        {
            var stack = stacks[i];
            if (string.IsNullOrWhiteSpace(stack.itemId) || stack.quantity <= 0) continue;
            if (!gridInventoryService.AddItem(stack.itemId, stack.quantity))
            {
                remainingStacks.Add(stack);
            }
        }

        stacks = remainingStacks;
    }
}
