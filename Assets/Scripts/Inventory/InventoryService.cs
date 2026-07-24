using System;
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
/// 背包统一入口：把工作台、奖励、消耗品等系统的请求转发给网格背包。
/// 物品真实数据只保存在 GridInventoryService 中，本组件不再维护第二份堆叠列表。
/// </summary>
public class InventoryService : MonoBehaviour
{
    [PropertyTooltip("唯一的网格背包数据服务。场景中建议直接绑定；为空时运行时自动查找。")]
    [SerializeField] private GridInventoryService gridInventoryService;

    public GridInventoryService GridInventory => gridInventoryService;

    private void Awake()
    {
        if (gridInventoryService == null)
            gridInventoryService = FindObjectOfType<GridInventoryService>();
    }

    /// <summary>
    /// 绑定唯一网格背包后端。保留 enableBackend 参数用于兼容现有调用；
    /// 传入 false 时清空后端，传入 true 时使用给定服务。
    /// </summary>
    public void BindGridBackend(GridInventoryService service, bool enableBackend)
    {
        gridInventoryService = enableBackend ? service : null;
        if (gridInventoryService != null) gridInventoryService.EnsureInitialized();
    }

    /// <summary>
    /// 向背包添加物品，返回是否完全添加成功。
    /// </summary>
    public bool AddItem(string itemId, int amount)
    {
        return gridInventoryService != null &&
               gridInventoryService.AddItem(itemId, amount);
    }

    /// <summary>
    /// 从背包消耗物品，返回是否扣除成功。
    /// </summary>
    public bool ConsumeItem(string itemId, int amount)
    {
        return gridInventoryService != null &&
               gridInventoryService.ConsumeItem(itemId, amount);
    }

    /// <summary>
    /// 统计指定物品总数量。
    /// </summary>
    public int CountItem(string itemId)
    {
        return gridInventoryService != null
            ? gridInventoryService.CountItem(itemId)
            : 0;
    }
}
