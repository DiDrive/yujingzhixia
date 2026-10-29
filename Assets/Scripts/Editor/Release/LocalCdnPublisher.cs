using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 将 ReleaseStaging 中已校验的资源安全发布到本机目录型 CDN。
/// Bundle/Manifest 先发布，DefaultPackage.version 最后发布。
/// </summary>
public static class LocalCdnPublisher
{
    private const string PackageName = RemoteUpdateBuildPipeline.PackageName;

    public sealed class PublishOptions
    {
        public string stagingDirectory;
        public string cdnPhysicalRoot;
        public string cdnBaseUrl;
        public string platformFolder = "PC";
        public string playerBaseline = RemoteUpdateBuildPipeline.DefaultBaseline;
        public bool verifyOverHttp = true;
    }

    public sealed class PublishResult
    {
        public string PackageVersion { get; internal set; }
        public string TargetDirectory { get; internal set; }
        public int CopiedFileCount { get; internal set; }
        public int SkippedFileCount { get; internal set; }
        public bool HttpVerified { get; internal set; }
    }

    [Serializable]
    private sealed class PublishRecord
    {
        public string packageVersion;
        public string publishedAtUtc;
        public string sourceDirectory;
        public string targetDirectory;
        public string targetUrl;
        public int copiedFileCount;
        public int skippedFileCount;
        public bool httpVerified;
    }

    public static string FindLatestStagingDirectory(string platformFolder, string playerBaseline)
    {
        string root = Path.Combine(GetProjectRoot(), "ReleaseStaging", platformFolder, playerBaseline);
        if (!Directory.Exists(root))
            return string.Empty;

        return Directory
            .EnumerateDirectories(root)
            .Where(IsValidStagingDirectory)
            .OrderByDescending(directory => Directory.GetLastWriteTimeUtc(directory))
            .FirstOrDefault() ?? string.Empty;
    }

    public static string ReadPackageVersion(string stagingDirectory)
    {
        if (string.IsNullOrWhiteSpace(stagingDirectory))
            return string.Empty;

        string versionFile = Path.Combine(stagingDirectory, PackageName + ".version");
        return File.Exists(versionFile) ? File.ReadAllText(versionFile, Encoding.UTF8).Trim() : string.Empty;
    }

    public static string GetTargetDirectory(PublishOptions options)
    {
        if (options == null || string.IsNullOrWhiteSpace(options.cdnPhysicalRoot))
            return string.Empty;

        return Path.GetFullPath(Path.Combine(
            options.cdnPhysicalRoot.Trim(),
            options.platformFolder.Trim(),
            options.playerBaseline.Trim()));
    }

    public static string GetTargetUrl(PublishOptions options)
    {
        if (options == null || string.IsNullOrWhiteSpace(options.cdnBaseUrl))
            return string.Empty;

        return options.cdnBaseUrl.Trim().TrimEnd('/') + "/" +
               Uri.EscapeDataString(options.platformFolder.Trim()) + "/" +
               Uri.EscapeDataString(options.playerBaseline.Trim());
    }

