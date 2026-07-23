using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 世界交互物的统一协议。搜索容器、露天物资等对象通过该接口被玩家发现和执行。
/// </summary>
public interface IWorldInteractable
{
    /// <summary>显示在交互提示中的动作文字。</summary>
    string InteractionPrompt { get; }
    /// <summary>完成交互需要持续按键的秒数；0 表示按下后立即执行。</summary>
    float InteractionHoldDuration { get; }
    /// <summary>交互目标在世界中的参考位置。</summary>
    Transform InteractionTransform { get; }
    /// <summary>判断指定玩家当前是否可以与该对象交互。</summary>
    bool CanInteract(PlayerInteractor2D interactor);
    /// <summary>执行搜索、拾取或打开等实际交互行为。</summary>
    void Interact(PlayerInteractor2D interactor);
}

/// <summary>
/// 维护当前场景中已启用的可交互物，避免瓦片地图的大量碰撞器干扰目标发现。
/// </summary>
public static class WorldInteractableRegistry2D
{
    public readonly struct Entry
    {
        public Entry(MonoBehaviour behaviour, IWorldInteractable interactable, Collider2D[] colliders)
        {
            Behaviour = behaviour;
            Interactable = interactable;
            Colliders = colliders;
        }

        /// <summary>用于生命周期和 Unity 空引用判断的组件实例。</summary>
        public MonoBehaviour Behaviour { get; }
        /// <summary>实际执行交互协议的对象。</summary>
        public IWorldInteractable Interactable { get; }
        /// <summary>只缓存交互物自身及子节点碰撞器，不包含 Tilemap 碰撞器。</summary>
        public Collider2D[] Colliders { get; }
    }

    private static readonly List<Entry> entries = new();
    public static IReadOnlyList<Entry> Entries => entries;

    /// <summary>登记一个已启用的世界交互物，并缓存它的交互范围碰撞器。</summary>
    public static void Register(MonoBehaviour behaviour)
    {
        if (behaviour == null || behaviour is not IWorldInteractable interactable) return;
        Unregister(behaviour);
        entries.Add(new Entry(behaviour, interactable, behaviour.GetComponentsInChildren<Collider2D>(true)));
    }

    /// <summary>移除已禁用、销毁或指定的世界交互物。</summary>
    public static void Unregister(MonoBehaviour behaviour)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].Behaviour == null || entries[i].Behaviour == behaviour) entries.RemoveAt(i);
        }
    }
}
