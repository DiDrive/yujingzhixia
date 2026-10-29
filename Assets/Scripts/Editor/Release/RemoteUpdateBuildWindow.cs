using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 远程更新构建的编辑器入口。
/// </summary>
public sealed class RemoteUpdateBuildWindow : EditorWindow
{
    private const string MenuPath = "Tools/余烬之下/构建与发布/远程更新构建";
    private const string BaselinePrefsKey = "EmbersBelow.RemoteUpdate.PlayerBaseline";

    private string packageVersion;
    private string playerBaseline;
    private bool compileHotUpdateDll = true;
    private bool revealStagingDirectory = true;
    private Vector2 scrollPosition;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        var window = GetWindow<RemoteUpdateBuildWindow>("远程更新构建");
        window.minSize = new Vector2(570f, 490f);
        window.Show();
    }

    private void OnEnable()
    {
        if (string.IsNullOrWhiteSpace(packageVersion))
            packageVersion = RemoteUpdateBuildPipeline.SuggestPackageVersion();
        if (string.IsNullOrWhiteSpace(playerBaseline))
            playerBaseline = EditorPrefs.GetString(BaselinePrefsKey, RemoteUpdateBuildPipeline.DefaultBaseline);
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("远程更新构建", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "这个工具会编译 HotUpdate.dll、构建 DefaultPackage，并生成 ReleaseStaging 待发布目录。" +
            "它不会自动上传 CDN。",
            MessageType.Info);

        EditorGUILayout.Space(8f);
        packageVersion = EditorGUILayout.TextField(new GUIContent("资源版本号", "每次发布必须使用全新的版本号"), packageVersion);
        playerBaseline = EditorGUILayout.TextField(new GUIContent("玩家大版本目录", "应和 CDN 地址中的 v1.0 等目录一致"), playerBaseline);
        compileHotUpdateDll = EditorGUILayout.ToggleLeft("先编译并复制 HybridCLR HotUpdate.dll", compileHotUpdateDll);
        revealStagingDirectory = EditorGUILayout.ToggleLeft("成功后打开待发布目录", revealStagingDirectory);

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("将自动执行", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("1. HybridCLR Release 模式编译 HotUpdate.dll");
        EditorGUILayout.LabelField("2. 复制到 Assets/AB/HotDll/HotUpdate.dll.bytes 并校验 SHA256");
        EditorGUILayout.LabelField("3. 以 Remote 模式构建 YooAsset DefaultPackage");
        EditorGUILayout.LabelField("4. 校验 Version、Manifest、Hash 和 Bundle");
        EditorGUILayout.LabelField("5. 生成 ReleaseStaging/PC/<大版本>/<资源版本>/");

        EditorGUILayout.Space(10f);
        EditorGUILayout.HelpBox(
            "发布时必须先上传 Bundle 和 Manifest，确认可访问后，最后上传 DefaultPackage.version。" +
            "工具会在产物中生成 UPLOAD_ORDER.txt。",
            MessageType.Warning);

        EditorGUILayout.Space(12f);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
        {
            if (GUILayout.Button("开始构建远程更新", GUILayout.Height(42f)))
                RunBuild();
        }

        if (GUILayout.Button("生成一个新的建议版本号", GUILayout.Height(28f)))
            packageVersion = RemoteUpdateBuildPipeline.SuggestPackageVersion();

        if (GUILayout.Button("打开本机 CDN 发布工具", GUILayout.Height(28f)))
            LocalCdnPublishWindow.OpenWindow();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("当前构建平台：" + EditorUserBuildSettings.activeBuildTarget);
        EditorGUILayout.LabelField("目标平台：StandaloneWindows64");

        EditorGUILayout.EndScrollView();
    }

    private void RunBuild()
    {
        GUI.FocusControl(null);
        EditorPrefs.SetString(BaselinePrefsKey, playerBaseline ?? string.Empty);

        var options = new RemoteUpdateBuildPipeline.BuildOptions
        {
            packageVersion = packageVersion,
            playerBaseline = playerBaseline,
            compileHotUpdateDll = compileHotUpdateDll,
            revealStagingDirectory = revealStagingDirectory
        };

        try
        {
            RemoteUpdateBuildPipeline.BuildOutput output = RemoteUpdateBuildPipeline.Build(options);
            EditorUtility.DisplayDialog(
                "远程更新构建完成",
                $"资源版本：{packageVersion}\n" +
                $"Bundle 数量：{output.BundleCount}\n\n" +
                $"待发布目录：\n{output.StagingDirectory}\n\n" +
                "请先查看 UPLOAD_ORDER.txt，不要直接覆盖线上 version 文件。",
                "知道了");

            packageVersion = RemoteUpdateBuildPipeline.SuggestPackageVersion();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("远程更新构建失败", exception.Message, "知道了");
        }
    }
}
