using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 本机目录型 CDN 的安全发布窗口。
/// </summary>
public sealed class LocalCdnPublishWindow : EditorWindow
{
    private const string MenuPath = "Tools/余烬之下/构建与发布/发布到本机 CDN";
    private const string CdnRootPrefsKey = "EmbersBelow.LocalCdn.PhysicalRoot";
    private const string CdnUrlPrefsKey = "EmbersBelow.LocalCdn.BaseUrl";
    private const string BaselinePrefsKey = "EmbersBelow.RemoteUpdate.PlayerBaseline";

    private string stagingDirectory;
    private string cdnPhysicalRoot;
    private string cdnBaseUrl;
    private string playerBaseline;
    private bool verifyOverHttp = true;
    private Vector2 scrollPosition;

    [MenuItem(MenuPath)]
    public static void OpenWindow()
    {
        var window = GetWindow<LocalCdnPublishWindow>("本机 CDN 发布");
        window.minSize = new Vector2(650f, 560f);
        window.Show();
    }

    private void OnEnable()
    {
        playerBaseline = EditorPrefs.GetString(BaselinePrefsKey, RemoteUpdateBuildPipeline.DefaultBaseline);
        cdnPhysicalRoot = EditorPrefs.GetString(CdnRootPrefsKey, string.Empty);
        cdnBaseUrl = EditorPrefs.GetString(CdnUrlPrefsKey, "http://127.0.0.1/CDN");
        RefreshLatestStagingDirectory();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("发布到本机目录型 CDN", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "适用于本机 IIS、Nginx、Apache 或其他把本地文件夹映射为 HTTP 地址的服务器。" +
            "工具会先复制 Bundle 和 Manifest，校验成功后最后更新 DefaultPackage.version。",
            MessageType.Info);

        EditorGUILayout.Space(8f);
        DrawPathField("待发布版本目录", ref stagingDirectory, SelectStagingDirectory);
        if (GUILayout.Button("重新选择最新待发布版本", GUILayout.Height(25f)))
            RefreshLatestStagingDirectory();

        string version = LocalCdnPublisher.ReadPackageVersion(stagingDirectory);
        EditorGUILayout.LabelField("检测到的资源版本", string.IsNullOrEmpty(version) ? "未检测到" : version);

        EditorGUILayout.Space(12f);
        DrawPathField("CDN 物理根目录", ref cdnPhysicalRoot, SelectCdnRoot);
        EditorGUILayout.HelpBox(
            "这里请选择 URL 中 /CDN 对应的文件夹，不要选择 PC/v1.0。" +
            "例如 URL 是 http://127.0.0.1/CDN，服务器映射目录是 D:/GameCDN，就选择 D:/GameCDN。",
            MessageType.None);

        cdnBaseUrl = EditorGUILayout.TextField(new GUIContent("CDN HTTP 根地址"), cdnBaseUrl);
        playerBaseline = EditorGUILayout.TextField(new GUIContent("玩家大版本目录"), playerBaseline);
        verifyOverHttp = EditorGUILayout.ToggleLeft("更新版本指针前，通过 HTTP 校验所有发布文件", verifyOverHttp);

        var previewOptions = CreateOptions();
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("最终物理目录", EmptyFallback(LocalCdnPublisher.GetTargetDirectory(previewOptions)));
        EditorGUILayout.LabelField("客户端访问地址", EmptyFallback(LocalCdnPublisher.GetTargetUrl(previewOptions)));

        EditorGUILayout.Space(12f);
        EditorGUILayout.HelpBox(
            "点击发布后会修改 CDN 目录，并最终覆盖 DefaultPackage.version。" +
            "如果 HTTP 校验失败，工具不会发布新版本指针；版本指针发布后的校验失败时会尝试恢复旧内容。",
            MessageType.Warning);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
        {
            GUI.backgroundColor = new Color(0.95f, 0.65f, 0.25f);
            if (GUILayout.Button("发布这个版本到本机 CDN", GUILayout.Height(44f)))
                ConfirmAndPublish();
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.Space(6f);
        if (GUILayout.Button("打开待发布目录", GUILayout.Height(27f)) && Directory.Exists(stagingDirectory))
            EditorUtility.RevealInFinder(stagingDirectory);
        if (GUILayout.Button("打开 CDN 目标目录", GUILayout.Height(27f)))
        {
            string target = LocalCdnPublisher.GetTargetDirectory(previewOptions);
            if (Directory.Exists(target))
                EditorUtility.RevealInFinder(target);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawPathField(string label, ref string value, Action browseAction)
    {
        EditorGUILayout.BeginHorizontal();
        value = EditorGUILayout.TextField(label, value);
        if (GUILayout.Button("选择…", GUILayout.Width(70f)))
            browseAction();
        EditorGUILayout.EndHorizontal();
    }

    private void SelectStagingDirectory()
    {
        string initial = Directory.Exists(stagingDirectory) ? stagingDirectory : Application.dataPath;
        string selected = EditorUtility.OpenFolderPanel("选择待发布版本目录", initial, string.Empty);
        if (!string.IsNullOrEmpty(selected))
            stagingDirectory = selected;
    }

    private void SelectCdnRoot()
    {
        string initial = Directory.Exists(cdnPhysicalRoot) ? cdnPhysicalRoot : Application.dataPath;
        string selected = EditorUtility.OpenFolderPanel("选择 URL /CDN 对应的物理目录", initial, string.Empty);
        if (!string.IsNullOrEmpty(selected))
            cdnPhysicalRoot = selected;
    }

    private void RefreshLatestStagingDirectory()
    {
        stagingDirectory = LocalCdnPublisher.FindLatestStagingDirectory("PC", playerBaseline);
    }

    private LocalCdnPublisher.PublishOptions CreateOptions()
    {
        return new LocalCdnPublisher.PublishOptions
        {
            stagingDirectory = stagingDirectory,
            cdnPhysicalRoot = cdnPhysicalRoot,
            cdnBaseUrl = cdnBaseUrl,
            platformFolder = "PC",
            playerBaseline = playerBaseline,
            verifyOverHttp = verifyOverHttp
        };
    }

    private void ConfirmAndPublish()
    {
        var options = CreateOptions();
        string version = LocalCdnPublisher.ReadPackageVersion(stagingDirectory);
        if (string.IsNullOrWhiteSpace(cdnPhysicalRoot))
        {
            EditorUtility.DisplayDialog(
                "尚未选择 CDN 目录",
                "请先选择 URL 中 /CDN 对应的本地物理文件夹。",
                "知道了");
            return;
        }

        string targetDirectory;
        try
        {
            targetDirectory = LocalCdnPublisher.GetTargetDirectory(options);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("发布配置错误", exception.Message, "知道了");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "确认发布远程更新",
            $"即将发布资源版本：{version}\n\n" +
            $"来源：\n{stagingDirectory}\n\n" +
            $"目标：\n{targetDirectory}\n\n" +
            "DefaultPackage.version 将在全部资源校验后最后更新。是否继续？",
            "确认发布",
            "取消");
        if (!confirmed)
            return;

        SavePreferences();
        try
        {
            LocalCdnPublisher.PublishResult result = LocalCdnPublisher.Publish(
                options,
                (message, progress) => EditorUtility.DisplayProgressBar("发布到本机 CDN", message, progress));

            Debug.Log(
                $"[本机 CDN 发布] 成功发布 {result.PackageVersion}\n" +
                $"复制：{result.CopiedFileCount}，跳过相同文件：{result.SkippedFileCount}\n" +
                $"目标目录：{result.TargetDirectory}");
            EditorUtility.DisplayDialog(
                "CDN 发布完成",
                $"资源版本：{result.PackageVersion}\n" +
                $"复制文件：{result.CopiedFileCount}\n" +
                $"跳过相同文件：{result.SkippedFileCount}\n" +
                $"HTTP 校验：{(result.HttpVerified ? "已通过" : "未启用")}\n\n" +
                "现在可以启动旧版客户端进行完整更新测试。",
                "知道了");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "CDN 发布失败",
                exception.Message + "\n\n新版本指针未发布，或已尝试恢复旧版本指针。",
                "知道了");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void SavePreferences()
    {
        EditorPrefs.SetString(CdnRootPrefsKey, cdnPhysicalRoot ?? string.Empty);
        EditorPrefs.SetString(CdnUrlPrefsKey, cdnBaseUrl ?? string.Empty);
        EditorPrefs.SetString(BaselinePrefsKey, playerBaseline ?? string.Empty);
    }

    private static string EmptyFallback(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "请先填写上方配置" : value;
    }
}
