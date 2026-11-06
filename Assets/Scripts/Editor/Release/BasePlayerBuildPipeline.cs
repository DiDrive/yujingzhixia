using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

/// <summary>
/// Windows 基础客户端构建流程。
/// 自动执行 HybridCLR/Generate/All，构建内置资源与 Player，并生成同版本的初始 CDN 待发布目录。
/// </summary>
public static class BasePlayerBuildPipeline
{
    private const BuildTarget Target = BuildTarget.StandaloneWindows64;
    private const string BootScenePath = "Assets/Scenes/BootScene.unity";
    private const string HotUpdateAssemblyName = "HotUpdate.dll";
    private const string PlayerOutputRoot = "PlayerBuilds/PC";
    private const string ExecutableName = "EmbersBelow.exe";

    private static readonly Regex SafeNameRegex =
        new Regex("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    [Serializable]
    public sealed class BuildOptions
    {
        /// <summary>客户端基线目录，例如 v1.0；必须和 BootScene 中 CDN 地址的末级目录一致。</summary>
        public string playerBaseline = RemoteUpdateBuildPipeline.DefaultBaseline;

        /// <summary>随客户端内置的 YooAsset 资源版本；每次构建必须使用新版本号。</summary>
        public string packageVersion;

        /// <summary>是否构建 Development Player。正式测试通常关闭。</summary>
        public bool developmentBuild;

        /// <summary>成功后是否在资源管理器中打开客户端目录。</summary>
        public bool revealOutputDirectory = true;
    }

    public sealed class BuildOutput
    {
        public string PlayerDirectory { get; internal set; }
        public string ExecutablePath { get; internal set; }
        public string PackageDirectory { get; internal set; }
        public string StagingDirectory { get; internal set; }
        public ulong TotalSize { get; internal set; }
        public TimeSpan BuildDuration { get; internal set; }
    }

    public static BuildOutput Build(BuildOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        Normalize(options);
        ValidateEnvironment(options);

        try
        {
            EditorUtility.DisplayProgressBar("基础客户端构建", "1/6 执行 HybridCLR Generate/All", 0.05f);
            GenerateHybridClr(options.developmentBuild);

            EditorUtility.DisplayProgressBar("基础客户端构建", "2/6 校验 HybridCLR 生成产物", 0.2f);
            ValidateHybridClrGeneratedFiles();

            EditorUtility.DisplayProgressBar("基础客户端构建", "3/6 复制 HotUpdate.dll", 0.28f);
            RemoteUpdateBuildPipeline.CopyGeneratedHotUpdateDll();
            string hotUpdateHash = RemoteUpdateBuildPipeline.ValidateHotUpdateDll();

            EditorUtility.DisplayProgressBar("基础客户端构建", "4/6 构建并复制 YooAsset 内置资源", 0.4f);
            string packageDirectory = RemoteUpdateBuildPipeline.BuildYooAssetPackage(
                options.packageVersion,
                EBundledCopyOption.ClearAndCopyAll);
            RemoteUpdateBuildPipeline.ValidatePackageOutput(packageDirectory, options.packageVersion);

            EditorUtility.DisplayProgressBar("基础客户端构建", "5/6 构建 Windows 64 位 IL2CPP Player", 0.65f);
            BuildOutput output = BuildWindowsPlayer(options, packageDirectory);

            EditorUtility.DisplayProgressBar("基础客户端构建", "6/6 生成初始 CDN 待发布目录", 0.94f);
            RemoteUpdateBuildPipeline.BuildOutput releaseOutput =
                RemoteUpdateBuildPipeline.CreateStagingDirectory(
                    new RemoteUpdateBuildPipeline.BuildOptions
                    {
                        packageVersion = options.packageVersion,
                        playerBaseline = options.playerBaseline,
                        compileHotUpdateDll = false,
                        revealStagingDirectory = false
                    },
                    packageDirectory,
                    hotUpdateHash);
            output.StagingDirectory = releaseOutput.StagingDirectory;

            Debug.Log(
                $"[基础客户端构建] 构建成功\n" +
                $"客户端基线：{options.playerBaseline}\n" +
                $"内置资源版本：{options.packageVersion}\n" +
                $"可执行文件：{output.ExecutablePath}\n" +
                $"初始 CDN 待发布目录：{output.StagingDirectory}\n" +
                $"构建大小：{FormatBytes(output.TotalSize)}\n" +
                $"耗时：{output.BuildDuration}");

            if (options.revealOutputDirectory)
                EditorUtility.RevealInFinder(output.PlayerDirectory);

            return output;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void Normalize(BuildOptions options)
    {
        options.playerBaseline = (options.playerBaseline ?? string.Empty).Trim();
        options.packageVersion = (options.packageVersion ?? string.Empty).Trim();
    }

    private static void ValidateEnvironment(BuildOptions options)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出运行模式，再构建基础客户端。");
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Unity 正在编译或刷新资源，请等待完成后重试。");
        if (EditorUserBuildSettings.activeBuildTarget != Target)
            throw new InvalidOperationException("请先将当前构建平台切换为 Windows 64 位（StandaloneWindows64）。");
        if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.IL2CPP)
            throw new InvalidOperationException("Standalone 的 Scripting Backend 必须设置为 IL2CPP。");

        ValidateSafeName(options.playerBaseline, "客户端基线");
        ValidateSafeName(options.packageVersion, "内置资源版本");
        ValidateBuildScenes();

        string playerDirectory = GetPlayerDirectory(options.playerBaseline);
        if (Directory.Exists(playerDirectory))
            throw new IOException(
                "该客户端基线的输出目录已经存在。为防止覆盖，请使用新的基线版本，或手动归档旧目录：\n" +
                playerDirectory);

        string packageDirectory = Path.Combine(
            GetProjectRoot(),
            "Bundles",
            Target.ToString(),
            RemoteUpdateBuildPipeline.PackageName,
            options.packageVersion);
        if (Directory.Exists(packageDirectory))
            throw new IOException("该 YooAsset 资源版本已经存在，请换一个新版本号：\n" + packageDirectory);

        string stagingDirectory =
            RemoteUpdateBuildPipeline.GetStagingDirectory(options.playerBaseline, options.packageVersion);
        if (Directory.Exists(stagingDirectory))
            throw new IOException(
                "该基线和资源版本的 CDN 待发布目录已经存在，请换一个新资源版本号：\n" +
                stagingDirectory);
    }

    private static void ValidateBuildScenes()
    {
        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (enabledScenes.Length != 1 ||
            !string.Equals(enabledScenes[0], BootScenePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Build Settings 中必须只启用 BootScene。RaidScene 和 ShelterScene 由 YooAsset 加载，不应直接打进 Player。\n" +
                "期望：" + BootScenePath);
        }

        if (!File.Exists(Path.Combine(GetProjectRoot(), BootScenePath)))
            throw new FileNotFoundException("找不到启动场景：" + BootScenePath);
    }

    private static void ValidateHybridClrGeneratedFiles()
    {
        string hotUpdateDll = Path.GetFullPath(Path.Combine(
            GetProjectRoot(),
            SettingsUtil.GetHotUpdateDllsOutputDirByTarget(Target),
            HotUpdateAssemblyName));
        string strippedAssembly = Path.GetFullPath(Path.Combine(
            GetProjectRoot(),
            SettingsUtil.GetAssembliesPostIl2CppStripDir(Target),
            "Assembly-CSharp.dll"));
        string linkXml = Path.Combine(GetProjectRoot(), "Assets/HybridCLRGenerate/link.xml");
        string aotReferences = Path.Combine(GetProjectRoot(), "Assets/HybridCLRGenerate/AOTGenericReferences.cs");

        RequireGeneratedFile(hotUpdateDll, "热更新 DLL");
        RequireGeneratedFile(strippedAssembly, "裁剪后的 AOT Assembly-CSharp.dll");
        RequireGeneratedFile(linkXml, "HybridCLR link.xml");
        RequireGeneratedFile(aotReferences, "AOT 泛型引用文件");

        DateTime newestHotUpdateSource = GetNewestSourceWriteTime("Assets/HotUpdate", false);
        if (File.GetLastWriteTimeUtc(hotUpdateDll) < newestHotUpdateSource)
        {
            throw new InvalidOperationException(
                "HybridCLR 的 HotUpdate.dll 比 Assets/HotUpdate 源码旧。\n" +
                "自动 Generate/All 没有生成最新 DLL，请查看 Unity Console 中更早的 HybridCLR 日志。");
        }

        DateTime newestAotSource = GetNewestSourceWriteTime("Assets/Scripts", true);
        if (File.GetLastWriteTimeUtc(strippedAssembly) < newestAotSource)
        {
            throw new InvalidOperationException(
                "HybridCLR 的 AOT 产物比 Assets/Scripts 运行时代码旧。\n" +
                "自动 Generate/All 没有生成最新 AOT 文件，请查看 Unity Console 中更早的 HybridCLR 日志。");
        }
    }

    private static void GenerateHybridClr(bool developmentBuild)
    {
        // HybridCLR Generate/All 会读取这个全局开关决定 DLL 的编译模式。
        // 构建后恢复原设置，避免工具悄悄改变开发者的 Editor 配置。
        bool previousDevelopmentSetting = EditorUserBuildSettings.development;
        try
        {
            EditorUserBuildSettings.development = developmentBuild;
            PrebuildCommand.GenerateAll();
        }
        finally
        {
            EditorUserBuildSettings.development = previousDevelopmentSetting;
        }
    }

    private static DateTime GetNewestSourceWriteTime(string projectRelativeRoot, bool excludeEditorFolders)
    {
        string root = Path.Combine(GetProjectRoot(), projectRelativeRoot);
        if (!Directory.Exists(root))
            return DateTime.MinValue;

        IEnumerable<string> files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories);
        if (excludeEditorFolders)
        {
            files = files.Where(path =>
                path.IndexOf(
                    Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) < 0);
        }

        return files.Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
    }

