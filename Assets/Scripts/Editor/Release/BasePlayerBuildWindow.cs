using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 基础客户端构建窗口。
/// </summary>
public sealed class BasePlayerBuildWindow : EditorWindow
{
    private const string MenuPath = "Tools/余烬之下/构建与发布/基础客户端构建";
    private const string BaselinePrefsKey = "EmbersBelow.RemoteUpdate.PlayerBaseline";

    private string playerBaseline;
    private string packageVersion;
    private bool developmentBuild;
    private bool revealOutputDirectory = true;
    private Vector2 scrollPosition;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        var window = GetWindow<BasePlayerBuildWindow>("基础客户端构建");
        window.minSize = new Vector2(620f, 570f);
        window.Show();
    }

    private void OnEnable()
    {
        playerBaseline = EditorPrefs.GetString(
            BaselinePrefsKey,
            RemoteUpdateBuildPipeline.DefaultBaseline);
        packageVersion = RemoteUpdateBuildPipeline.SuggestPackageVersion();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Windows 基础客户端构建", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "这是完整的基础客户端流水线：自动执行 HybridCLR Generate/All、构建内置 YooAsset 资源和 Windows IL2CPP Player，并生成同版本的初始 CDN 待发布目录。",
            MessageType.Info);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("开始前请确认", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("1. 切换到 Windows 64 位平台");
        EditorGUILayout.LabelField("2. 退出 Play Mode，等待 Unity 编译结束");
        EditorGUILayout.LabelField("3. Console 中没有脚本编译错误");

        EditorGUILayout.Space(10f);
        playerBaseline = EditorGUILayout.TextField(
            new GUIContent("客户端基线", "例如 v1.0；需要与 BootScene 的 CDN 地址一致"),
            playerBaseline);
        packageVersion = EditorGUILayout.TextField(
            new GUIContent("内置资源版本", "随客户端放入 StreamingAssets 的 YooAsset 版本"),
            packageVersion);
        developmentBuild = EditorGUILayout.ToggleLeft("构建 Development Player（调试包）", developmentBuild);
        revealOutputDirectory = EditorGUILayout.ToggleLeft("成功后打开客户端目录", revealOutputDirectory);

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("工具将自动执行", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("1. HybridCLR Generate/All");
        EditorGUILayout.LabelField("2. 校验生成产物并复制 HotUpdate.dll.bytes");
        EditorGUILayout.LabelField("3. 使用 ClearAndCopyAll 构建 DefaultPackage 到 StreamingAssets");
        EditorGUILayout.LabelField("4. 只用 BootScene 构建 Windows 64 位 IL2CPP Player");
        EditorGUILayout.LabelField("5. 输出到 PlayerBuilds/PC/<客户端基线>/");
        EditorGUILayout.LabelField("6. 生成 ReleaseStaging/PC/<客户端基线>/<资源版本>/");

        EditorGUILayout.Space(10f);
        EditorGUILayout.HelpBox(
            "工具只生成初始 CDN 待发布目录，不会直接上传。构建成功后请使用“发布到本机 CDN”。客户端基线必须与 BootScene 中 defaultHostServer / fallbackHostServer 的末级目录一致。",
            MessageType.Warning);

        using (new EditorGUI.DisabledScope(
                   EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
        {
            if (GUILayout.Button("开始构建基础客户端", GUILayout.Height(44f)))
                RunBuild();
        }

        if (GUILayout.Button("生成一个新的建议资源版本号", GUILayout.Height(28f)))
            packageVersion = RemoteUpdateBuildPipeline.SuggestPackageVersion();

        if (GUILayout.Button("打开本机 CDN 发布工具", GUILayout.Height(28f)))
            LocalCdnPublishWindow.OpenWindow();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("当前构建平台", EditorUserBuildSettings.activeBuildTarget.ToString());
        EditorGUILayout.LabelField("当前 Player 版本", PlayerSettings.bundleVersion);
        EditorGUILayout.EndScrollView();
    }

    private void RunBuild()
    {
        GUI.FocusControl(null);
        EditorPrefs.SetString(BaselinePrefsKey, playerBaseline ?? string.Empty);

        bool confirmed = EditorUtility.DisplayDialog(
            "确认构建基础客户端",
            $"工具将先自动执行 HybridCLR → Generate → All。\n\n" +
            $"客户端基线：{playerBaseline}\n" +
            $"内置资源版本：{packageVersion}\n" +
            $"构建类型：{(developmentBuild ? "Development" : "Release")}\n\n" +
            "构建过程可能持续较长时间，是否继续？",
            "开始构建",
            "取消");
        if (!confirmed)
            return;

        var options = new BasePlayerBuildPipeline.BuildOptions
        {
            playerBaseline = playerBaseline,
            packageVersion = packageVersion,
            developmentBuild = developmentBuild,
            revealOutputDirectory = revealOutputDirectory
        };

        try
        {
            BasePlayerBuildPipeline.BuildOutput output = BasePlayerBuildPipeline.Build(options);
            EditorUtility.DisplayDialog(
                "基础客户端构建完成",
                $"客户端基线：{playerBaseline}\n" +
                $"内置资源版本：{packageVersion}\n\n" +
                $"可执行文件：\n{output.ExecutablePath}\n\n" +
                $"初始 CDN 待发布目录：\n{output.StagingDirectory}\n\n" +
                "下一步：打开“发布到本机 CDN”，发布这个相同版本，然后再启动新客户端验证。",
                "知道了");

            packageVersion = RemoteUpdateBuildPipeline.SuggestPackageVersion();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("基础客户端构建失败", exception.Message, "知道了");
        }
    }
}
