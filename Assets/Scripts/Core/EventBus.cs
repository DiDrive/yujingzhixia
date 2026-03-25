using System;
using System.Collections.Generic;

/// <summary>
/// 轻量级事件总线，用于系统间解耦通信。
/// </summary>
public static class EventBus
{
    private static readonly Dictionary<Type, Delegate> Handlers = new();

    /// <summary>
    /// 订阅指定类型事件。
    /// </summary>
    public static void Subscribe<T>(Action<T> handler)
    {
        var key = typeof(T);
        if (Handlers.TryGetValue(key, out var existing))
        {
            Handlers[key] = Delegate.Combine(existing, handler);
            return;
        }
        Handlers[key] = handler;
    }

    /// <summary>
    /// 取消订阅指定类型事件。
    /// </summary>
    public static void Unsubscribe<T>(Action<T> handler)
    {
        var key = typeof(T);
        if (!Handlers.TryGetValue(key, out var existing)) return;
        var updated = Delegate.Remove(existing, handler);
        if (updated == null) Handlers.Remove(key);
        else Handlers[key] = updated;
    }

    /// <summary>
    /// 发布事件到所有监听者。
    /// </summary>
    public static void Publish<T>(T evt)
    {
        var key = typeof(T);
        if (!Handlers.TryGetValue(key, out var existing)) return;
        if (existing is Action<T> action) action.Invoke(evt);
    }
}

/// <summary>
/// 游戏阶段切换事件。
/// </summary>
public readonly struct GamePhaseChangedEvent
{
    public readonly GamePhase From;
    public readonly GamePhase To;

    public GamePhaseChangedEvent(GamePhase from, GamePhase to)
    {
        From = from;
        To = to;
    }
}

/// <summary>
/// 生存系统逐帧Tick事件。
/// </summary>
public readonly struct SurvivalTickEvent
{
    public readonly float DeltaSeconds;

    public SurvivalTickEvent(float deltaSeconds)
    {
        DeltaSeconds = deltaSeconds;
    }
}
