using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
public class FloorVisibilityController : MonoBehaviour
{
    public enum VisibilityMode
    {
        IndoorFocus,
        OutdoorShowAll
    }

    [Header("Target")]
    [Tooltip("当前楼层可见性跟随的目标。通常拖玩家根节点上的 FloorSortingState。为空时可自动查找。")]
    [SerializeField] private FloorSortingState targetFloorState;
    [Tooltip("为空时是否自动查找带 PlayerCombatController2D 的对象，并读取其 FloorSortingState。")]
    [SerializeField] private bool autoFindPlayer = true;

    [Header("Floor Root")]
    [Tooltip("楼层根节点。通常直接拖 Grid；为空时使用当前对象。")]
    [SerializeField] private Transform floorsRoot;
    [Tooltip("楼层根节点命名前缀。默认识别 Floor_0、Floor_1、Floor_2。")]
    [SerializeField] private string floorRootPrefix = "Floor_";

    [Header("Visibility Rule")]
    [Tooltip("当前建筑显示模式。IndoorFocus 表示按当前楼层隐藏更高楼层；OutdoorShowAll 表示室外整楼显示。")]
    [SerializeField] private VisibilityMode visibilityMode = VisibilityMode.IndoorFocus;
    [Tooltip("为 true 时：隐藏所有高于当前楼层的楼层显示。")]
    [SerializeField] private bool hideFloorsAboveCurrent = true;
    [Tooltip("为 true 时：当前楼层会额外隐藏指定名称的构件组。当前建议用于 RoofOrCover。")]
    [SerializeField] private bool hideCurrentFloorSpecialGroups = true;
    [Tooltip("当前楼层需要隐藏的构件组名称。默认建议只填 RoofOrCover。")]
    [SerializeField] private string[] hiddenGroupNamesOnCurrentFloor = { "RoofOrCover" };
    [Tooltip("显示/隐藏渐变时长（秒）。值越大，楼层切换越柔和。")]
    [SerializeField] private float fadeDurationSeconds = 0.2f;

    [Header("Dynamic Actors")]
    [Tooltip("为 true 时：场景中高于当前楼层的动态角色/敌人也会隐藏其渲染。")]
    [SerializeField] private bool hideDynamicActorsAboveCurrent = true;
    [Tooltip("动态角色缓存刷新间隔（秒）。场景里会生成/销毁僵尸时可适当调小。")]
    [SerializeField] private float actorRefreshIntervalSeconds = 1f;

    private sealed class FloorRendererEntry
    {
        public Renderer Renderer;
        public SpriteRenderer SpriteRenderer;
        public Tilemap Tilemap;
        public bool InitialEnabled;
        public float InitialAlpha;
        public float CurrentAlpha;
        public float TargetAlpha;
        public int FloorIndex;
        public string GroupName;
    }

    private sealed class ActorRendererEntry
    {
        public FloorSortingState FloorState;
        public Renderer Renderer;
        public SpriteRenderer SpriteRenderer;
        public Tilemap Tilemap;
        public bool InitialEnabled;
        public float InitialAlpha;
        public float CurrentAlpha;
        public float TargetAlpha;
    }

    private readonly List<FloorRendererEntry> floorRenderers = new();
    private readonly List<ActorRendererEntry> actorRenderers = new();
    private int appliedFloorIndex = -1;
    private VisibilityMode appliedVisibilityMode;
    private float nextActorRefreshTime;

    private void Awake()
    {
        RebuildFloorRendererCache();
        RefreshActorRendererCache();
        ApplyVisibilityTargets(force: true);
        StepVisibilityFade(999f);
    }

    private void OnEnable()
    {
        ApplyVisibilityTargets(force: true);
        StepVisibilityFade(999f);
    }

    private void LateUpdate()
    {
        if (autoFindPlayer && targetFloorState == null)
        {
            targetFloorState = FindPlayerFloorState();
        }

        if (hideDynamicActorsAboveCurrent && Time.unscaledTime >= nextActorRefreshTime)
        {
            RefreshActorRendererCache();
        }

        ApplyVisibilityTargets(force: false);
        StepVisibilityFade(Time.unscaledDeltaTime);
    }

    private void OnTransformChildrenChanged()
    {
        RebuildFloorRendererCache();
        ApplyVisibilityTargets(force: true);
        StepVisibilityFade(999f);
    }

    private void OnValidate()
    {
        actorRefreshIntervalSeconds = Mathf.Max(0.1f, actorRefreshIntervalSeconds);
        fadeDurationSeconds = Mathf.Max(0.01f, fadeDurationSeconds);
        if (!Application.isPlaying)
        {
            RebuildFloorRendererCache();
        }
    }