    public static PublishResult Publish(PublishOptions options, Action<string, float> reportProgress = null)
    {
        ValidateOptions(options);

        string stagingDirectory = Path.GetFullPath(options.stagingDirectory);
        string packageVersion = ReadPackageVersion(stagingDirectory);
        string targetDirectory = GetTargetDirectory(options);
        string targetUrl = GetTargetUrl(options);

        string manifestName = $"{PackageName}_{packageVersion}.bytes";
        string manifestHashName = $"{PackageName}_{packageVersion}.hash";
        string versionName = PackageName + ".version";

        var publishFiles = Directory
            .EnumerateFiles(stagingDirectory, "*.bundle", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        publishFiles.Add(Path.Combine(stagingDirectory, manifestName));
        publishFiles.Add(Path.Combine(stagingDirectory, manifestHashName));

        Directory.CreateDirectory(targetDirectory);

        int copied = 0;
        int skipped = 0;
        for (int index = 0; index < publishFiles.Count; index++)
        {
            string source = publishFiles[index];
            string relativePath = MakeRelativePath(stagingDirectory, source);
            string destination = Path.Combine(targetDirectory, relativePath);
            reportProgress?.Invoke(
                $"发布资源文件 {index + 1}/{publishFiles.Count}\n{relativePath}",
                0.05f + 0.65f * (index + 1f) / publishFiles.Count);

            CopyImmutableFile(source, destination, ref copied, ref skipped);
        }

        if (options.verifyOverHttp)
        {
            reportProgress?.Invoke("通过 HTTP 校验 Bundle 和 Manifest", 0.75f);
            foreach (string source in publishFiles)
            {
                string relativePath = MakeRelativePath(stagingDirectory, source).Replace('\\', '/');
                VerifyRemoteFile(BuildFileUrl(targetUrl, relativePath), new FileInfo(source).Length);
            }
        }

        reportProgress?.Invoke("最后发布 DefaultPackage.version", 0.9f);
        string sourceVersionFile = Path.Combine(stagingDirectory, versionName);
        string destinationVersionFile = Path.Combine(targetDirectory, versionName);
        byte[] previousVersionBytes = File.Exists(destinationVersionFile)
            ? File.ReadAllBytes(destinationVersionFile)
            : null;

        try
        {
            CopyVersionPointerLast(sourceVersionFile, destinationVersionFile);
            copied++;

            if (options.verifyOverHttp)
                VerifyRemoteVersion(BuildFileUrl(targetUrl, versionName), packageVersion);
        }
        catch
        {
            RestorePreviousVersionPointer(destinationVersionFile, previousVersionBytes);
            throw;
        }

        var result = new PublishResult
        {
            PackageVersion = packageVersion,
            TargetDirectory = targetDirectory,
            CopiedFileCount = copied,
            SkippedFileCount = skipped,
            HttpVerified = options.verifyOverHttp
        };

        WritePublishRecord(stagingDirectory, targetUrl, result);
        reportProgress?.Invoke("发布完成", 1f);
        return result;
    }

    private static void ValidateOptions(PublishOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.stagingDirectory))
            throw new ArgumentException("请选择待发布版本目录。");
        if (string.IsNullOrWhiteSpace(options.cdnPhysicalRoot))
            throw new ArgumentException("请选择 CDN 物理根目录（对应 URL 中的 /CDN）。");
        if (string.IsNullOrWhiteSpace(options.platformFolder))
            throw new ArgumentException("平台目录不能为空。");
        if (string.IsNullOrWhiteSpace(options.playerBaseline))
            throw new ArgumentException("玩家大版本目录不能为空。");
        if (options.verifyOverHttp && string.IsNullOrWhiteSpace(options.cdnBaseUrl))
            throw new ArgumentException("启用 HTTP 校验时，CDN 根地址不能为空。");

        string stagingDirectory = Path.GetFullPath(options.stagingDirectory);
        string stagingRoot = Path.Combine(GetProjectRoot(), "ReleaseStaging");
        EnsurePathIsInside(stagingDirectory, stagingRoot, "待发布版本目录必须位于项目 ReleaseStaging 内。");

        ValidateSingleFolderName(options.platformFolder, "平台目录");
        ValidateSingleFolderName(options.playerBaseline, "玩家大版本目录");

        if (!IsValidStagingDirectory(stagingDirectory))
            throw new InvalidDataException("所选目录不是完整的 YooAsset 待发布版本目录。");

        string packageVersion = ReadPackageVersion(stagingDirectory);
        RequireFile(Path.Combine(stagingDirectory, $"{PackageName}_{packageVersion}.bytes"), "Manifest");
        RequireFile(Path.Combine(stagingDirectory, $"{PackageName}_{packageVersion}.hash"), "Manifest Hash");

        if (!Directory.EnumerateFiles(stagingDirectory, "*.bundle", SearchOption.AllDirectories).Any())
            throw new InvalidDataException("待发布目录中没有 .bundle 文件。");

        string targetDirectory = GetTargetDirectory(options);
        EnsurePathIsInside(
            targetDirectory,
            Path.GetFullPath(options.cdnPhysicalRoot),
            "CDN 目标目录超出了所选 CDN 物理根目录。");
        string stagingWithSeparator = stagingDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string targetWithSeparator = targetDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (targetWithSeparator.StartsWith(stagingWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CDN 目标目录不能位于当前待发布版本目录内部。");
    }

    private static void ValidateSingleFolderName(string value, string displayName)
    {
        string name = value.Trim();
        if (name == "." || name == ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains("/") || name.Contains("\\"))
        {
            throw new ArgumentException(displayName + "只能填写单个文件夹名称，不能包含路径符号。");
        }
    }

    private static bool IsValidStagingDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        string versionFile = Path.Combine(directory, PackageName + ".version");
        if (!File.Exists(versionFile))
            return false;

        string version = File.ReadAllText(versionFile, Encoding.UTF8).Trim();
        return !string.IsNullOrWhiteSpace(version) &&
               File.Exists(Path.Combine(directory, $"{PackageName}_{version}.bytes")) &&
               File.Exists(Path.Combine(directory, $"{PackageName}_{version}.hash"));
    }

