using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 网格背包的数据服务，统一管理容器定义、物品定义、占用格和物品实例。
/// </summary>
public class GridInventoryService : MonoBehaviour
{
    [Serializable]
    private class ItemDefinitionJsonTable
    {
        public List<GridItemUseProfile> useProfiles = new();
        public List<GridItemUseProfileAssignment> itemUseProfileAssignments = new();
        public List<GridItemDefinition> items = new();
    }

    private struct StackCountSnapshot
    {
        public string instanceId;
        public int stackCount;
    }

    [Tooltip("容器定义列表。")]
    [SerializeField] private List<GridContainerDefinition> containerDefinitions = new();
    [Tooltip("物品定义列表。")]
    [SerializeField] private List<GridItemDefinition> itemDefinitions = new();
    [Tooltip("可选的物品 JSON 表。正式流程由 YooAsset 加载；编辑器直接运行场景时从 Assets/GameRes/Config 读取。")]
    [SerializeField] private TextAsset itemDefinitionsJson;
    [Tooltip("启动时优先用 JSON 覆盖 Inspector 中的物品定义。正式转表后再开启。")]
    [SerializeField] private bool preferJsonItemDefinitions;
    [Tooltip("是否在游戏启动时读取场景 ItemLayer 下的 GridInventoryEditorItem 节点作为默认背包内容。")]
    [SerializeField] private bool loadEditorItemNodesOnStart = true;
    [Tooltip("快速转移时的容器优先级。")]
    [SerializeField] private List<string> quickTransferPriorityContainerIds = new();

    private readonly Dictionary<string, GridContainerDefinition> containerDefinitionById = new();
    private readonly Dictionary<string, GridContainerOwnerType> containerOwnerById = new();
    private readonly Dictionary<string, GridItemDefinition> itemDefinitionById = new();
    private readonly Dictionary<string, GridOccupancyMap> occupancyByContainerId = new();
    private readonly Dictionary<string, int> unlockedCellCountByContainerId = new();
    private readonly Dictionary<string, GridItemInstance> itemInstanceById = new();
    private readonly Dictionary<string, List<string>> containerItemInstanceIds = new();

    public IReadOnlyDictionary<string, GridItemInstance> ItemInstances => itemInstanceById;
    public IReadOnlyCollection<string> ContainerIds => containerDefinitionById.Keys;
    public IEnumerable<string> PlayerContainerIds => containerOwnerById
        .Where(pair => pair.Value == GridContainerOwnerType.Player)
        .Select(pair => pair.Key);

    public bool HasSceneEditorItemNodes()
    {
        var editorItems = FindObjectsOfType<GridInventoryEditorItem>(true);
        for (var i = 0; i < editorItems.Length; i++)
        {
            var editorItem = editorItems[i];
            if (editorItem != null && editorItem.gameObject.scene.IsValid()) return true;
        }

        return false;
    }

    private void Awake()
    {
        EnsureInitialized();
    }

    private void Start()
    {
        if (loadEditorItemNodesOnStart) LoadEditorItemNodes();
    }

    public void EnsureInitialized()
    {
        if (containerDefinitionById.Count > 0 && itemDefinitionById.Count > 0) return;
        EnsureDemoData();
        RebuildRuntime();
    }

    [ContextMenu("Ensure Demo Data")]
    public void EnsureDemoData()
    {
        if (containerDefinitions.Count == 0)
        {
            containerDefinitions.Add(new GridContainerDefinition
            {
                containerId = "main_bag",
                rows = 8,
                columns = 10,
                allowNestedContainer = false,
                allowedItemTags = new List<string>(),// 所有物品都允许放入主背包
                ownerType = GridContainerOwnerType.Player
            });
            containerDefinitions.Add(new GridContainerDefinition
            {
                containerId = "pocket",
                rows = 2,
                columns = 3,
                allowNestedContainer = false,
                allowedItemTags = new List<string> { "consumable", "medical" },
                ownerType = GridContainerOwnerType.Player
            });
        }

        if (GameContentService.IsReady)
        {
            if (!TryLoadItemDefinitionsFromJson(true))
            {
                Debug.LogError("YooAsset 物品配置加载失败");
            }
        }
        else if (preferJsonItemDefinitions)
        {
            TryLoadItemDefinitionsFromJson(true);
        }
        else if (itemDefinitions.Count == 0)
        {
            TryLoadItemDefinitionsFromJson(false);
        }

        // JSON 和 Inspector 均未提供定义时才使用兜底数据；品质也会决定逐物品搜索耗时。
        if (itemDefinitions.Count == 0)
        {
            itemDefinitions.Add(new GridItemDefinition
            {
                itemId = "food_canned",
                width = 1,
                height = 1,
                canRotate = false,
                maxStack = 3,
                itemTags = new List<string> { "consumable" },
                weight = 0.5f,
                quality = GridItemQuality.Common
            });
            itemDefinitions.Add(new GridItemDefinition
            {
                itemId = "water_bottle",
                width = 1,
                height = 2,
                canRotate = true,
                maxStack = 2,
                itemTags = new List<string> { "consumable" },
                weight = 0.8f,
                quality = GridItemQuality.Uncommon
            });
            itemDefinitions.Add(new GridItemDefinition
            {
                itemId = "bandage",
                width = 1,
                height = 1,
                canRotate = false,
                maxStack = 5,
                itemTags = new List<string> { "medical" },
                weight = 0.2f,
                quality = GridItemQuality.Common
            });
            itemDefinitions.Add(new GridItemDefinition
            {
                itemId = "cloth_raw",
                width = 2,
                height = 1,
                canRotate = true,
                maxStack = 4,
                itemTags = new List<string> { "material" },
                weight = 0.4f,
                quality = GridItemQuality.Uncommon
            });
        }

        if (quickTransferPriorityContainerIds.Count == 0)
        {
            quickTransferPriorityContainerIds.Add("pocket");
            quickTransferPriorityContainerIds.Add("main_bag");
        }
    }

    [ContextMenu("Rebuild Runtime")]
    public void RebuildRuntime()
    {
        containerDefinitionById.Clear();
        containerOwnerById.Clear();
        itemDefinitionById.Clear();
        occupancyByContainerId.Clear();
        unlockedCellCountByContainerId.Clear();
        containerItemInstanceIds.Clear();
        itemInstanceById.Clear();

        for (var i = 0; i < containerDefinitions.Count; i++)
        {
            var definition = containerDefinitions[i];
            if (string.IsNullOrWhiteSpace(definition.containerId)) continue;
            if (definition.rows <= 0 || definition.columns <= 0) continue;

            containerDefinitionById[definition.containerId] = definition;
            containerOwnerById[definition.containerId] = definition.ownerType;
            var occupancy = new GridOccupancyMap(definition.rows, definition.columns);
            var unlockedCount = NormalizeUnlockedCellCount(definition.initialUnlockedCellCount, definition.rows, definition.columns);
            occupancy.SetUnlockedCellCount(unlockedCount);
            occupancyByContainerId[definition.containerId] = occupancy;
            unlockedCellCountByContainerId[definition.containerId] = unlockedCount;
            containerItemInstanceIds[definition.containerId] = new List<string>();
        }

        for (var i = 0; i < itemDefinitions.Count; i++)
        {
            var definition = itemDefinitions[i];
            if (string.IsNullOrWhiteSpace(definition.itemId)) continue;
            if (definition.width <= 0 || definition.height <= 0) continue;
            itemDefinitionById[definition.itemId] = definition;
        }
    }

