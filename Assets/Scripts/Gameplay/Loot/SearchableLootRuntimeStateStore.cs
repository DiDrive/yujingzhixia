using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 搜索容器中单个剩余物品的可序列化状态。
/// 实例 ID 在场景重载后会变化，因此快照只保存可重建的物品数据和揭示状态。
/// </summary>
[Serializable]
public sealed class SearchableLootItemState
{
    public string itemId;
    public int stackCount;
    public GridItemRotation rotation;
    public int row;
    public int column;
    public bool revealed;

    public SearchableLootItemState Clone()
    {
        return new SearchableLootItemState
        {
            itemId = itemId,
            stackCount = stackCount,
            rotation = rotation,
            row = row,
            column = column,
            revealed = revealed
        };
    }
}

/// <summary>
/// 单个世界搜索容器的可序列化状态。
/// 后续正式存档系统可以直接把该结构写入存档槽。
/// </summary>
[Serializable]
public sealed class SearchableLootContainerState
{
    public string persistentId;
    public bool populated;
    public bool searched;
    public List<SearchableLootItemState> items = new();

    public SearchableLootContainerState Clone()
    {
        var clone = new SearchableLootContainerState
        {
            persistentId = persistentId,
            populated = populated,
            searched = searched
        };
        if (items == null) return clone;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] != null) clone.items.Add(items[i].Clone());
        }
        return clone;
    }
}

/// <summary>
/// 当前游戏会话内的搜索容器状态仓库。
/// 不依赖场景对象、不写入 PlayerPrefs；场景重载时状态仍保留，新一局开始时应调用 ClearAll。
/// </summary>
public static class SearchableLootRuntimeStateStore
{
    private static readonly Dictionary<string, SearchableLootContainerState> StateById = new();

    // 进入一次新的游戏会话时清空；普通场景重载不会触发 SubsystemRegistration。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewGameSession()
    {
        ClearAll();
    }

    public static bool TryGet(string persistentId, out SearchableLootContainerState state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(persistentId) ||
            !StateById.TryGetValue(persistentId, out var stored))
        {
            return false;
        }

        state = stored.Clone();
        return true;
    }

    public static void Save(SearchableLootContainerState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.persistentId)) return;
        StateById[state.persistentId] = state.Clone();
    }

    public static void Remove(string persistentId)
    {
        if (!string.IsNullOrWhiteSpace(persistentId)) StateById.Remove(persistentId);
    }

    /// <summary>开始新一局或清空存档时调用，避免上一局容器状态带入下一局。</summary>
    public static void ClearAll()
    {
        StateById.Clear();
    }
}
