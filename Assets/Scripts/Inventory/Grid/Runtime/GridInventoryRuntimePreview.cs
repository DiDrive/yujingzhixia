using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class GridInventoryRuntimePreview : MonoBehaviour
{
    [PropertyTooltip("是否显示网格背包预览。")]
    [SerializeField] private bool visible = true;
    [PropertyTooltip("预览窗口位置。")]
    [SerializeField] private Vector2 panelPosition = new(580f, 20f);
    [PropertyTooltip("预览窗口尺寸。")]
    [SerializeField] private Vector2 panelSize = new(420f, 520f);
    [PropertyTooltip("快捷切换预览的按键。")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F2;
    [PropertyTooltip("网格背包服务引用。")]
    [SerializeField] private GridInventoryService gridInventoryService;
    [PropertyTooltip("预览容器ID。为空时自动选择第一个容器。")]
    [SerializeField] private string previewContainerId = "main_bag";
    [PropertyTooltip("调试投放用物品ID。")]
    [SerializeField] private List<string> demoItemIds = new() { "food_canned", "water_bottle", "bandage" };
    [PropertyTooltip("每个格子的像素尺寸。")]
    [SerializeField] private float cellSize = 30f;
    [PropertyTooltip("格子间距。")]
    [SerializeField] private float cellSpacing = 2f;

    private Rect windowRect;
    private bool isWindowRectInitialized;
    private const int WindowId = 295118;
    private string selectedItemIdForSpawn = "food_canned";
    private string selectedInstanceId;
    private Vector2 containerScroll;

    private void Awake()
    {
        if (gridInventoryService == null) gridInventoryService = FindObjectOfType<GridInventoryService>();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)) visible = !visible;
    }

    private void OnGUI()
    {
        if (!visible) return;
        if (gridInventoryService == null) return;

        EnsureWindowRect();
        windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "Grid Inventory Preview");
        panelPosition = new Vector2(windowRect.x, windowRect.y);
        panelSize = new Vector2(windowRect.width, windowRect.height);
    }

    private void EnsureWindowRect()
    {
        if (isWindowRectInitialized) return;
        windowRect = new Rect(panelPosition.x, panelPosition.y, panelSize.x, panelSize.y);
        isWindowRectInitialized = true;
    }

    private void DrawWindow(int windowId)
    {
        ResolvePreviewContainerId();

        GUILayout.BeginVertical();
        GUILayout.Label($"Container: {previewContainerId}");
        DrawContainerTabs();
        DrawSpawnToolbar();
        DrawSelectionToolbar();
        GUILayout.Space(6f);
        DrawGrid();
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 22f));
    }

    private void DrawContainerTabs()
    {
        containerScroll = GUILayout.BeginScrollView(containerScroll, GUILayout.Height(52f));
        GUILayout.BeginHorizontal();
        foreach (var containerId in gridInventoryService.ContainerIds)
        {
            var isCurrent = containerId == previewContainerId;
            var oldColor = GUI.backgroundColor;
            if (isCurrent) GUI.backgroundColor = new Color(0.55f, 0.9f, 0.75f, 1f);
            if (GUILayout.Button(containerId, GUILayout.Height(30f), GUILayout.MinWidth(90f)))
            {
                previewContainerId = containerId;
                selectedInstanceId = null;
            }
            GUI.backgroundColor = oldColor;
        }
        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();
    }

    private void DrawSpawnToolbar()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Spawn:");
        if (demoItemIds != null)
        {
            for (var i = 0; i < demoItemIds.Count; i++)
            {
                var itemId = demoItemIds[i];
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                var oldColor = GUI.backgroundColor;
                if (itemId == selectedItemIdForSpawn) GUI.backgroundColor = new Color(0.7f, 0.85f, 1f, 1f);
                if (GUILayout.Button(itemId, GUILayout.Height(24f)))
                {
                    selectedItemIdForSpawn = itemId;
                }
                GUI.backgroundColor = oldColor;
            }
        }
        GUILayout.EndHorizontal();
    }

    private void DrawSelectionToolbar()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Selected Item: {selectedItemIdForSpawn}");
        GUILayout.FlexibleSpace();
        if (!string.IsNullOrWhiteSpace(previewContainerId) && GUILayout.Button("Auto Sort", GUILayout.Height(24f), GUILayout.Width(90f)))
        {
            gridInventoryService.AutoSort(previewContainerId);
        }
        if (GUILayout.Button("Clear", GUILayout.Height(24f), GUILayout.Width(70f)))
        {
            selectedInstanceId = null;
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Selected Instance: {(string.IsNullOrWhiteSpace(selectedInstanceId) ? "None" : GetShortId(selectedInstanceId))}");
        if (!string.IsNullOrWhiteSpace(selectedInstanceId) && GUILayout.Button("Rotate", GUILayout.Height(24f), GUILayout.Width(90f)))
        {
            gridInventoryService.TryRotate(selectedInstanceId);
        }
        GUILayout.EndHorizontal();
    }

    private void ResolvePreviewContainerId()
    {
        if (!string.IsNullOrWhiteSpace(previewContainerId)) return;
        foreach (var containerId in gridInventoryService.ContainerIds)
        {
            previewContainerId = containerId;
            break;
        }
    }

    private void DrawGrid()
    {
        if (string.IsNullOrWhiteSpace(previewContainerId))
        {
            GUILayout.Label("No container available");
            return;
        }

        if (!gridInventoryService.TryGetContainerSize(previewContainerId, out var rows, out var columns))
        {
            GUILayout.Label("Container not found");
            return;
        }

        for (var row = 0; row < rows; row++)
        {
            GUILayout.BeginHorizontal();
            for (var column = 0; column < columns; column++)
            {
                var hasOccupant = gridInventoryService.TryGetCellOccupantId(previewContainerId, row, column, out var occupantId);
                var isSelected = hasOccupant && occupantId == selectedInstanceId;
                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = isSelected ? new Color(0.95f, 0.86f, 0.45f, 1f) : (hasOccupant ? new Color(0.58f, 0.78f, 0.96f, 1f) : Color.white);
                var label = GetCellLabel(hasOccupant ? occupantId : null);
                if (GUILayout.Button(label, GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
                {
                    OnCellClicked(row, column, hasOccupant ? occupantId : null);
                }
                GUI.backgroundColor = oldColor;
                GUILayout.Space(cellSpacing);
            }
            GUILayout.EndHorizontal();
        }
    }

    private string GetCellLabel(string occupantId)
    {
        if (string.IsNullOrWhiteSpace(occupantId)) return string.Empty;
        if (!gridInventoryService.TryGetItemInstance(occupantId, out var instance)) return "X";
        return string.IsNullOrWhiteSpace(instance.itemId) ? "?" : instance.itemId[..1].ToUpperInvariant();
    }

    private string GetShortId(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return "None";
        return instanceId.Length <= 8 ? instanceId : instanceId.Substring(0, 8);
    }

    private void OnCellClicked(int row, int column, string occupantId)
    {
        if (!string.IsNullOrWhiteSpace(occupantId))
        {
            selectedInstanceId = occupantId;
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedInstanceId))
        {
            if (gridInventoryService.TryPlace(selectedInstanceId, previewContainerId, row, column, GridItemRotation.Deg0))
            {
                return;
            }

            if (gridInventoryService.TryGetItemInstance(selectedInstanceId, out var selectedInstance))
            {
                gridInventoryService.TryPlace(selectedInstanceId, previewContainerId, row, column, selectedInstance.rotation);
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedItemIdForSpawn)) return;
        gridInventoryService.TrySpawnToCell(selectedItemIdForSpawn, previewContainerId, row, column);
    }
}
