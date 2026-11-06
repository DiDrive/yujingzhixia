using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

/// <summary>
/// 项目的远程资源更新构建流程。
/// 只生成本地待发布目录，不会上传 CDN，也不会修改线上版本指针。
/// </summary>
public static class RemoteUpdateBuildPipeline
{
    public const string PackageName = "DefaultPackage";
    public const string DefaultBaseline = "v1.0";

    private const BuildTarget SupportedTarget = BuildTarget.StandaloneWindows64;
    private const string HotUpdateAssemblyName = "HotUpdate";
    private const string HotUpdateDestination = "Assets/AB/HotDll/HotUpdate.dll.bytes";
    private const string BundleOutputRootName = "Bundles";
    private const string StagingRootName = "ReleaseStaging";

    private static readonly Regex SafeNameRegex = new Regex("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    [Serializable]
    public sealed class BuildOptions
    {
        public string packageVersion;
        public string playerBaseline = DefaultBaseline;
        public bool compileHotUpdateDll = true;
        public bool revealStagingDirectory = true;
    }

    public sealed class BuildOutput
    {
        public string PackageDirectory { get; internal set; }
        public string StagingDirectory { get; internal set; }
        public string HotUpdateSha256 { get; internal set; }
        public int BundleCount { get; internal set; }
    }

    [Serializable]
    private sealed class ReleaseInfo
    {
        public string packageName;
        public string packageVersion;
        public string playerBaseline;
        public string buildTarget;
        public string unityVersion;
        public string applicationVersion;
        public string builtAtUtc;
        public string hotUpdateSha256;
        public string sourcePackageDirectory;
        public int bundleCount;
        public List<ReleaseFileInfo> files = new List<ReleaseFileInfo>();
    }

    [Serializable]
    private sealed class ReleaseFileInfo
    {
        public string relativePath;
        public long size;
        public string sha256;
        public string role;
        public bool publishToCdn;
        public int uploadPhase;
    }

    public static string SuggestPackageVersion()
    {
        string datePrefix = DateTime.Now.ToString("yyyyMMdd");
        int largestSequence = 0;

        foreach (string directory in GetExistingVersionDirectories())
        {
            string name = Path.GetFileName(directory);
            Match match = Regex.Match(name, "^" + datePrefix + "-(\\d{3})$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int sequence))
                largestSequence = Math.Max(largestSequence, sequence);
        }

        return $"{datePrefix}-{largestSequence + 1:000}";
    }

    public static BuildOutput Build(BuildOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        NormalizeOptions(options);
        ValidateEnvironment(options);

        string hotUpdateHash;
        try
        {
            if (options.compileHotUpdateDll)
            {
                EditorUtility.DisplayProgressBar("远程更新构建", "1/4 编译 HotUpdate.dll", 0.1f);
                CompileAndCopyHotUpdateDll();
            }

            EditorUtility.DisplayProgressBar("远程更新构建", "2/4 校验 HotUpdate.dll", 0.3f);
            hotUpdateHash = ValidateHotUpdateDll();

            EditorUtility.DisplayProgressBar("远程更新构建", "3/4 构建 YooAsset 资源包", 0.5f);
            string packageDirectory = BuildYooAssetPackage(options.packageVersion);
            ValidatePackageOutput(packageDirectory, options.packageVersion);

            EditorUtility.DisplayProgressBar("远程更新构建", "4/4 生成待发布目录与校验清单", 0.85f);
            BuildOutput output = CreateStagingDirectory(options, packageDirectory, hotUpdateHash);

            Debug.Log(
                $"[远程更新构建] 构建成功\n" +
                $"资源版本：{options.packageVersion}\n" +
                $"Bundle 数量：{output.BundleCount}\n" +
                $"待发布目录：{output.StagingDirectory}");

            if (options.revealStagingDirectory)
                EditorUtility.RevealInFinder(output.StagingDirectory);

            return output;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void NormalizeOptions(BuildOptions options)
    {
        options.packageVersion = (options.packageVersion ?? string.Empty).Trim();
        options.playerBaseline = (options.playerBaseline ?? string.Empty).Trim();
    }

    private static void ValidateEnvironment(BuildOptions options)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出运行模式，再构建远程更新。");
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Unity 正在编译或刷新资源，请等待完成后重试。");
        if (EditorUserBuildSettings.activeBuildTarget != SupportedTarget)
            throw new InvalidOperationException(
                $"当前平台是 {EditorUserBuildSettings.activeBuildTarget}。请先切换到 Windows 64 位（{SupportedTarget}）。");

        ValidateSafeName(options.packageVersion, "资源版本号");
        ValidateSafeName(options.playerBaseline, "玩家大版本目录");

        string bundleVersionDirectory = GetBundleVersionDirectory(options.packageVersion);
        if (Directory.Exists(bundleVersionDirectory))
            throw new IOException($"该资源版本已经构建过，为避免覆盖已发布文件，请换一个新版本号：\n{bundleVersionDirectory}");

        string stagingDirectory = GetStagingDirectory(options.playerBaseline, options.packageVersion);
        if (Directory.Exists(stagingDirectory))
            throw new IOException($"待发布目录已经存在，为避免覆盖，请换一个新版本号：\n{stagingDirectory}");

        RequireFile("Assets/HotUpdate/HotUpdate.asmdef", "HotUpdate 程序集定义");
        RequireFile("Assets/GameSetting/YooAsset/BundleCollectorSetting.asset", "YooAsset 收集配置");
    }

    private static void ValidateSafeName(string value, string displayName)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException(displayName + "不能为空。");
        if (!SafeNameRegex.IsMatch(value))
            throw new ArgumentException(displayName + "只能包含英文字母、数字、点、下划线和短横线。");
    }

    private static void CompileAndCopyHotUpdateDll()
    {
        CompileDllCommand.CompileDll(SupportedTarget, false);

        CopyGeneratedHotUpdateDll();
    }

    /// <summary>
    /// 将 HybridCLR 已经生成的 HotUpdate.dll 复制到 YooAsset 收集目录。
    /// 基础客户端构建会在自动执行 Generate/All 后调用这里。
    /// </summary>
    internal static void CopyGeneratedHotUpdateDll()
    {
        string outputDirectory = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(SupportedTarget);
        string bytesSource = Path.Combine(outputDirectory, HotUpdateAssemblyName + ".dll.bytes");
        string dllSource = Path.Combine(outputDirectory, HotUpdateAssemblyName + ".dll");
        string source = File.Exists(bytesSource) ? bytesSource : dllSource;

        if (!File.Exists(source))
            throw new FileNotFoundException(
                "HybridCLR 编译完成，但没有找到 HotUpdate.dll。请检查 HybridCLRSettings 的热更新程序集配置。",
                bytesSource);

        string destination = ToAbsolutePath(HotUpdateDestination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException());

        if (!File.Exists(destination) || !FilesHaveSameHash(source, destination))
            File.Copy(source, destination, true);

        AssetDatabase.ImportAsset(HotUpdateDestination, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        if (!FilesHaveSameHash(source, destination))
            throw new IOException("HotUpdate.dll 复制后的 SHA256 不一致，已停止构建。");
    }

    internal static string ValidateHotUpdateDll()
    {
        RequireFile(HotUpdateDestination, "YooAsset 收集目录中的 HotUpdate.dll.bytes");
        return CalculateSha256(ToAbsolutePath(HotUpdateDestination));
    }

    internal static string BuildYooAssetPackage(
        string packageVersion,
        EBundledCopyOption bundledCopyOption = EBundledCopyOption.None)
    {
        string projectRoot = GetProjectRoot();
        string outputRoot = Path.Combine(projectRoot, BundleOutputRootName);
        string builtinShaderBundleName = GetBuiltinShaderBundleName(PackageName);

        var buildParameters = new ScriptableBuildParameters
        {
            BuildOutputRoot = outputRoot,
            BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
            BuildPipeline = EBuildPipeline.ScriptableBuildPipeline.ToString(),
            BuildBundleType = (int)EBundleType.AssetBundle,
            BuildTarget = SupportedTarget,
            PackageName = PackageName,
            PackageVersion = packageVersion,
            EnableSharePackRule = true,
            VerifyBuildingResult = true,
            FileNameStyle = EFileNameStyle.HashName,
            BundledCopyOption = bundledCopyOption,
            BundledCopyParams = string.Empty,
            CompressOption = ECompressOption.LZ4,
            ClearBuildCacheFiles = false,
            UseAssetDependencyDB = true,
            BuiltinShadersBundleName = builtinShaderBundleName,
            BundleEncryptor = new BundleEncryption.Encryptor()
        };

        BuildResult result = new ScriptableBuildPipeline().Run(buildParameters, false);
        if (!result.Success)
            throw new InvalidOperationException(
                $"YooAsset 构建失败。失败步骤：{result.FailedTask}\n{result.ErrorInfo}\n{result.ErrorStack}");

        return Path.GetFullPath(result.OutputPackageDirectory);
    }

    private static string GetBuiltinShaderBundleName(string packageName)
    {
        bool uniqueBundleName = BundleCollectorSettingData.Setting.UniqueBundleName;
        var packRuleResult = DefaultBundlePackRule.CreateShadersPackRuleResult();
        return packRuleResult.GetBundleName(packageName, uniqueBundleName);
    }

    internal static void ValidatePackageOutput(string packageDirectory, string packageVersion)
    {
        if (!Directory.Exists(packageDirectory))
            throw new DirectoryNotFoundException("YooAsset 返回的构建目录不存在：" + packageDirectory);

        string versionFile = Path.Combine(packageDirectory, PackageName + ".version");
        string manifestFile = Path.Combine(packageDirectory, $"{PackageName}_{packageVersion}.bytes");
        string hashFile = Path.Combine(packageDirectory, $"{PackageName}_{packageVersion}.hash");

        RequireAbsoluteFile(versionFile, "版本指针文件");
        RequireAbsoluteFile(manifestFile, "资源清单文件");
        RequireAbsoluteFile(hashFile, "资源清单哈希文件");

        string actualVersion = File.ReadAllText(versionFile, Encoding.UTF8).Trim();
        if (!string.Equals(actualVersion, packageVersion, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"版本指针内容不正确。期望 {packageVersion}，实际 {actualVersion}。");

        if (!Directory.EnumerateFiles(packageDirectory, "*.bundle", SearchOption.AllDirectories).Any())
            throw new InvalidDataException("构建目录中没有找到任何 .bundle 文件。");
    }

    internal static BuildOutput CreateStagingDirectory(
        BuildOptions options,
        string packageDirectory,
        string hotUpdateHash)
    {
        string stagingDirectory = GetStagingDirectory(options.playerBaseline, options.packageVersion);
        EnsurePathIsInside(stagingDirectory, Path.Combine(GetProjectRoot(), StagingRootName));
        Directory.CreateDirectory(stagingDirectory);

        foreach (string sourceFile in Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = MakeRelativePath(packageDirectory, sourceFile);
            string destinationFile = Path.Combine(stagingDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? stagingDirectory);
            File.Copy(sourceFile, destinationFile, false);
        }

        List<ReleaseFileInfo> files = Directory
            .EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories)
            .Select(path => CreateReleaseFileInfo(stagingDirectory, path, options.packageVersion))
            .OrderBy(info => info.uploadPhase)
            .ThenBy(info => info.relativePath, StringComparer.Ordinal)
            .ToList();

        int bundleCount = files.Count(file => file.role == "Bundle");
        var releaseInfo = new ReleaseInfo
        {
            packageName = PackageName,
            packageVersion = options.packageVersion,
            playerBaseline = options.playerBaseline,
            buildTarget = SupportedTarget.ToString(),
            unityVersion = Application.unityVersion,
            applicationVersion = PlayerSettings.bundleVersion,
            builtAtUtc = DateTime.UtcNow.ToString("O"),
            hotUpdateSha256 = hotUpdateHash,
            sourcePackageDirectory = packageDirectory,
            bundleCount = bundleCount,
            files = files
        };

        File.WriteAllText(
            Path.Combine(stagingDirectory, "release-info.json"),
            JsonUtility.ToJson(releaseInfo, true),
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(stagingDirectory, "UPLOAD_ORDER.txt"),
            BuildUploadInstructions(options, files),
            new UTF8Encoding(false));

        return new BuildOutput
        {
            PackageDirectory = packageDirectory,
            StagingDirectory = stagingDirectory,
            HotUpdateSha256 = hotUpdateHash,
            BundleCount = bundleCount
        };
    }

    private static ReleaseFileInfo CreateReleaseFileInfo(string root, string path, string packageVersion)
    {
        string fileName = Path.GetFileName(path);
        string role;
        bool publish = true;
        int phase;

        if (fileName.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
        {
            role = "Bundle";
            phase = 1;
        }
        else if (fileName == $"{PackageName}_{packageVersion}.bytes")
        {
            role = "Manifest";
            phase = 2;
        }
        else if (fileName == $"{PackageName}_{packageVersion}.hash")
        {
            role = "ManifestHash";
            phase = 2;
        }
        else if (fileName == PackageName + ".version")
        {
            role = "VersionPointer";
            phase = 3;
        }
        else
        {
            role = "BuildArtifact";
            publish = false;
            phase = 0;
        }

        var fileInfo = new FileInfo(path);
        return new ReleaseFileInfo
        {
            relativePath = MakeRelativePath(root, path).Replace('\\', '/'),
            size = fileInfo.Length,
            sha256 = CalculateSha256(path),
            role = role,
            publishToCdn = publish,
            uploadPhase = phase
        };
    }

    private static string BuildUploadInstructions(BuildOptions options, List<ReleaseFileInfo> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("余烬之下 - YooAsset 远程更新上传顺序");
        builder.AppendLine($"资源版本：{options.packageVersion}");
        builder.AppendLine($"CDN 大版本目录：{options.playerBaseline}");
        builder.AppendLine();
        builder.AppendLine("必须按以下阶段上传；同一阶段内的文件顺序不限：");
        builder.AppendLine("1. 上传所有 Bundle 文件（*.bundle）");
        builder.AppendLine($"2. 上传 {PackageName}_{options.packageVersion}.bytes 和对应 .hash");
        builder.AppendLine("3. 确认前两步文件均可从 CDN 访问");
        builder.AppendLine($"4. 最后上传 {PackageName}.version（它相当于正式发布开关）");
        builder.AppendLine();
        builder.AppendLine("不要上传：*.report、buildlogtep.json、link.xml、release-info.json、UPLOAD_ORDER.txt。");
        builder.AppendLine();
        builder.AppendLine("本次建议上传文件：");

        foreach (ReleaseFileInfo file in files.Where(item => item.publishToCdn))
            builder.AppendLine($"[{file.uploadPhase}] {file.relativePath}  SHA256={file.sha256}");

        return builder.ToString();
    }

    private static IEnumerable<string> GetExistingVersionDirectories()
    {
        string packageRoot = Path.Combine(GetProjectRoot(), BundleOutputRootName, SupportedTarget.ToString(), PackageName);
        if (Directory.Exists(packageRoot))
        {
            foreach (string directory in Directory.EnumerateDirectories(packageRoot))
                yield return directory;
        }

        string stagingRoot = Path.Combine(GetProjectRoot(), StagingRootName);
        if (Directory.Exists(stagingRoot))
        {
            foreach (string directory in Directory.EnumerateDirectories(stagingRoot, "*", SearchOption.AllDirectories))
                yield return directory;
        }
    }

    private static string GetBundleVersionDirectory(string packageVersion)
    {
        return Path.Combine(
            GetProjectRoot(),
            BundleOutputRootName,
            SupportedTarget.ToString(),
            PackageName,
            packageVersion);
    }

    internal static string GetStagingDirectory(string playerBaseline, string packageVersion)
    {
        return Path.Combine(GetProjectRoot(), StagingRootName, "PC", playerBaseline, packageVersion);
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static string ToAbsolutePath(string projectRelativePath)
    {
        return Path.GetFullPath(Path.Combine(GetProjectRoot(), projectRelativePath));
    }

    private static string MakeRelativePath(string root, string path)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Uri rootUri = new Uri(normalizedRoot);
        Uri pathUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }

    private static void EnsurePathIsInside(string path, string expectedRoot)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullRoot = Path.GetFullPath(expectedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("待发布目录超出了项目 ReleaseStaging 范围：" + fullPath);
    }

    private static void RequireFile(string projectRelativePath, string displayName)
    {
        RequireAbsoluteFile(ToAbsolutePath(projectRelativePath), displayName);
    }

    private static void RequireAbsoluteFile(string path, string displayName)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"缺少{displayName}：{path}", path);
    }

    private static bool FilesHaveSameHash(string first, string second)
    {
        return string.Equals(CalculateSha256(first), CalculateSha256(second), StringComparison.OrdinalIgnoreCase);
    }

    private static string CalculateSha256(string path)
    {
        using (var stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
        {
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
