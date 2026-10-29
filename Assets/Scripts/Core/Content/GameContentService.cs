using System.Collections;
using UnityEngine;
using YooAsset;

/// <summary>
/// 游戏配置资源服务。
/// 在进入业务场景前，通过 YooAsset 加载并持有配置资源。
/// </summary>
public static class GameContentService
{
    public const string ItemDefinitionsLocation = "item_definitions";   // 物品配置 JSON 文件路径
    public const string LootTablesLocation = "loot_tables";             // 掉落配置 JSON 文件路径
    public const string SurvivalRulesLocation = "survival_rules";       // 生存规则配置 JSON 文件路径

    private static AssetHandle itemDefinitionsHandle;
    private static AssetHandle lootTablesHandle;
    private static AssetHandle survivalRulesHandle;

    public static string ItemDefinitionsJson { get; private set; }
    public static string LootTablesJson { get; private set; }
    public static string SurvivalRulesJson { get; private set; }

    public static bool IsReady { get; private set; }
    public static string Error { get; private set; }

    public static IEnumerator InitializeAsync(ResourcePackage package)
    {
        Release();

        IsReady = false;
        Error = null;

        if (package == null)
        {
            Error = "GameContentService 初始化失败：ResourcePackage 为空";
            Debug.LogError(Error);
            yield break;
        }

        itemDefinitionsHandle =
            package.LoadAssetAsync<TextAsset>(ItemDefinitionsLocation);

        yield return itemDefinitionsHandle;

        if (itemDefinitionsHandle.Status != EOperationStatus.Succeeded)
        {
            Error =
                $"加载物品配置失败：{itemDefinitionsHandle.Error}";
            Debug.LogError(Error);
            Release();
            yield break;
        }

        var itemDefinitionsAsset =
            itemDefinitionsHandle.AssetObject as TextAsset;

        if (itemDefinitionsAsset == null ||
            string.IsNullOrWhiteSpace(itemDefinitionsAsset.text))
        {
            Error = "物品配置内容为空";
            Debug.LogError(Error);
            Release();
            yield break;
        }

        ItemDefinitionsJson = itemDefinitionsAsset.text;

        lootTablesHandle =
            package.LoadAssetAsync<TextAsset>(LootTablesLocation);

        yield return lootTablesHandle;

        if (lootTablesHandle.Status != EOperationStatus.Succeeded)
        {
            Error =
                $"加载掉落配置失败：{lootTablesHandle.Error}";
            Debug.LogError(Error);
            Release();
            yield break;
        }

        var lootTablesAsset =
            lootTablesHandle.AssetObject as TextAsset;

        if (lootTablesAsset == null ||
            string.IsNullOrWhiteSpace(lootTablesAsset.text))
        {
            Error = "掉落配置内容为空";
            Debug.LogError(Error);
            Release();
            yield break;
        }

        LootTablesJson = lootTablesAsset.text;

        survivalRulesHandle =
            package.LoadAssetAsync<TextAsset>(SurvivalRulesLocation);

        yield return survivalRulesHandle;

        if (survivalRulesHandle.Status != EOperationStatus.Succeeded)
        {
            Error =
                $"加载生存规则配置失败：{survivalRulesHandle.Error}";
            Debug.LogError(Error);
            Release();
            yield break;
        }

        var survivalRulesAsset =
            survivalRulesHandle.AssetObject as TextAsset;

        if (survivalRulesAsset == null ||
            string.IsNullOrWhiteSpace(survivalRulesAsset.text))
        {
            Error = "生存规则配置内容为空";
            Debug.LogError(Error);
            Release();
            yield break;
        }

        SurvivalRulesJson = survivalRulesAsset.text;
        IsReady = true;

        Debug.Log(
            $"游戏配置加载完成：" +
            $"items={ItemDefinitionsJson.Length}字符，" +
            $"loot={LootTablesJson.Length}字符，" +
            $"survival={SurvivalRulesJson.Length}字符");
    }

    public static void Release()
    {
        if (itemDefinitionsHandle != null)
        {
            itemDefinitionsHandle.Release();
            itemDefinitionsHandle = null;
        }

        if (lootTablesHandle != null)
        {
            lootTablesHandle.Release();
            lootTablesHandle = null;
        }

        if (survivalRulesHandle != null)
        {
            survivalRulesHandle.Release();
            survivalRulesHandle = null;
        }

        ItemDefinitionsJson = null;
        LootTablesJson = null;
        SurvivalRulesJson = null;
        IsReady = false;
    }
}
