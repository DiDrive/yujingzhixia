#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把当前已打开的旧场景对象迁移到统一 JSON 掉落表。
/// 该迁移只在编辑器中工作，结果是普通场景组件；运行时不会创建 UI 或掉落服务。
/// </summary>
[InitializeOnLoad]
public static class LootSystemUnificationMigration
{
    private const string StartScenePath = "Assets/Scenes/RaidScene.unity";

    static LootSystemUnificationMigration()
    {
        EditorApplication.delayCall += MigrateActiveStartScene;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == StartScenePath)
            EditorApplication.delayCall += MigrateActiveStartScene;
    }

    private static void MigrateActiveStartScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != StartScenePath) return;

        var database = Object.FindObjectOfType<LootTableDatabase>(true);
        if (database == null)
        {
            var host = GameObject.Find("Gameplay_LootSpawner") ?? GameObject.Find("Core_LootTableDatabase");
            if (host == null)
            {
                host = new GameObject("Core_LootTableDatabase");
                Undo.RegisterCreatedObjectUndo(host, "Create Loot Table Database");
            }
            else
            {
                Undo.RecordObject(host, "Migrate Loot Table Database");
                host.name = "Core_LootTableDatabase";
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(host);
            }

            #pragma warning disable CS0618
            var legacySpawner = host.GetComponent<LootSpawner>();
            if (legacySpawner != null) Undo.DestroyObjectImmediate(legacySpawner);
            #pragma warning restore CS0618

            database = host.GetComponent<LootTableDatabase>();
            if (database == null) database = Undo.AddComponent<LootTableDatabase>(host);
        }

        // 场景已经拥有新数据库但仍残留旧占位组件时，同样清理掉旧组件。
        #pragma warning disable CS0618
        var remainingLegacySpawner = database.GetComponent<LootSpawner>();
        if (remainingLegacySpawner != null) Undo.DestroyObjectImmediate(remainingLegacySpawner);
        #pragma warning restore CS0618

        var changed = false;
        var bootstrap = Object.FindObjectOfType<GameBootstrap>(true);
        if (bootstrap != null)
            changed |= SetObjectReference(bootstrap, "lootTableDatabase", database);

        var nightAssault = Object.FindObjectOfType<NightAssaultSystem>(true);
        if (nightAssault != null)
        {
            changed |= SetObjectReference(nightAssault, "lootTableDatabase", database);
            changed |= SetString(nightAssault, "victoryRewardLootTableId", "night_assault_reward");
        }

        var inventory = Object.FindObjectOfType<InventoryService>(true);
        var grid = Object.FindObjectOfType<GridInventoryService>(true);
        if (inventory != null && grid != null)
            changed |= SetObjectReference(inventory, "gridInventoryService", grid);

        if (database.gameObject.name != "Core_LootTableDatabase")
        {
            Undo.RecordObject(database.gameObject, "Rename Loot Table Database");
            database.gameObject.name = "Core_LootTableDatabase";
            changed = true;
        }

        if (changed || scene.isDirty)
            EditorSceneManager.MarkSceneDirty(scene);
    }

    private static bool SetObjectReference(Object target, string propertyName, Object value)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue == value) return false;
        property.objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
        return true;
    }

    private static bool SetString(Object target, string propertyName, string value)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(propertyName);
        if (property == null || property.stringValue == value) return false;
        property.stringValue = value;
        serialized.ApplyModifiedProperties();
        return true;
    }
}
#endif
