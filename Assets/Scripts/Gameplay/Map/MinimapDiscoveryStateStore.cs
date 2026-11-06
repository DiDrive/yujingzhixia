using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>跨场景保存已发现的小地图稳定 ID，并向正式磁盘存档提供快照。</summary>
public static class MinimapDiscoveryStateStore
{
    private static readonly HashSet<string> DiscoveredIds = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForNewSession()
    {
        ClearAll();
    }

    public static void MarkDiscovered(string markerId)
    {
        if (!string.IsNullOrWhiteSpace(markerId)) DiscoveredIds.Add(markerId);
    }

    public static bool Contains(string markerId)
    {
        return !string.IsNullOrWhiteSpace(markerId) && DiscoveredIds.Contains(markerId);
    }

    public static void Unmark(string markerId)
    {
        if (!string.IsNullOrWhiteSpace(markerId)) DiscoveredIds.Remove(markerId);
    }

    public static List<string> CaptureAll()
    {
        return DiscoveredIds.OrderBy(id => id).ToList();
    }

    public static void RestoreAll(IEnumerable<string> markerIds)
    {
        DiscoveredIds.Clear();
        if (markerIds == null) return;
        foreach (var markerId in markerIds) MarkDiscovered(markerId);
    }

    public static void ClearAll()
    {
        DiscoveredIds.Clear();
    }
}
