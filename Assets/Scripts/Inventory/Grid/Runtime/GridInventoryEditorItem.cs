using UnityEngine;

[ExecuteAlways]
public sealed class GridInventoryEditorItem : MonoBehaviour
{
    [Tooltip("这个场景物品所属的容器 ID。通常由它所在的 InventoryUI_* 自动决定。")]
    [SerializeField] private string containerId = "main_bag";
    [Tooltip("物品定义 ID，必须和 GridInventoryService 的 Item Definitions 中的 Item Id 一致。")]
    [SerializeField] private string itemId;
    [Tooltip("该场景物品启动时携带的数量。")]
    [Min(1)]
    [SerializeField] private int stackCount = 1;
    [Tooltip("物品左上角所在行，从 0 开始。拖动物品节点后会自动更新。")]
    [Min(0)]
    [SerializeField] private int row;
    [Tooltip("物品左上角所在列，从 0 开始。拖动物品节点后会自动更新。")]
    [Min(0)]
    [SerializeField] private int column;
    [Tooltip("物品的初始旋转状态。")]
    [SerializeField] private GridItemRotation rotation;

    private string runtimeInstanceId;
    private bool isSynchronizing;

    public string ContainerId => containerId;
    public string ItemId => itemId;
    public int StackCount => Mathf.Max(1, stackCount);
    public int Row => Mathf.Max(0, row);
    public int Column => Mathf.Max(0, column);
    public GridItemRotation Rotation => rotation;
    public string RuntimeInstanceId => runtimeInstanceId;

    public void Configure(string targetContainerId, string targetItemId, int targetStackCount, int targetRow, int targetColumn, GridItemRotation targetRotation)
    {
        containerId = targetContainerId;
        itemId = targetItemId;
        stackCount = Mathf.Max(1, targetStackCount);
        row = Mathf.Max(0, targetRow);
        column = Mathf.Max(0, targetColumn);
        rotation = targetRotation;
    }

    public void SetRuntimeInstanceId(string instanceId)
    {
        runtimeInstanceId = instanceId;
    }

    public void SetGridPosition(int targetRow, int targetColumn)
    {
        row = Mathf.Max(0, targetRow);
        column = Mathf.Max(0, targetColumn);
    }

    private void OnValidate()
    {
        if (Application.isPlaying || isSynchronizing) return;
        var inventoryUi = GetComponentInParent<GridInventoryUnityUI>();
        if (inventoryUi == null) return;

        isSynchronizing = true;
        inventoryUi.SnapEditorItemToGrid(this);
        isSynchronizing = false;
    }
}
