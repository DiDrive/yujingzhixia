using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GridInventoryService))]
public class GridInventoryServiceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox("修改 Container Definitions 的 Rows / Columns 后，点击下面按钮同步场景里的 InventoryUI_main_bag / InventoryUI_* 层级和 Cell 数量。默认背包物品使用 ItemLayer 下的 Item_* 节点保存。", MessageType.Info);
        var syncContent = new GUIContent(
            "Sync Inventory UI Layout",
            "按当前 Container Definitions 同步编辑器里的背包 UI：更新主背包和口袋的 GridRoot、CellLayer、ItemLayer、GhostLayer，以及每个 Cell 节点数量；不会覆盖你已经摆放好的 Item_* 节点。");
        if (GUILayout.Button(syncContent))
        {
            var service = target as GridInventoryService;
            if (service == null) return;
            GridInventoryEditorLayoutBuilder.SyncContainerViews(service);
        }

        var resetItemsContent = new GUIContent(
            "Reset Default Editor Item Nodes",
            "清空主背包当前的 Item_* 节点，并重建四个默认物品：food_canned、water_bottle、bandage、cloth_raw。之后可直接在 ItemLayer 中拖动、复制、删除或修改这些节点。");
        if (GUILayout.Button(resetItemsContent))
        {
            GridInventoryEditorLayoutBuilder.ResetDefaultEditorItemNodesMenu();
        }
    }
}