    [ContextMenu("Refresh Visibility Cache")]
    public void RefreshVisibilityCache()
    {
        RebuildFloorRendererCache();
        RefreshActorRendererCache();
        ApplyVisibilityTargets(force: true);
        StepVisibilityFade(999f);
    }

    public void SetVisibilityMode(VisibilityMode mode)
    {
        if (visibilityMode == mode) return;
        visibilityMode = mode;
        ApplyVisibilityTargets(force: true);
    }

    public void SetIndoorMode()
    {
        SetVisibilityMode(VisibilityMode.IndoorFocus);
    }

    public void SetOutdoorMode()
    {
        SetVisibilityMode(VisibilityMode.OutdoorShowAll);
    }

    private void ApplyVisibilityTargets(bool force)
    {
        var currentFloor = targetFloorState != null ? targetFloorState.VisibilityFloorIndex : 0;
        if (!force && currentFloor == appliedFloorIndex && visibilityMode == appliedVisibilityMode) return;

        appliedFloorIndex = currentFloor;
        appliedVisibilityMode = visibilityMode;
        var isOutdoor = visibilityMode == VisibilityMode.OutdoorShowAll;

        for (var i = 0; i < floorRenderers.Count; i++)
        {
            var entry = floorRenderers[i];
            if (entry.Renderer == null) continue;

            var shouldShow = entry.InitialEnabled;
            if (!isOutdoor && hideFloorsAboveCurrent && entry.FloorIndex > currentFloor)
            {
                shouldShow = false;
            }
            else if (!isOutdoor &&
                     hideCurrentFloorSpecialGroups &&
                     entry.FloorIndex == currentFloor &&
                     IsHiddenGroupOnCurrentFloor(entry.GroupName))
            {
                shouldShow = false;
            }

            SetEntryTarget(entry, shouldShow);
        }

        if (isOutdoor || !hideDynamicActorsAboveCurrent)
        {
            for (var i = 0; i < actorRenderers.Count; i++)
            {
                SetEntryTarget(actorRenderers[i], actorRenderers[i].InitialEnabled);
            }

            return;
        }

        for (var i = 0; i < actorRenderers.Count; i++)
        {
            var entry = actorRenderers[i];
            if (entry.Renderer == null || entry.FloorState == null) continue;
            if (targetFloorState != null && entry.FloorState == targetFloorState) continue;

            var shouldShow = entry.InitialEnabled;
            if (hideFloorsAboveCurrent && entry.FloorState.FloorIndex > currentFloor)
            {
                shouldShow = false;
            }

            SetEntryTarget(entry, shouldShow);
        }
    }

