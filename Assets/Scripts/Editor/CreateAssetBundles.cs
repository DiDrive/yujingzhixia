using UnityEditor;

public class CreateAssetBundles
{
    [MenuItem("AssetBundles/Build AssetBundles")]
    static void BuildAllAssetBundles(){
        string dir ="Assets/AssetBundles";
        if(!System.IO.Directory.Exists(dir)){
            System.IO.Directory.CreateDirectory(dir);
        }

        BuildPipeline.BuildAssetBundles(dir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        
    }
}