    /// <summary>
    /// 将场景 ItemLayer 中的编辑器物品节点转为本次运行的物品实例。
    /// </summary>
    public void LoadEditorItemNodes()
    {
        var editorItems = FindObjectsOfType<GridInventoryEditorItem>(true);
        for (var i = 0; i < editorItems.Length; i++)
        {
            var editorItem = editorItems[i];
            if (editorItem == null || !editorItem.gameObject.scene.IsValid()) continue;
            if (string.IsNullOrWhiteSpace(editorItem.ItemId)) continue;
            if (!containerDefinitionById.ContainsKey(editorItem.ContainerId)) continue;

            if (!TryCreateItemInstance(editorItem.ItemId, editorItem.StackCount, out var instanceId)) continue;
            if (!TryPlace(instanceId, editorItem.ContainerId, editorItem.Row, editorItem.Column, editorItem.Rotation))
            {
                RemoveInstanceInternal(instanceId);
                continue;
            }

            editorItem.SetRuntimeInstanceId(instanceId);
        }

        var inventoryUis = FindObjectsOfType<GridInventoryUnityUI>(true);
        for (var i = 0; i < inventoryUis.Length; i++)
        {
            if (inventoryUis[i] != null) inventoryUis[i].RefreshEditorItemBindings(this);
        }
    }

    /// <summary>
    /// 尝试创建物品实例。
    /// </summary>
    /// <param name="itemId">物品ID。</param>
    /// <param name="stackCount">物品数量。</param>
    /// <param name="instanceId">创建的物品实例ID。</param>
    /// <returns>是否成功创建物品实例。</returns>
    public bool TryCreateItemInstance(string itemId, int stackCount, out string instanceId)
    {
        instanceId = null;
        if (string.IsNullOrWhiteSpace(itemId) || stackCount <= 0) return false;
        if (!itemDefinitionById.TryGetValue(itemId, out var itemDefinition)) return false;
        if (stackCount > itemDefinition.maxStack && itemDefinition.maxStack > 0) return false;

        instanceId = Guid.NewGuid().ToString("N");
        var instance = new GridItemInstance
        {
            instanceId = instanceId,
            itemId = itemId,
            stackCount = stackCount,
            rotation = GridItemRotation.Deg0,
            containerId = null,
            originRow = -1,
            originColumn = -1
        };
        itemInstanceById[instanceId] = instance;
        return true;
    }

    /// <summary>
    /// 向背包添加物品，返回是否完全添加成功。
    /// </summary>
    /// <param name="itemId">物品ID。</param>
    /// <param name="amount">物品数量。</param>
    /// <returns>是否成功添加物品。</returns>
    public bool AddItem(string itemId, int amount)
    {
        return TryAddItemToContainers(itemId, amount, BuildPlayerContainerPriority());
    }