    private static BuildOutput BuildWindowsPlayer(BuildOptions options, string packageDirectory)
    {
        string playerDirectory = GetPlayerDirectory(options.playerBaseline);
        Directory.CreateDirectory(playerDirectory);
        string executablePath = Path.Combine(playerDirectory, ExecutableName);

        var buildOptions = new BuildPlayerOptions
        {
            scenes = new[] { BootScenePath },
            locationPathName = executablePath,
            target = Target,
            options = options.developmentBuild
                ? UnityEditor.BuildOptions.Development
                : UnityEditor.BuildOptions.None
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Windows Player 构建失败：{report.summary.result}\n" +
                $"错误：{report.summary.totalErrors}，警告：{report.summary.totalWarnings}");
        }

        return new BuildOutput
        {
            PlayerDirectory = playerDirectory,
            ExecutablePath = executablePath,
            PackageDirectory = packageDirectory,
            TotalSize = report.summary.totalSize,
            BuildDuration = report.summary.totalTime
        };
    }

    private static string GetPlayerDirectory(string playerBaseline)
    {
        return Path.Combine(GetProjectRoot(), PlayerOutputRoot, playerBaseline);
    }

    private static void ValidateSafeName(string value, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(displayName + "不能为空。");
        if (!SafeNameRegex.IsMatch(value))
            throw new ArgumentException(displayName + "只能包含英文字母、数字、点、下划线和短横线。");
    }

    private static void RequireGeneratedFile(string path, string displayName)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "自动 Generate/All 后仍缺少" + displayName + "，请检查 HybridCLR 配置与 Console 日志：\n" + path,
                path);
        }
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static string FormatBytes(ulong bytes)
    {
        const double unit = 1024d;
        if (bytes < unit)
            return bytes + " B";
        if (bytes < unit * unit)
            return (bytes / unit).ToString("F1") + " KB";
        if (bytes < unit * unit * unit)
            return (bytes / unit / unit).ToString("F1") + " MB";
        return (bytes / unit / unit / unit).ToString("F2") + " GB";
    }
}
