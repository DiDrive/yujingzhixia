using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class CreateAssetBundles
{
    [System.Serializable]
    private class BundleCrcEntry
    {
        public string bundleName;
        public uint crc;
    }

    [System.Serializable]
    private class BundleCrcTable
    {
        public List<BundleCrcEntry> items = new List<BundleCrcEntry>();
    }

    [MenuItem("AssetBundles/Build AssetBundles")]
    static void BuildAllAssetBundles()
    {
        var dir = "AssetBundles";
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var manifest = BuildPipeline.BuildAssetBundles(dir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        if (manifest == null)
        {
            Debug.LogError("Build AssetBundles failed, manifest is null.");
            return;
        }

        ExportBundleCrcTable(dir, manifest);
        AssetDatabase.Refresh();
    }

    private static void ExportBundleCrcTable(string outputDir, AssetBundleManifest manifest)
    {
        var table = new BundleCrcTable();
        var bundleNames = manifest.GetAllAssetBundles();
        for (var i = 0; i < bundleNames.Length; i++)
        {
            var bundleName = bundleNames[i];
            var bundlePath = Path.Combine(outputDir, bundleName);
            if (!BuildPipeline.GetCRCForAssetBundle(bundlePath, out var crc))
            {
                Debug.LogWarning($"Get CRC failed: {bundlePath}");
                continue;
            }

            table.items.Add(new BundleCrcEntry
            {
                bundleName = bundleName,
                crc = crc
            });
        }

        var json = JsonUtility.ToJson(table, true);
        var crcMapPath = Path.Combine(outputDir, "crc_map.json");
        File.WriteAllText(crcMapPath, json);
        Debug.Log($"Exported CRC map: {crcMapPath}");
    }

}