    public bool TryAddItemToContainer(string containerId, string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(containerId)) return false;
        return TryAddItemToContainers(itemId, amount, new List<string> { containerId });
    }

    /// <summary>
    /// 尝试将物品实例放置到指定容器的指定位置。
    /// </summary>
    /// <param name="itemId">物品ID。</param>
    /// <param name="containerId">容器ID。</param>
    /// <param name="row">行索引。</param>
    /// <param name="column">列索引。</param>
    /// <param name="rotation">旋转角度。</param>
    /// <param name="stackCount">物品数量。</param>
    /// <returns>是否成功放置物品实例。</returns>
    public bool TrySpawnToCell(string itemId, string containerId, int row, int column, GridItemRotation rotation = GridItemRotation.Deg0, int stackCount = 1)
    {
        return TrySpawnToCell(itemId, containerId, row, column, rotation, stackCount, out _);
    }

    /// <summary>
    /// 在指定格子恢复物品，并返回本次创建的新实例 ID，供外部状态恢复时重新建立引用。
    /// </summary>
    public bool TrySpawnToCell(
        string itemId,
        string containerId,
        int row,
        int column,
        GridItemRotation rotation,
        int stackCount,
        out string instanceId)
    {
        if (!TryCreateItemInstance(itemId, stackCount, out instanceId)) return false;
        if (TryPlace(instanceId, containerId, row, column, rotation)) return true;
        RemoveInstanceInternal(instanceId);
        instanceId = null;
        return false;
    }

    /// <summary>
    /// 尝试消耗物品实例。
    /// </summary>
    /// <param name="itemId">物品ID。</param>
    /// <param name="amount">物品数量。</param>
    /// <returns>是否成功消耗物品实例。</returns>
    public bool ConsumeItem(string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0) return false;
        if (CountItem(itemId) < amount) return false;

        var pending = amount;
        var candidates = itemInstanceById.Values
            .Where(instance => instance.itemId == itemId)
            .Where(instance => IsPlayerContainer(instance.containerId))
            .OrderBy(instance => instance.stackCount)
            .ToList();

        for (var i = 0; i < candidates.Count && pending > 0; i++)
        {
            var instance = candidates[i];
            if (!itemInstanceById.TryGetValue(instance.instanceId, out var current)) continue;

            var consume = Mathf.Min(current.stackCount, pending);
            current.stackCount -= consume;
            pending -= consume;

            if (current.stackCount <= 0)
            {
                RemoveInstanceInternal(current.instanceId);
                continue;
            }

            itemInstanceById[current.instanceId] = current;
        }

        return pending == 0;
    }

    /// <summary>
    /// 尝试将物品实例放置到指定容器的指定位置。
    /// </summary>
    /// <param name="instanceId">物品实例ID。</param>
    /// <param name="containerId">容器ID。</param>
    /// <param name="originRow">行索引。</param>
    /// <param name="originColumn">列索引。</param>
    /// <param name="rotation">旋转角度。</param>
    /// <returns>是否成功放置物品实例。</returns>
    public bool TryPlace(string instanceId, string containerId, int originRow, int originColumn, GridItemRotation rotation)
    {
        if (!itemInstanceById.TryGetValue(instanceId, out var instance)) return false;
        if (!itemDefinitionById.TryGetValue(instance.itemId, out var itemDefinition)) return false;
        if (!containerDefinitionById.TryGetValue(containerId, out var containerDefinition)) return false;
        if (!occupancyByContainerId.TryGetValue(containerId, out var targetOccupancy)) return false;
        if (!CanContainerAcceptItem(containerDefinition, itemDefinition)) return false;
        if (!GridPlacementSolver.TryGetFootprint(itemDefinition, rotation, out var width, out var height)) return false;

        var oldContainerId = instance.containerId;
        var oldRow = instance.originRow;
        var oldColumn = instance.originColumn;
        var oldRotation = instance.rotation;
        var hadOldPlacement = HasPlacement(instance);

        if (hadOldPlacement && occupancyByContainerId.TryGetValue(oldContainerId, out var oldOccupancy))
        {
            oldOccupancy.ClearAreaByInstance(instanceId);
        }

        var ignoreInstanceId = containerId == oldContainerId ? instanceId : null;
        if (!targetOccupancy.TryFillArea(originRow, originColumn, width, height, instanceId, ignoreInstanceId))
        {
            RestoreOldPlacement(instance, oldContainerId, oldRow, oldColumn, oldRotation);
            return false;
        }

        UpdateContainerMembership(instanceId, oldContainerId, containerId);
        instance.containerId = containerId;
        instance.originRow = originRow;
        instance.originColumn = originColumn;
        instance.rotation = rotation;
        itemInstanceById[instanceId] = instance;
        return true;
    }

    /// <summary>
    /// 尝试将物品实例移动到指定容器的指定位置。
    /// </summary>
    /// <param name="instanceId">物品实例ID。</param>
    /// <param name="targetContainerId">目标容器ID。</param>
    /// <param name="targetRow">目标行索引。</param>
    /// <param name="targetColumn">目标列索引。</param>
    /// <returns>是否成功移动物品实例。</returns>
    public bool TryMove(string instanceId, string targetContainerId, int targetRow, int targetColumn)
    {
        if (!itemInstanceById.TryGetValue(instanceId, out var instance)) return false;
        return TryPlace(instanceId, targetContainerId, targetRow, targetColumn, instance.rotation);
    }

    /// <summary>
    /// 尝试将物品实例旋转。
    /// </summary>
    /// <param name="instanceId">物品实例ID。</param>
    /// <returns>是否成功旋转物品实例。</returns>
    public bool TryRotate(string instanceId)
    {
        if (!itemInstanceById.TryGetValue(instanceId, out var instance)) return false;
        if (!itemDefinitionById.TryGetValue(instance.itemId, out var definition)) return false;
        if (!definition.canRotate) return false;
        if (!HasPlacement(instance)) return false;

        var next = instance.rotation == GridItemRotation.Deg0 ? GridItemRotation.Deg90 : GridItemRotation.Deg0;
        return TryPlace(instanceId, instance.containerId, instance.originRow, instance.originColumn, next);
    }

    public bool TryQuickTransfer(string instanceId)
    {
        if (!itemInstanceById.TryGetValue(instanceId, out var instance)) return false;
        if (!itemDefinitionById.TryGetValue(instance.itemId, out var definition)) return false;

        var priorities = BuildContainerPriority(instance.containerId);
        for (var i = 0; i < priorities.Count; i++)
        {
            var containerId = priorities[i];
            if (!containerDefinitionById.TryGetValue(containerId, out var containerDefinition)) continue;
            if (!CanContainerAcceptItem(containerDefinition, definition)) continue;
            if (!occupancyByContainerId.TryGetValue(containerId, out var occupancy)) continue;

            var ignoreId = containerId == instance.containerId ? instanceId : null;
            if (!GridPlacementSolver.FindFirstFit(occupancy, definition, out var placement, true, ignoreId)) continue;
            if (TryPlace(instanceId, containerId, placement.row, placement.column, placement.rotation)) return true;
        }

        return false;
    }

    public bool TryQuickTransferToPlayer(string instanceId)
    {
        if (!itemInstanceById.TryGetValue(instanceId, out var instance)) return false;
        if (IsPlayerContainer(instance.containerId)) return TryQuickTransfer(instanceId);

        var priorities = BuildPlayerContainerPriority();
        for (var i = 0; i < priorities.Count; i++)
        {
            if (!TryPlaceFirstFit(instanceId, priorities[i])) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 自动排序容器内的物品实例。
    /// </summary>
    /// <param name="containerId">容器ID。</param>
    /// <returns>自动排序结果。</returns>
    public GridAutoSortResult AutoSort(string containerId)
    {
        if (!containerItemInstanceIds.TryGetValue(containerId, out var ids)) return new GridAutoSortResult(false, Array.Empty<string>());
        if (!occupancyByContainerId.TryGetValue(containerId, out var occupancy)) return new GridAutoSortResult(false, Array.Empty<string>());

        var sortable = BuildCompactedStacksForSort(containerId, ids)
            .Select(id => itemInstanceById.TryGetValue(id, out var instance) ? instance : default)
            .Where(instance => !string.IsNullOrWhiteSpace(instance.instanceId))
            .Where(instance => itemDefinitionById.ContainsKey(instance.itemId))
            .OrderBy(instance => GetItemTypeSortKey(containerId, instance.itemId))
            .ThenBy(instance => instance.itemId)
            .ThenByDescending(instance => GetArea(itemDefinitionById[instance.itemId]))
            .ThenByDescending(instance => instance.stackCount)
            .ToList();

        var failedIds = new List<string>();
        occupancyByContainerId[containerId] = new GridOccupancyMap(occupancy.Rows, occupancy.Columns);
        occupancyByContainerId[containerId].SetUnlockedCellCount(GetUnlockedCellCount(containerId));
        containerItemInstanceIds[containerId].Clear();
        var rebuiltOccupancy = occupancyByContainerId[containerId];

        for (var i = 0; i < sortable.Count; i++)
        {
            var instance = sortable[i];
            var definition = itemDefinitionById[instance.itemId];
            if (!GridPlacementSolver.FindFirstFit(rebuiltOccupancy, definition, out var placement, true))
            {
                failedIds.Add(instance.instanceId);
                continue;
            }

            if (!GridPlacementSolver.TryGetFootprint(definition, placement.rotation, out var width, out var height))
            {
                failedIds.Add(instance.instanceId);
                continue;
            }

            if (!rebuiltOccupancy.TryFillArea(placement.row, placement.column, width, height, instance.instanceId))
            {
                failedIds.Add(instance.instanceId);
                continue;
            }

            instance.containerId = containerId;
            instance.originRow = placement.row;
            instance.originColumn = placement.column;
            instance.rotation = placement.rotation;
            itemInstanceById[instance.instanceId] = instance;
            containerItemInstanceIds[containerId].Add(instance.instanceId);
        }

        return new GridAutoSortResult(failedIds.Count == 0, failedIds);
    }

    private List<string> BuildCompactedStacksForSort(string containerId, List<string> ids)
    {
        var result = new List<string>();
        var sourceInstances = ids
            .Select(id => itemInstanceById.TryGetValue(id, out var instance) ? instance : default)
            .Where(instance => !string.IsNullOrWhiteSpace(instance.instanceId))
            .Where(instance => instance.containerId == containerId)
            .Where(instance => itemDefinitionById.ContainsKey(instance.itemId))
            .ToList();

        var groups = sourceInstances
            .GroupBy(instance => instance.itemId)
            .OrderBy(group => GetItemTypeSortKey(containerId, group.Key))
            .ThenBy(group => group.Key);

        foreach (var group in groups)
        {
            var definition = itemDefinitionById[group.Key];
            var stackLimit = definition.maxStack > 0 ? definition.maxStack : int.MaxValue;
            var pending = group.Sum(instance => instance.stackCount);
            var reusable = group
                .OrderBy(instance => instance.originRow < 0 ? int.MaxValue : instance.originRow)
                .ThenBy(instance => instance.originColumn < 0 ? int.MaxValue : instance.originColumn)
                .ThenByDescending(instance => instance.stackCount)
                .ToList();
            var reusableIndex = 0;

            while (pending > 0)
            {
                var stackCount = stackLimit == int.MaxValue ? pending : Mathf.Min(stackLimit, pending);
                GridItemInstance instance;
                if (reusableIndex < reusable.Count)
                {
                    instance = reusable[reusableIndex++];
                }
                else if (!TryCreateItemInstance(group.Key, stackCount, out var newInstanceId) ||
                         !itemInstanceById.TryGetValue(newInstanceId, out instance))
                {
                    break;
                }

                instance.stackCount = stackCount;
                instance.containerId = null;
                instance.originRow = -1;
                instance.originColumn = -1;
                instance.rotation = GridItemRotation.Deg0;
                itemInstanceById[instance.instanceId] = instance;
                result.Add(instance.instanceId);
                pending -= stackCount;
            }

            for (var i = reusableIndex; i < reusable.Count; i++)
            {
                RemoveInstanceInternal(reusable[i].instanceId);
            }
        }

        return result;
    }

    /// <summary>
    /// 统计物品ID在背包中的数量。
    /// </summary>
    /// <param name="itemId">物品ID。</param>
    /// <returns>物品数量。</returns>
    public int CountItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return 0;
        var total = 0;
        foreach (var pair in itemInstanceById)
        {
            if (pair.Value.itemId != itemId) continue;
            if (!IsPlayerContainer(pair.Value.containerId)) continue;
            total += pair.Value.stackCount;
        }
        return total;
    }

    public int CountItemInContainer(string containerId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(containerId) || string.IsNullOrWhiteSpace(itemId)) return 0;
        var total = 0;
        foreach (var instance in itemInstanceById.Values)
        {
            if (instance.containerId == containerId && instance.itemId == itemId)
            {
                total += instance.stackCount;
            }
        }

        return total;
    }

    public bool TryGetContainerOwner(string containerId, out GridContainerOwnerType ownerType)
    {
        return containerOwnerById.TryGetValue(containerId, out ownerType);
    }

    public bool RegisterRuntimeContainer(
        string containerId,
        int rows,
        int columns,
        GridContainerOwnerType ownerType = GridContainerOwnerType.WorldLoot,
        bool allowNestedContainer = false,
        List<string> allowedItemTags = null,
        int initialUnlockedCellCount = 0)
    {
        if (string.IsNullOrWhiteSpace(containerId) || rows <= 0 || columns <= 0) return false;
        if (containerDefinitionById.ContainsKey(containerId)) return true;

        var definition = new GridContainerDefinition
        {
            containerId = containerId,
            rows = rows,
            columns = columns,
            initialUnlockedCellCount = initialUnlockedCellCount,
            allowNestedContainer = allowNestedContainer,
            allowedItemTags = allowedItemTags ?? new List<string>(),
            ownerType = ownerType
        };
        containerDefinitionById[containerId] = definition;
        containerOwnerById[containerId] = ownerType;
        var occupancy = new GridOccupancyMap(rows, columns);
        var unlockedCount = NormalizeUnlockedCellCount(initialUnlockedCellCount, rows, columns);
        occupancy.SetUnlockedCellCount(unlockedCount);
        occupancyByContainerId[containerId] = occupancy;
        unlockedCellCountByContainerId[containerId] = unlockedCount;
        containerItemInstanceIds[containerId] = new List<string>();
        return true;
    }

    public bool UnregisterRuntimeContainer(string containerId, bool removeContainedItems = true)
    {
        if (!containerDefinitionById.ContainsKey(containerId)) return false;
        if (removeContainedItems && containerItemInstanceIds.TryGetValue(containerId, out var ids))
        {
            var copy = ids.ToArray();
            for (var i = 0; i < copy.Length; i++) RemoveInstanceInternal(copy[i]);
        }
        else if (containerItemInstanceIds.TryGetValue(containerId, out var remaining) && remaining.Count > 0)
        {
            return false;
        }

        containerDefinitionById.Remove(containerId);
        containerOwnerById.Remove(containerId);
        occupancyByContainerId.Remove(containerId);
        unlockedCellCountByContainerId.Remove(containerId);
        containerItemInstanceIds.Remove(containerId);
        return true;
    }

    /// <summary>
    /// 开始新一局时移除上一张地图留下的搜索容器和尸体容器。
    /// 玩家背包与基地 Storage 容器不会被清理，因此撤离物资和仓库内容能够跨场景保留。
    /// </summary>
    public void ClearTransientWorldContainers()
    {
        var transientIds = containerOwnerById
            .Where(pair => pair.Value == GridContainerOwnerType.WorldLoot ||
                           pair.Value == GridContainerOwnerType.Corpse)
            .Select(pair => pair.Key)
            .ToArray();

        for (var i = 0; i < transientIds.Length; i++)
            UnregisterRuntimeContainer(transientIds[i], true);
    }

    /// <summary>
    /// 导出玩家容器和基地 Storage 容器。世界掉落与尸体由各自的稳定状态协议保存。
    /// </summary>
    public GridInventorySaveData CapturePersistentSaveData()
    {
        EnsureInitialized();
        var snapshot = new GridInventorySaveData();
        var containerIds = containerOwnerById
            .Where(pair => pair.Value == GridContainerOwnerType.Player ||
                           pair.Value == GridContainerOwnerType.Storage)
            .Select(pair => pair.Key)
            .OrderBy(id => id)
            .ToArray();

        for (var i = 0; i < containerIds.Length; i++)
        {
            var containerId = containerIds[i];
            if (!containerDefinitionById.TryGetValue(containerId, out var definition)) continue;
            var savedContainer = new GridInventoryContainerSaveData
            {
                containerId = containerId,
                rows = definition.rows,
                columns = definition.columns,
                ownerType = definition.ownerType,
                unlockedCellCount = GetUnlockedCellCount(containerId)
            };

            if (containerItemInstanceIds.TryGetValue(containerId, out var instanceIds))
            {
                var instances = instanceIds
                    .Where(itemInstanceById.ContainsKey)
                    .Select(id => itemInstanceById[id])
                    .OrderBy(item => item.originRow)
                    .ThenBy(item => item.originColumn)
                    .ThenBy(item => item.itemId)
                    .ToArray();
                for (var itemIndex = 0; itemIndex < instances.Length; itemIndex++)
                {
                    var item = instances[itemIndex];
                    savedContainer.items.Add(new GridInventoryItemSaveData
                    {
                        itemId = item.itemId,
                        stackCount = item.stackCount,
                        rotation = item.rotation,
                        row = item.originRow,
                        column = item.originColumn
                    });
                }
            }

            snapshot.containers.Add(savedContainer);
        }

        return snapshot;
    }

    /// <summary>
    /// 事务式恢复持久容器。失败时尽力回滚到调用前状态，避免损坏当前运行数据。
    /// </summary>
    public bool RestorePersistentSaveData(GridInventorySaveData data, out string error)
    {
        EnsureInitialized();
        var backup = CapturePersistentSaveData();
        if (TryRestorePersistentSaveData(data, out error)) return true;

        if (!TryRestorePersistentSaveData(backup, out var rollbackError))
            Debug.LogError("背包读档失败后的内存回滚也失败：" + rollbackError, this);
        return false;
    }

    private bool TryRestorePersistentSaveData(GridInventorySaveData data, out string error)
    {
        error = null;
        if (data?.containers == null)
        {
            error = "inventory.containers 为空。";
            return false;
        }

        var seenContainerIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < data.containers.Count; i++)
        {
            var saved = data.containers[i];
            if (saved == null || string.IsNullOrWhiteSpace(saved.containerId))
            {
                error = $"第 {i} 个容器缺少 ID。";
                return false;
            }
            if (!seenContainerIds.Add(saved.containerId))
            {
                error = "存档包含重复容器：" + saved.containerId;
                return false;
            }
            if (saved.rows <= 0 || saved.columns <= 0 ||
                (saved.ownerType != GridContainerOwnerType.Player &&
                 saved.ownerType != GridContainerOwnerType.Storage))
            {
                error = "存档容器参数无效：" + saved.containerId;
                return false;
            }

            if (containerDefinitionById.TryGetValue(saved.containerId, out var existing))
            {
                if (existing.rows != saved.rows || existing.columns != saved.columns ||
                    (existing.ownerType != GridContainerOwnerType.Player &&
                     existing.ownerType != GridContainerOwnerType.Storage))
                {
                    error = $"容器结构不兼容：{saved.containerId}。";
                    return false;
                }
            }
            else if (!RegisterRuntimeContainer(
                         saved.containerId,
                         saved.rows,
                         saved.columns,
                         saved.ownerType,
                         false,
                         null,
                         saved.unlockedCellCount))
            {
                error = "无法创建存档中的容器：" + saved.containerId;
                return false;
            }

            if (saved.items == null) continue;
            for (var itemIndex = 0; itemIndex < saved.items.Count; itemIndex++)
            {
                var item = saved.items[itemIndex];
                if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.stackCount <= 0 ||
                    !itemDefinitionById.TryGetValue(item.itemId, out var definition) ||
                    (definition.maxStack > 0 && item.stackCount > definition.maxStack))
                {
                    error = $"容器 {saved.containerId} 的第 {itemIndex} 个物品无效或定义已不存在。";
                    return false;
                }
            }
        }

        var persistentContainerIds = containerOwnerById
            .Where(pair => pair.Value == GridContainerOwnerType.Player ||
                           pair.Value == GridContainerOwnerType.Storage)
            .Select(pair => pair.Key)
            .ToArray();
        for (var i = 0; i < persistentContainerIds.Length; i++)
        {
            if (!containerItemInstanceIds.TryGetValue(persistentContainerIds[i], out var ids)) continue;
            var copy = ids.ToArray();
            for (var itemIndex = 0; itemIndex < copy.Length; itemIndex++)
                RemoveInstanceInternal(copy[itemIndex]);
        }

        for (var i = 0; i < data.containers.Count; i++)
        {
            var saved = data.containers[i];
            SetUnlockedCellCountFromSave(saved.containerId, saved.unlockedCellCount);
            if (saved.items == null) continue;
            var items = saved.items
                .OrderBy(item => item.row)
                .ThenBy(item => item.column)
                .ThenBy(item => item.itemId)
                .ToArray();
            for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
            {
                var item = items[itemIndex];
                if (TrySpawnToCell(
                        item.itemId,
                        saved.containerId,
                        item.row,
                        item.column,
                        item.rotation,
                        item.stackCount)) continue;

                error = $"无法恢复 {saved.containerId} 中的 {item.itemId}，位置=({item.row},{item.column})。";
                return false;
            }
        }

        return true;
    }

    private void SetUnlockedCellCountFromSave(string containerId, int savedCount)
    {
        if (!occupancyByContainerId.TryGetValue(containerId, out var occupancy)) return;
        var total = occupancy.Rows * occupancy.Columns;
        var normalized = savedCount <= 0 ? total : Mathf.Clamp(savedCount, 1, total);
        unlockedCellCountByContainerId[containerId] = normalized;
        occupancy.SetUnlockedCellCount(normalized);
    }

    /// <summary>
    /// 尝试获取容器的行数和列数。
    /// </summary>
    /// <param name="containerId">容器ID。</param>
    /// <param name="rows">行数。</param>
    /// <param name="columns">列数。</param>
    /// <returns>是否成功获取容器大小。</returns>
    public bool TryGetContainerSize(string containerId, out int rows, out int columns)
    {
        rows = 0;
        columns = 0;
        if (!occupancyByContainerId.TryGetValue(containerId, out var occupancy)) return false;
        rows = occupancy.Rows;
        columns = occupancy.Columns;
        return true;
    }

    /// <summary>
    /// 检查容器的指定位置是否已被占用。
    /// </summary>
    /// <param name="containerId">容器ID。</param>
    /// <param name="row">行索引。</param>
    /// <param name="column">列索引。</param>
    /// <returns>是否已被占用。</returns>
    public bool IsCellOccupied(string containerId, int row, int column)
    {
        if (!occupancyByContainerId.TryGetValue(containerId, out var occupancy)) return false;
        return occupancy.IsCellOccupied(row, column);
    }

    /// <summary>查询指定格子是否尚未解锁。UI、拖放、自动整理共用这一份容量状态。</summary>
    public bool IsCellLocked(string containerId, int row, int column)
    {
        return occupancyByContainerId.TryGetValue(containerId, out var occupancy) && occupancy.IsCellLocked(row, column);
    }

    /// <summary>获取容器当前已解锁格数；找不到容器时返回 0。</summary>
    public int GetUnlockedCellCount(string containerId)
    {
        return unlockedCellCountByContainerId.TryGetValue(containerId, out var count) ? count : 0;
    }

    /// <summary>
    /// 解锁接下来的若干格。基地升级、任务、材料消费等条件系统只需在条件满足后调用此入口。
    /// </summary>
    public bool TryUnlockNextCells(string containerId, int cellCount, out int unlockedCellCount)
    {
        unlockedCellCount = GetUnlockedCellCount(containerId);
        if (cellCount <= 0 || !occupancyByContainerId.TryGetValue(containerId, out var occupancy)) return false;
        var total = occupancy.Rows * occupancy.Columns;
        if (unlockedCellCount >= total) return false;
        unlockedCellCount = Mathf.Min(total, unlockedCellCount + cellCount);
        unlockedCellCountByContainerId[containerId] = unlockedCellCount;
        occupancy.SetUnlockedCellCount(unlockedCellCount);
        return true;
    }

    /// <summary>
    /// 尝试获取容器指定位置的物品实例ID。
    /// </summary>
    /// <param name="containerId">容器ID。</param>
    /// <param name="row">行索引。</param>
    /// <param name="column">列索引。</param>
    /// <param name="instanceId">物品实例ID。</param>
    /// <returns>是否成功获取物品实例ID。</returns>
    public bool TryGetCellOccupantId(string containerId, int row, int column, out string instanceId)
    {
        instanceId = null;
        if (!occupancyByContainerId.TryGetValue(containerId, out var occupancy)) return false;
        instanceId = occupancy.GetCellOccupantId(row, column);
        return !string.IsNullOrWhiteSpace(instanceId);
    }

    /// <summary>
    /// 尝试获取物品实例。
    /// </summary>
    /// <param name="instanceId">物品实例ID。</param>
    /// <param name="instance">物品实例。</param>
    /// <returns>是否成功获取物品实例。</returns>
    public bool TryGetItemInstance(string instanceId, out GridItemInstance instance)
    {
        return itemInstanceById.TryGetValue(instanceId, out instance);
    }

    /// <summary>
    /// 尝试获取物品定义。
    /// </summary>
    /// <param name="itemId">物品ID。</param>
    /// <param name="definition">物品定义。</param>
    /// <returns>是否成功获取物品定义。</returns>
    public bool TryGetItemDefinition(string itemId, out GridItemDefinition definition)
    {
        return itemDefinitionById.TryGetValue(itemId, out definition);
    }

    /// <summary>判断指定实例是否位于玩家拥有的背包或口袋中。</summary>
    public bool IsPlayerOwnedInstance(string instanceId)
    {
        return itemInstanceById.TryGetValue(instanceId, out var instance) &&
               IsPlayerContainer(instance.containerId);
    }

    /// <summary>
    /// 从指定物品实例中精确扣除数量。用于“使用选中物品”，避免错误扣到同类的另一组堆叠。
    /// </summary>
    public bool TryConsumeFromInstance(string instanceId, int amount)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || amount <= 0) return false;
        if (!itemInstanceById.TryGetValue(instanceId, out var instance)) return false;
        if (!IsPlayerContainer(instance.containerId) || instance.stackCount < amount) return false;

        instance.stackCount -= amount;
        if (instance.stackCount <= 0)
        {
            RemoveInstanceInternal(instanceId);
            return true;
        }

        itemInstanceById[instanceId] = instance;
        return true;
    }

    /// <summary>
    /// 检查物品实例是否可以在指定容器的指定位置放置。
    /// </summary>
    /// <param name="instanceId">物品实例ID。</param>
    /// <param name="containerId">容器ID。</param>
    /// <param name="originRow">行索引。</param>
    /// <param name="originColumn">列索引。</param>
    /// <param name="rotation">物品旋转。</param>
    /// <returns>是否可以放置。</returns>
    public bool CanPlace(string instanceId, string containerId, int originRow, int originColumn, GridItemRotation rotation)
    {
        if (!itemInstanceById.TryGetValue(instanceId, out var instance)) return false;
        if (!itemDefinitionById.TryGetValue(instance.itemId, out var itemDefinition)) return false;
        if (!containerDefinitionById.TryGetValue(containerId, out var containerDefinition)) return false;
        if (!occupancyByContainerId.TryGetValue(containerId, out var occupancy)) return false;
        if (!CanContainerAcceptItem(containerDefinition, itemDefinition)) return false;
        return GridPlacementSolver.CanPlace(occupancy, originRow, originColumn, itemDefinition, rotation, containerId == instance.containerId ? instanceId : null);
    }

    /// <summary>
    /// 检查物品定义是否可以在指定容器的指定位置放置。
    /// </summary>
    /// <param name="itemId">物品ID。</param>
    /// <param name="containerId">容器ID。</param>
    /// <param name="originRow">行索引。</param>
    /// <param name="originColumn">列索引。</param>
    /// <param name="rotation">物品旋转。</param>
    /// <param name="stackCount">物品数量。</param>
    /// <returns>是否可以放置。</returns>
    public bool CanSpawn(string itemId, string containerId, int originRow, int originColumn, GridItemRotation rotation, int stackCount = 1)
    {
        if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(containerId)) return false;
        if (!itemDefinitionById.TryGetValue(itemId, out var itemDefinition)) return false;
        if (stackCount <= 0) return false;
        if (itemDefinition.maxStack > 0 && stackCount > itemDefinition.maxStack) return false;
        if (!containerDefinitionById.TryGetValue(containerId, out var containerDefinition)) return false;
        if (!occupancyByContainerId.TryGetValue(containerId, out var occupancy)) return false;
        if (!CanContainerAcceptItem(containerDefinition, itemDefinition)) return false;
        return GridPlacementSolver.CanPlace(occupancy, originRow, originColumn, itemDefinition, rotation);
    }

    /// <summary>
    /// 尝试移除物品实例。
    /// </summary>
    /// <param name="instanceId">物品实例ID。</param>
    /// <returns>是否成功移除物品实例。</returns>
    public bool TryRemoveInstance(string instanceId)
    {
        if (!itemInstanceById.ContainsKey(instanceId)) return false;
        RemoveInstanceInternal(instanceId);
        return true;
    }

    /// <summary>
    /// 尝试将物品实例拆分成多个物品叠。
    /// </summary>
    /// <param name="instanceId">物品实例ID。</param>
    /// <param name="splitCount">拆分数量。</param>
    /// <param name="newInstanceId">新物品实例ID。</param>
    /// <returns>是否成功拆分物品实例。</returns>
    public bool TrySplitStack(string instanceId, int splitCount, out string newInstanceId)
    {
        newInstanceId = null;
        if (!itemInstanceById.TryGetValue(instanceId, out var source)) return false;
        if (!itemDefinitionById.TryGetValue(source.itemId, out var definition)) return false;
        if (splitCount <= 0 || splitCount >= source.stackCount) return false;
        if (definition.maxStack > 0 && splitCount > definition.maxStack) return false;

        source.stackCount -= splitCount;
        itemInstanceById[instanceId] = source;

        if (!TryCreateItemInstance(source.itemId, splitCount, out newInstanceId))
        {
            source.stackCount += splitCount;
            itemInstanceById[instanceId] = source;
            return false;
        }

        if (HasPlacement(source))
        {
            if (TryQuickTransfer(newInstanceId)) return true;
            RemoveInstanceInternal(newInstanceId);
            source.stackCount += splitCount;
            itemInstanceById[instanceId] = source;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 尝试将物品实例合并为一个物品叠。
    /// </summary>
    /// <param name="sourceInstanceId">源物品实例ID。</param>
    /// <param name="targetInstanceId">目标物品实例ID。</param>
    /// <returns>是否成功合并物品实例。</returns>
    public bool TryMergeStacks(string sourceInstanceId, string targetInstanceId)
    {
        if (sourceInstanceId == targetInstanceId) return false;
        if (!itemInstanceById.TryGetValue(sourceInstanceId, out var source)) return false;
        if (!itemInstanceById.TryGetValue(targetInstanceId, out var target)) return false;
        if (source.itemId != target.itemId) return false;
        if (!itemDefinitionById.TryGetValue(source.itemId, out var definition)) return false;
        var stackLimit = definition.maxStack > 0 ? definition.maxStack : int.MaxValue;
        if (target.stackCount >= stackLimit) return false;

        var canMove = Mathf.Min(stackLimit - target.stackCount, source.stackCount);
        if (canMove <= 0) return false;

        target.stackCount += canMove;
        source.stackCount -= canMove;
        itemInstanceById[targetInstanceId] = target;

        if (source.stackCount <= 0)
        {
            RemoveInstanceInternal(sourceInstanceId);
            return true;
        }

        itemInstanceById[sourceInstanceId] = source;
        return true;
    }

    private bool HasPlacement(GridItemInstance instance)
    {
        return !string.IsNullOrWhiteSpace(instance.containerId) && instance.originRow >= 0 && instance.originColumn >= 0;
    }
    /// <summary>
    /// 恢复物品实例的旧位置，放置失败时回滚到旧位置。
    /// </summary>
    /// <param name="instance">物品实例。</param>
    /// <param name="oldContainerId">旧容器ID。</param>
    /// <param name="oldRow">旧行索引。</param>
    /// <param name="oldColumn">旧列索引。</param>
    /// <param name="oldRotation">旧旋转。</param>
    private void RestoreOldPlacement(GridItemInstance instance, string oldContainerId, int oldRow, int oldColumn, GridItemRotation oldRotation)
    {
        if (string.IsNullOrWhiteSpace(oldContainerId)) return;
        if (!occupancyByContainerId.TryGetValue(oldContainerId, out var oldOccupancy)) return;
        if (!itemDefinitionById.TryGetValue(instance.itemId, out var itemDefinition)) return;
        if (!GridPlacementSolver.TryGetFootprint(itemDefinition, oldRotation, out var width, out var height)) return;

        oldOccupancy.TryFillArea(oldRow, oldColumn, width, height, instance.instanceId);
    }

    /// <summary>
    /// 更新物品实例所属容器的成员关系。
    /// </summary>
    /// <param name="instanceId">物品实例ID。</param>
    /// <param name="oldContainerId">旧容器ID。</param>
    /// <param name="newContainerId">新容器ID。</param>
    private void UpdateContainerMembership(string instanceId, string oldContainerId, string newContainerId)
    {
        if (!string.IsNullOrWhiteSpace(oldContainerId) && containerItemInstanceIds.TryGetValue(oldContainerId, out var oldList))
        {
            oldList.Remove(instanceId);
        }

        if (!string.IsNullOrWhiteSpace(newContainerId) && containerItemInstanceIds.TryGetValue(newContainerId, out var newList))
        {
            if (!newList.Contains(instanceId)) newList.Add(instanceId);
        }
    }

    /// <summary>
    /// 检查容器是否允许放入对应物品。
    /// </summary>
    /// <param name="containerDefinition">容器定义。</param>
    /// <param name="itemDefinition">物品定义。</param>
    /// <returns>是否允许放入物品。</returns>
    private bool CanContainerAcceptItem(GridContainerDefinition containerDefinition, GridItemDefinition itemDefinition)
    {
        if (containerDefinition.allowedItemTags == null || containerDefinition.allowedItemTags.Count == 0) return true;
        if (itemDefinition.itemTags == null || itemDefinition.itemTags.Count == 0) return false;

        var matched = false;
        for (var i = 0; i < itemDefinition.itemTags.Count; i++)
        {
            if (!containerDefinition.allowedItemTags.Contains(itemDefinition.itemTags[i])) continue;
            matched = true;
            break;
        }

        if (!matched) return false;
        if (containerDefinition.allowNestedContainer) return true;
        return !itemDefinition.itemTags.Contains("container");
    }
    /// <summary>
    /// 构建容器优先级列表，包含当前容器并按优先级排序。
    /// </summary>
    /// <param name="currentContainerId">当前容器ID。</param>
    /// <returns>容器优先级列表。</returns>
    private List<string> BuildContainerPriority(string currentContainerId)
    {
        var result = BuildPlayerContainerPriority();

        if (!string.IsNullOrWhiteSpace(currentContainerId) && IsPlayerContainer(currentContainerId))
        {
            result.Remove(currentContainerId);
            result.Add(currentContainerId);
        }

        return result;
    }

    /// <summary>
    /// 构建玩家容器优先级列表，按 quickTransferPriorityContainerIds 中的顺序排列，若未在列表中则按注册顺序排列。
    /// </summary>
    /// <returns></returns>
    private List<string> BuildPlayerContainerPriority()
    {
        var result = new List<string>();
        for (var i = 0; i < quickTransferPriorityContainerIds.Count; i++)
        {
            var containerId = quickTransferPriorityContainerIds[i];
            if (string.IsNullOrWhiteSpace(containerId) || !IsPlayerContainer(containerId)) continue;
            if (!result.Contains(containerId)) result.Add(containerId);
        }

        foreach (var pair in containerOwnerById)
        {
            if (pair.Value != GridContainerOwnerType.Player || result.Contains(pair.Key)) continue;
            result.Add(pair.Key);
        }

        return result;
    }

    /// <summary>
    /// 检查指定容器是否属于玩家。
    /// </summary>
    /// <param name="containerId">容器ID。</param>
    /// <returns>是否属于玩家。</returns>
    private bool IsPlayerContainer(string containerId)
    {
        return !string.IsNullOrWhiteSpace(containerId) &&
               containerOwnerById.TryGetValue(containerId, out var ownerType) &&
               ownerType == GridContainerOwnerType.Player;
    }

    /// <summary>
    /// 尝试将物品实例放置到指定容器的第一个可用位置。
    /// </summary>
    /// <param name="instanceId"></param>
    /// <param name="containerId"></param>
    /// <returns></returns>
    private bool TryPlaceFirstFit(string instanceId, string containerId)
    {
        if (!itemInstanceById.TryGetValue(instanceId, out var instance)) return false;
        if (!itemDefinitionById.TryGetValue(instance.itemId, out var definition)) return false;
        if (!containerDefinitionById.TryGetValue(containerId, out var containerDefinition)) return false;
        if (!occupancyByContainerId.TryGetValue(containerId, out var occupancy)) return false;
        if (!CanContainerAcceptItem(containerDefinition, definition)) return false;
        var ignoreId = instance.containerId == containerId ? instanceId : null;
        if (!GridPlacementSolver.FindFirstFit(occupancy, definition, out var placement, true, ignoreId)) return false;
        return TryPlace(instanceId, containerId, placement.row, placement.column, placement.rotation);
    }

    /// <summary>
    /// 尝试将指定数量的物品添加到指定的容器列表中，优先合并已有堆叠，若无法合并则创建新堆叠。
    /// </summary>
    /// <param name="itemId"></param>
    /// <param name="amount"></param>
    /// <param name="targetContainerIds"></param>
    /// <returns></returns>
    private bool TryAddItemToContainers(string itemId, int amount, List<string> targetContainerIds)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0 || targetContainerIds == null || targetContainerIds.Count == 0) return false;
        if (!itemDefinitionById.TryGetValue(itemId, out var itemDefinition)) return false;

        var validTargets = targetContainerIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && containerDefinitionById.ContainsKey(id))
            .Distinct()
            .ToList();
        if (validTargets.Count == 0) return false;

        var pending = amount;
        var stackLimit = itemDefinition.maxStack > 0 ? itemDefinition.maxStack : int.MaxValue;
        var snapshots = new List<StackCountSnapshot>();
        var createdInstanceIds = new List<string>();

        var mergeTargets = itemInstanceById.Values
            .Where(instance => instance.itemId == itemId && validTargets.Contains(instance.containerId))
            .Where(instance => instance.stackCount < stackLimit)
            .OrderBy(instance => validTargets.IndexOf(instance.containerId))
            .ThenByDescending(instance => instance.stackCount)
            .ToList();

        for (var i = 0; i < mergeTargets.Count && pending > 0; i++)
        {
            var instance = mergeTargets[i];
            var add = Mathf.Min(pending, stackLimit - instance.stackCount);
            if (add <= 0) continue;
            snapshots.Add(new StackCountSnapshot { instanceId = instance.instanceId, stackCount = instance.stackCount });
            instance.stackCount += add;
            itemInstanceById[instance.instanceId] = instance;
            pending -= add;
        }

        while (pending > 0)
        {
            var createCount = stackLimit == int.MaxValue ? pending : Mathf.Min(stackLimit, pending);
            if (!TryCreateItemInstance(itemId, createCount, out var instanceId))
            {
                RollbackAddOperation(snapshots, createdInstanceIds);
                return false;
            }

            var placed = false;
            for (var i = 0; i < validTargets.Count; i++)
            {
                if (!TryPlaceFirstFit(instanceId, validTargets[i])) continue;
                placed = true;
                break;
            }

            if (!placed)
            {
                RemoveInstanceInternal(instanceId);
                RollbackAddOperation(snapshots, createdInstanceIds);
                return false;
            }

            createdInstanceIds.Add(instanceId);
            pending -= createCount;
        }

        return true;
    }

    /// <summary>
    /// 回滚添加物品操作，将已修改的堆叠数量恢复为快照中的值，并移除新创建的物品实例。
    /// </summary>
    /// <param name="snapshots"></param>
    /// <param name="createdInstanceIds"></param>
    private void RollbackAddOperation(List<StackCountSnapshot> snapshots, List<string> createdInstanceIds)
    {
        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            if (!itemInstanceById.TryGetValue(snapshot.instanceId, out var instance)) continue;
            instance.stackCount = snapshot.stackCount;
            itemInstanceById[snapshot.instanceId] = instance;
        }

        RollbackCreatedInstances(createdInstanceIds);
    }

    /// <summary>
    /// 尝试从 JSON 文件加载物品定义。正式流程使用 YooAsset；编辑器直开场景时读取源配置。
    /// </summary>
    /// <param name="replaceExisting">是否替换已存在的物品定义</param>
    /// <returns></returns>
    public bool TryLoadItemDefinitionsFromJson(bool replaceExisting)
    {
        var json = GameContentService.ItemDefinitionsJson;

        #if UNITY_EDITOR
        // 允许直接打开 RaidScene 调试。
        // 正常启动必须从 BootScene 进入并使用 YooAsset 配置。
        if (string.IsNullOrWhiteSpace(json))
        {
            var source = itemDefinitionsJson != null
                ? itemDefinitionsJson
                : UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(
                    "Assets/GameRes/Config/item_definitions.json");

            // 兼容尚未迁移到 GameRes 的旧工程布局。
            if (source == null)
                source = Resources.Load<TextAsset>("Data/item_definitions");

            if (source != null)
                json = source.text;
        }
        #endif

        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError("无法读取物品定义 JSON");
            return false;
        }

        var table = JsonUtility.FromJson<ItemDefinitionJsonTable>(json);
        if (table?.items == null || table.items.Count == 0) return false;
        if (replaceExisting) itemDefinitions.Clear();
        var knownIds = new HashSet<string>(itemDefinitions.Select(item => item.itemId));
        var profileById = BuildUseProfileMap(table.useProfiles);
        var assignedProfileByItemId = BuildUseProfileAssignmentMap(table.itemUseProfileAssignments);
        for (var i = 0; i < table.items.Count; i++)
        {
            var item = table.items[i];
            if (string.IsNullOrWhiteSpace(item.itemId) || item.width <= 0 || item.height <= 0) continue;
            item = ResolveItemUseProfile(item, profileById, assignedProfileByItemId);
            if (knownIds.Add(item.itemId)) itemDefinitions.Add(item);
        }

        return true;
    }

    /// <summary>建立使用档案索引，忽略空 ID 和重复项。</summary>
    private static Dictionary<string, GridItemUseProfile> BuildUseProfileMap(List<GridItemUseProfile> profiles)
    {
        var result = new Dictionary<string, GridItemUseProfile>();
        if (profiles == null) return result;
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            if (string.IsNullOrWhiteSpace(profile.profileId) || result.ContainsKey(profile.profileId)) continue;
            result.Add(profile.profileId, profile);
        }
        return result;
    }

    /// <summary>建立物品到使用档案的分配索引。</summary>
    private static Dictionary<string, string> BuildUseProfileAssignmentMap(List<GridItemUseProfileAssignment> assignments)
    {
        var result = new Dictionary<string, string>();
        if (assignments == null) return result;
        for (var i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            if (string.IsNullOrWhiteSpace(assignment.itemId) ||
                string.IsNullOrWhiteSpace(assignment.profileId)) continue;
            result[assignment.itemId] = assignment.profileId;
        }
        return result;
    }

    /// <summary>
    /// 将通用档案合并进物品定义。物品自身显式填写的 Use Effects 和 Use Duration 优先。
    /// </summary>
    private static GridItemDefinition ResolveItemUseProfile(
        GridItemDefinition item,
        IReadOnlyDictionary<string, GridItemUseProfile> profileById,
        IReadOnlyDictionary<string, string> assignedProfileByItemId)
    {
        var profileId = item.useProfile;
        if (string.IsNullOrWhiteSpace(profileId))
            assignedProfileByItemId.TryGetValue(item.itemId, out profileId);
        if (string.IsNullOrWhiteSpace(profileId) || !profileById.TryGetValue(profileId, out var profile))
            return item;

        item.useProfile = profileId;
        item.usable = true;
        if (item.useDuration <= 0f) item.useDuration = Mathf.Max(0f, profile.useDuration);
        if (item.useEffects == null || item.useEffects.Count == 0)
            item.useEffects = profile.useEffects != null
                ? new List<GridItemUseEffect>(profile.useEffects)
                : new List<GridItemUseEffect>();

        item.itemTags ??= new List<string>();
        if (profile.itemTagsToAdd != null)
        {
            for (var i = 0; i < profile.itemTagsToAdd.Count; i++)
            {
                var tag = profile.itemTagsToAdd[i];
                if (!string.IsNullOrWhiteSpace(tag) && !item.itemTags.Contains(tag)) item.itemTags.Add(tag);
            }
        }
        return item;
    }
    /// <summary>
    /// 计算物品定义的占用面积。
    /// </summary>
    /// <param name="definition">物品定义。</param>
    /// <returns>物品占用面积。</returns>
    private int GetArea(GridItemDefinition definition)
    {
        return Mathf.Max(1, definition.width) * Mathf.Max(1, definition.height);
    }

    /// <summary>把 Inspector 中的 0（兼容旧数据）解释为全解锁，并限制到有效范围。</summary>
    private static int NormalizeUnlockedCellCount(int configuredCount, int rows, int columns)
    {
        var total = Mathf.Max(0, rows) * Mathf.Max(0, columns);
        return configuredCount <= 0 ? total : Mathf.Clamp(configuredCount, 1, total);
    }

    private int GetItemTypeSortKey(string containerId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(containerId) || string.IsNullOrWhiteSpace(itemId)) return int.MaxValue;
        if (!occupancyByContainerId.TryGetValue(containerId, out var occupancy)) return int.MaxValue;

        var best = int.MaxValue;
        foreach (var instance in itemInstanceById.Values)
        {
            if (instance.itemId != itemId) continue;
            if (instance.containerId != containerId) continue;
            if (instance.originRow < 0 || instance.originColumn < 0) continue;
            var score = instance.originRow * Mathf.Max(1, occupancy.Columns) + instance.originColumn;
            if (score < best) best = score;
        }

        return best;
    }
    /// <summary>
    /// 从物品实例字典中移除指定实例ID的物品实例。
    /// </summary>
    /// <param name="instanceId">物品实例ID。</param>
    private void RemoveInstanceInternal(string instanceId)
    {
        if (!itemInstanceById.TryGetValue(instanceId, out var instance)) return;

        if (HasPlacement(instance) && occupancyByContainerId.TryGetValue(instance.containerId, out var occupancy))
        {
            occupancy.ClearAreaByInstance(instanceId);
        }

        if (!string.IsNullOrWhiteSpace(instance.containerId) && containerItemInstanceIds.TryGetValue(instance.containerId, out var list))
        {
            list.Remove(instanceId);
        }

        itemInstanceById.Remove(instanceId);
    }
    /// <summary>
    /// 回滚创建的物品实例，将它们从占用网格中移除。
    /// </summary>
    /// <param name="instanceIds">要回滚的物品实例ID列表。</param>
    private void RollbackCreatedInstances(List<string> instanceIds)
    {
        for (var i = 0; i < instanceIds.Count; i++)
        {
            RemoveInstanceInternal(instanceIds[i]);
        }
    }
}
