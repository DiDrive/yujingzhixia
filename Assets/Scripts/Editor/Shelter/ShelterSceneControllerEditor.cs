using UnityEditor;
using UnityEngine;

/// <summary>
/// 基地控制器的测试面板。正式解锁条件尚未确定前，用它验证锁格显示、拖放和整理行为。
/// </summary>
[CustomEditor(typeof(ShelterSceneController))]
public sealed class ShelterSceneControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var controller = target as ShelterSceneController;
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("格子解锁测试", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            Application.isPlaying
                ? "点击按钮会解锁对应容器的下一整行。停止运行后测试数据会恢复；以后材料、任务或基地等级只需调用同一个解锁接口。"
                : "请进入 Play Mode 后测试。该面板不会在编辑状态改写正式容量数据。",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(!Application.isPlaying || controller == null))
        {
            DrawStatus(controller, "shelter_stash", "基地仓库");
            if (GUILayout.Button(new GUIContent("测试：仓库解锁下一行", "按仓库列数解锁下一整行，当前是每次 16 格。")))
                controller.TestUnlockNextStashRow();

            EditorGUILayout.Space(4f);
            DrawStatus(controller, "main_bag", "主背包");
            if (GUILayout.Button(new GUIContent("测试：主背包解锁下一行", "按主背包列数解锁下一整行，当前是每次 12 格。")))
                controller.TestUnlockNextMainBagRow();
        }
    }

    private static void DrawStatus(ShelterSceneController controller, string containerId, string displayName)
    {
        if (controller != null && controller.TryGetContainerUnlockStatus(containerId, out var unlocked, out var total, out _))
            EditorGUILayout.LabelField(displayName, $"{unlocked} / {total} 格已解锁");
        else
            EditorGUILayout.LabelField(displayName, "等待运行时容器初始化");
    }
}
