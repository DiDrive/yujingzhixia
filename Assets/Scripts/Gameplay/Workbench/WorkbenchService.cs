using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class WorkbenchMaterialRequirement
{
    [PropertyTooltip("所需材料的物品 ID。")]
    public string itemId;
    [PropertyTooltip("所需材料数量。")]
    public int quantity = 1;
}

[Serializable]
public class WorkbenchRecipe
{
    [PropertyTooltip("配方唯一 ID，例如 craft_bandage。")]
    public string recipeId;
    [PropertyTooltip("解锁该配方所需的工作台等级。")]
    public int requiredWorkbenchLevel;
    [PropertyTooltip("产出物品 ID。")]
    public string outputItemId;
    [PropertyTooltip("产出物品数量。")]
    public int outputQuantity = 1;
    [PropertyTooltip("该配方的材料需求列表。")]
    public List<WorkbenchMaterialRequirement> requirements = new();
}

/// <summary>
/// 工作台服务：负责配方校验、制作与基础修复逻辑。
/// </summary>
public class WorkbenchService : MonoBehaviour
{
    [PropertyTooltip("当前工作台等级。影响可用配方。")]
    [SerializeField] private int currentWorkbenchLevel = 1;
    [PropertyTooltip("工作台可用配方列表。")]
    [SerializeField] private List<WorkbenchRecipe> recipes = new();

    public int CurrentWorkbenchLevel => currentWorkbenchLevel;

    /// <summary>
    /// 提升工作台等级。
    /// </summary>
    public void SetWorkbenchLevel(int level)
    {
        currentWorkbenchLevel = Mathf.Max(1, level);
    }

    /// <summary>
    /// 判断配方是否解锁。
    /// </summary>
    public bool IsRecipeUnlocked(string recipeId)
    {
        var recipe = FindRecipe(recipeId);
        if (recipe == null) return false;
        return currentWorkbenchLevel >= recipe.requiredWorkbenchLevel;
    }

    /// <summary>
    /// 尝试制作指定配方。
    /// </summary>
    public bool TryCraft(string recipeId, InventoryService inventoryService)
    {
        if (inventoryService == null) return false;
        var recipe = FindRecipe(recipeId);
        if (recipe == null) return false;
        if (currentWorkbenchLevel < recipe.requiredWorkbenchLevel) return false;
        if (string.IsNullOrWhiteSpace(recipe.outputItemId)) return false;

        if (!CanAfford(recipe, inventoryService)) return false;
        ConsumeRequirements(recipe, inventoryService);
        return inventoryService.AddItem(recipe.outputItemId, Mathf.Max(1, recipe.outputQuantity));
    }

    /// <summary>
    /// 尝试进行武器修复的资源扣除（修复表现由武器系统实现）。
    /// </summary>
    public bool TryPayRepairCost(InventoryService inventoryService, string repairMaterialId, int repairMaterialCost)
    {
        if (inventoryService == null) return false;
        if (string.IsNullOrWhiteSpace(repairMaterialId)) return false;
        if (repairMaterialCost <= 0) return false;
        if (inventoryService.CountItem(repairMaterialId) < repairMaterialCost) return false;
        return inventoryService.ConsumeItem(repairMaterialId, repairMaterialCost);
    }

    private WorkbenchRecipe FindRecipe(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId)) return null;
        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            if (recipe == null) continue;
            if (recipe.recipeId == recipeId) return recipe;
        }
        return null;
    }

    private bool CanAfford(WorkbenchRecipe recipe, InventoryService inventoryService)
    {
        for (var i = 0; i < recipe.requirements.Count; i++)
        {
            var req = recipe.requirements[i];
            if (req == null) return false;
            if (string.IsNullOrWhiteSpace(req.itemId)) return false;
            if (req.quantity <= 0) return false;
            if (inventoryService.CountItem(req.itemId) < req.quantity) return false;
        }
        return true;
    }

    private void ConsumeRequirements(WorkbenchRecipe recipe, InventoryService inventoryService)
    {
        for (var i = 0; i < recipe.requirements.Count; i++)
        {
            var req = recipe.requirements[i];
            inventoryService.ConsumeItem(req.itemId, req.quantity);
        }
    }
}