    private static void CopyImmutableFile(
        string source,
        string destination,
        ref int copied,
        ref int skipped)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException());

        if (File.Exists(destination))
        {
            if (FilesHaveSameHash(source, destination))
            {
                skipped++;
                return;
            }

            throw new IOException(
                "CDN 已存在同名但内容不同的不可变文件，已停止发布，未更新版本指针：\n" + destination);
        }

        string temporaryFile = destination + ".uploading";
        try
        {
            File.Copy(source, temporaryFile, true);
            if (!FilesHaveSameHash(source, temporaryFile))
                throw new IOException("复制后 SHA256 不一致：" + destination);
            File.Move(temporaryFile, destination);
            copied++;
        }
        finally
        {
            if (File.Exists(temporaryFile))
                File.Delete(temporaryFile);
        }
    }

    private static void CopyVersionPointerLast(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException());
        string temporaryFile = destination + ".uploading";
        try
        {
            File.Copy(source, temporaryFile, true);
            if (!FilesHaveSameHash(source, temporaryFile))
                throw new IOException("版本指针复制后的 SHA256 不一致。");
            File.Copy(temporaryFile, destination, true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
                File.Delete(temporaryFile);
        }
    }

    private static void RestorePreviousVersionPointer(string destination, byte[] previousBytes)
    {
        try
        {
            if (previousBytes == null)
            {
                if (File.Exists(destination))
                    File.Delete(destination);
            }
            else
            {
                File.WriteAllBytes(destination, previousBytes);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("恢复旧版本指针失败，请立即检查 CDN 上的 DefaultPackage.version：" + exception);
        }
    }

    private static void VerifyRemoteFile(string url, long expectedLength)
    {
        var request = (HttpWebRequest)WebRequest.Create(AppendCacheBuster(url));
        request.Method = "HEAD";
        request.Timeout = 10000;
        request.ReadWriteTimeout = 10000;
        request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

        using (var response = (HttpWebResponse)request.GetResponse())
        {
            int status = (int)response.StatusCode;
            if (status < 200 || status >= 300)
                throw new WebException($"HTTP 校验失败：{status} {url}");
            if (response.ContentLength >= 0 && response.ContentLength != expectedLength)
                throw new InvalidDataException(
                    $"HTTP 文件大小不一致：{url}\n期望 {expectedLength}，实际 {response.ContentLength}");
        }
    }

    private static void VerifyRemoteVersion(string url, string expectedVersion)
    {
        var request = (HttpWebRequest)WebRequest.Create(AppendCacheBuster(url));
        request.Method = "GET";
        request.Timeout = 10000;
        request.ReadWriteTimeout = 10000;
        request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

        using (var response = (HttpWebResponse)request.GetResponse())
        using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
        {
            string actualVersion = reader.ReadToEnd().Trim();
            if (!string.Equals(actualVersion, expectedVersion, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"CDN 版本指针校验失败。期望 {expectedVersion}，实际 {actualVersion}。");
        }
    }

    private static string AppendCacheBuster(string url)
    {
        return url + (url.Contains("?") ? "&" : "?") + "publishCheck=" + DateTime.UtcNow.Ticks;
    }

    private static string BuildFileUrl(string baseUrl, string relativePath)
    {
        string encodedPath = string.Join(
            "/",
            relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return baseUrl.TrimEnd('/') + "/" + encodedPath;
    }

    private static void WritePublishRecord(string stagingDirectory, string targetUrl, PublishResult result)
    {
        var record = new PublishRecord
        {
            packageVersion = result.PackageVersion,
            publishedAtUtc = DateTime.UtcNow.ToString("O"),
            sourceDirectory = stagingDirectory,
            targetDirectory = result.TargetDirectory,
            targetUrl = targetUrl,
            copiedFileCount = result.CopiedFileCount,
            skippedFileCount = result.SkippedFileCount,
            httpVerified = result.HttpVerified
        };

        File.WriteAllText(
            Path.Combine(stagingDirectory, "publish-result.json"),
            JsonUtility.ToJson(record, true),
            new UTF8Encoding(false));
    }

    private static string MakeRelativePath(string root, string path)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Uri.UnescapeDataString(
                new Uri(normalizedRoot).MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static void EnsurePathIsInside(string path, string root, string errorMessage)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(errorMessage);
    }

    private static void RequireFile(string path, string displayName)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("缺少 " + displayName + " 文件：" + path, path);
    }

    private static bool FilesHaveSameHash(string first, string second)
    {
        return string.Equals(CalculateSha256(first), CalculateSha256(second), StringComparison.OrdinalIgnoreCase);
    }

    private static string CalculateSha256(string path)
    {
        using (var stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