    private bool IsHiddenGroupOnCurrentFloor(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName) || hiddenGroupNamesOnCurrentFloor == null) return false;
        for (var i = 0; i < hiddenGroupNamesOnCurrentFloor.Length; i++)
        {
            if (string.Equals(hiddenGroupNamesOnCurrentFloor[i], groupName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void RebuildFloorRendererCache()
    {
        floorRenderers.Clear();

        var root = floorsRoot != null ? floorsRoot : transform;
        if (root == null) return;

        for (var i = 0; i < root.childCount; i++)
        {
            var floorRoot = root.GetChild(i);
            if (!TryGetFloorIndex(floorRoot.name, out var floorIndex)) continue;

            var renderers = floorRoot.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (renderer == null) continue;

                floorRenderers.Add(new FloorRendererEntry
                {
                    Renderer = renderer,
                    SpriteRenderer = renderer as SpriteRenderer,
                    Tilemap = renderer.GetComponent<Tilemap>(),
                    InitialEnabled = renderer.enabled,
                    InitialAlpha = GetInitialAlpha(renderer),
                    CurrentAlpha = renderer.enabled ? GetInitialAlpha(renderer) : 0f,
                    TargetAlpha = renderer.enabled ? GetInitialAlpha(renderer) : 0f,
                    FloorIndex = floorIndex,
                    GroupName = GetFloorGroupName(floorRoot, renderer.transform)
                });
            }
        }
    }

    private void RefreshActorRendererCache()
    {
        actorRenderers.Clear();
        nextActorRefreshTime = Time.unscaledTime + actorRefreshIntervalSeconds;

        if (!hideDynamicActorsAboveCurrent) return;

        var states = FindObjectsOfType<FloorSortingState>(true);
        for (var i = 0; i < states.Length; i++)
        {
            var state = states[i];
            if (state == null) continue;
            if (targetFloorState != null && state == targetFloorState) continue;
            if (IsUnderFloorsRoot(state.transform)) continue;

            var renderers = state.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (renderer == null) continue;

                actorRenderers.Add(new ActorRendererEntry
                {
                    FloorState = state,
                    Renderer = renderer,
                    SpriteRenderer = renderer as SpriteRenderer,
                    Tilemap = renderer.GetComponent<Tilemap>(),
                    InitialEnabled = renderer.enabled,
                    InitialAlpha = GetInitialAlpha(renderer),
                    CurrentAlpha = renderer.enabled ? GetInitialAlpha(renderer) : 0f,
                    TargetAlpha = renderer.enabled ? GetInitialAlpha(renderer) : 0f
                });
            }
        }
    }

    private bool IsUnderFloorsRoot(Transform target)
    {
        var root = floorsRoot != null ? floorsRoot : transform;
        if (root == null || target == null) return false;
        return target.IsChildOf(root);
    }

    private static string GetFloorGroupName(Transform floorRoot, Transform rendererTransform)
    {
        if (floorRoot == null || rendererTransform == null) return string.Empty;

        var current = rendererTransform;
        while (current != null && current.parent != null)
        {
            if (current.parent == floorRoot)
            {
                return current.name;
            }

            current = current.parent;
        }

        return rendererTransform.name;
    }

    private bool TryGetFloorIndex(string objectName, out int floorIndex)
    {
        floorIndex = -1;
        if (string.IsNullOrWhiteSpace(objectName)) return false;
        if (!objectName.StartsWith(floorRootPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        var suffix = objectName.Substring(floorRootPrefix.Length);
        return int.TryParse(suffix, out floorIndex);
    }

    private static FloorSortingState FindPlayerFloorState()
    {
        var player = FindObjectOfType<PlayerCombatController2D>();
        if (player == null) return null;
        return player.GetComponentInParent<FloorSortingState>() ?? player.GetComponent<FloorSortingState>();
    }

    private void StepVisibilityFade(float deltaTime)
    {
        var step = deltaTime <= 0f ? 1f : deltaTime / Mathf.Max(0.01f, fadeDurationSeconds);

        for (var i = 0; i < floorRenderers.Count; i++)
        {
            StepEntry(floorRenderers[i], step);
        }

        for (var i = 0; i < actorRenderers.Count; i++)
        {
            StepEntry(actorRenderers[i], step);
        }
    }

    private static float GetInitialAlpha(Renderer renderer)
    {
        if (renderer is SpriteRenderer spriteRenderer)
        {
            return spriteRenderer.color.a;
        }

        var tilemap = renderer != null ? renderer.GetComponent<Tilemap>() : null;
        if (tilemap != null)
        {
            return tilemap.color.a;
        }

        return renderer != null && renderer.enabled ? 1f : 0f;
    }

    private static void SetEntryTarget(FloorRendererEntry entry, bool shouldShow)
    {
        if (entry == null || entry.Renderer == null) return;
        entry.TargetAlpha = shouldShow ? entry.InitialAlpha : 0f;
        if (shouldShow && entry.InitialEnabled)
        {
            entry.Renderer.enabled = true;
        }
    }

    private static void SetEntryTarget(ActorRendererEntry entry, bool shouldShow)
    {
        if (entry == null || entry.Renderer == null) return;
        entry.TargetAlpha = shouldShow ? entry.InitialAlpha : 0f;
        if (shouldShow && entry.InitialEnabled)
        {
            entry.Renderer.enabled = true;
        }
    }

    private static void StepEntry(FloorRendererEntry entry, float step)
    {
        if (entry == null || entry.Renderer == null) return;
        entry.CurrentAlpha = Mathf.MoveTowards(entry.CurrentAlpha, entry.TargetAlpha, Mathf.Max(0.0001f, step) * Mathf.Max(0.0001f, entry.InitialAlpha));
        ApplyEntryAlpha(entry.Renderer, entry.SpriteRenderer, entry.Tilemap, entry.CurrentAlpha);
    }

    private static void StepEntry(ActorRendererEntry entry, float step)
    {
        if (entry == null || entry.Renderer == null) return;
        entry.CurrentAlpha = Mathf.MoveTowards(entry.CurrentAlpha, entry.TargetAlpha, Mathf.Max(0.0001f, step) * Mathf.Max(0.0001f, entry.InitialAlpha));
        ApplyEntryAlpha(entry.Renderer, entry.SpriteRenderer, entry.Tilemap, entry.CurrentAlpha);
    }

    private static void ApplyEntryAlpha(Renderer renderer, SpriteRenderer spriteRenderer, Tilemap tilemap, float alpha)
    {
        var clampedAlpha = Mathf.Clamp01(alpha);

        if (spriteRenderer != null)
        {
            var color = spriteRenderer.color;
            color.a = clampedAlpha;
            spriteRenderer.color = color;
            renderer.enabled = clampedAlpha > 0.001f;
            return;
        }

        if (tilemap != null)
        {
            var color = tilemap.color;
            color.a = clampedAlpha;
            tilemap.color = color;
            renderer.enabled = clampedAlpha > 0.001f;
            return;
        }

        renderer.enabled = clampedAlpha > 0.5f;
    }
}
