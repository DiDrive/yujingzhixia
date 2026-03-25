using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>
/// 庇护所模块服务：负责饮水、休息和基础供电状态管理。
/// </summary>
public class ShelterModuleService : MonoBehaviour
{
    [PropertyTooltip("当前是否有电。断电可用于触发相关玩法事件。")]
    [SerializeField] private bool hasPower = true;
    [PropertyTooltip("每次使用饮水模块恢复的口渴值。")]
    [SerializeField] private float drinkRecoverAmount = 30f;
    [PropertyTooltip("每次使用休息区恢复的疲劳值。")]
    [SerializeField] private float restRecoverAmount = 40f;

    public bool HasPower => hasPower;

    /// <summary>
    /// 更新庇护所供电状态。
    /// </summary>
    public void SetPowerState(bool powered)
    {
        if (hasPower == powered) return;
        hasPower = powered;
        EventBus.Publish(new PowerStateChangedEvent(hasPower));
    }

    /// <summary>
    /// 使用饮水模块恢复口渴值。
    /// </summary>
    public bool UseDrinkStation(SurvivalSystem survivalSystem)
    {
        if (survivalSystem == null) return false;
        survivalSystem.Drink(drinkRecoverAmount);
        return true;
    }

    /// <summary>
    /// 使用休息区恢复疲劳值。
    /// </summary>
    public bool UseRestArea(SurvivalSystem survivalSystem)
    {
        if (survivalSystem == null) return false;
        survivalSystem.Sleep(restRecoverAmount);
        return true;
    }

    /// <summary>
    /// 存放食物到玩家背包系统（用于后续扩展储物逻辑）。
    /// </summary>
    public bool StoreFood(InventoryService inventoryService, string itemId, int quantity)
    {
        if (inventoryService == null) return false;
        return inventoryService.AddItem(itemId, quantity);
    }
}
