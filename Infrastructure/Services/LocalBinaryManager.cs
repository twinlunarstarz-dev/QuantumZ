using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using Microsoft.Maui.Storage;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

public interface ILocalBinaryManager
{
    Task<string> EnsureBinaryAsync(string binaryId, CancellationToken ct = default);
    bool IsBinaryInstalled(string binaryId);
}

public class LocalBinaryManager(IDebugLogger debugLogger) : ILocalBinaryManager
{
    private readonly HttpClient _httpClient = CreateHttpClient();
    
    // Mapping of binaries to their trusted download sources (MIT/Apache licensed versions).
    // llama.cpp Android releases are published as versioned tarballs, not as a stable
    // standalone `llama-server-android-arm64` asset. Resolve the latest release via
    // the GitHub API and extract the required executable from the archive.
    private static readonly Dictionary<string, List<BinarySource>> BinarySources = new()
    {
        {
            "llama-server", new List<BinarySource>
            {
                BinarySource.GitHubLatest("ggml-org/llama.cpp", "bin-android-arm64.tar.gz", "llama-server")
            }
        },
        {
            "piper", new List<BinarySource>
            {
                BinarySource.Direct("https://github.com/rhasspy/piper/releases/latest/download/piper-android-arm64"),
                BinarySource.Direct("https://github.com/rhasspy/piper/releases/latest/download/piper_android_arm64")
            }
        },
        {
            "whisper-cpp", new List<BinarySource>
            {
                BinarySource.Direct("https://github.com/ggml-org/whisper.cpp/releases/latest/download/whisper-android-arm64"),
                BinarySource.Direct("https://github.com/ggerganov/whisper.cpp/releases/latest/download/whisper-android-arm64")
            }
        }
    };

    public bool IsBinaryInstalled(string binaryId)
    {
        if (TryGetPackagedBinaryPath(binaryId) is not null)
            return true;

        if (IsDownloadedExecutionBlockedOnAndroid())
            return false;

        var path = GetBinaryPath(binaryId);
        return File.Exists(path);
    }

    public async Task<string> EnsureBinaryAsync(string binaryId, CancellationToken ct = default)
    {
        if (!BinarySources.TryGetValue(binaryId, out var urls))
        {
            throw new ArgumentException($"No source defined for binary: {binaryId}");
        }

        var path = GetBinaryPath(binaryId);

        if (TryGetPackagedBinaryPath(binaryId) is { } packagedPath)
        {
            debugLogger.Log("LocalBinaryManager", $"Using packaged binary {binaryId} at {packagedPath}", LogLevel.Info);
            return packagedPath;
        }

        if (IsDownloadedExecutionBlockedOnAndroid())
        {
            TryDelete(path);
            throw new PlatformNotSupportedException(
                $"Android 10+ blocks executing downloaded binaries from app-writable storage. " +
                $"Package {binaryId} as a native library in the APK or configure an external OpenAI-compatible endpoint instead.");
        }

        if (IsBinaryInstalled(binaryId))
        {
            SetExecutablePermission(path);
            debugLogger.Log("LocalBinaryManager", $"Binary {binaryId} already installed at {path}", LogLevel.Info);
            return path;
        }

        const int maxRetries = 3;
        Exception? lastError = null;
        for (int attempt = 0; attempt < urls.Count; attempt++)
        {
            var source = urls[attempt];
            try
            {
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        debugLogger.Log("LocalBinaryManager", $"Installing binary {binaryId} from {source.Description} (Attempt {retry + 1}/{maxRetries})...", LogLevel.Info);
                        await InstallFromSourceAsync(source, path, ct);
                        if (!File.Exists(path) || new FileInfo(path).Length == 0)
                            throw new InvalidOperationException($"Downloaded {binaryId} was empty or missing.");

                        SetExecutablePermission(path);

                        debugLogger.Log("LocalBinaryManager", $"Successfully installed {binaryId} at {path}", LogLevel.Info);
                        return path;
                    }
                    catch (Exception ex) when ((ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or InvalidOperationException) && retry < maxRetries - 1)
                    {
                        lastError = ex;
                        TryDelete(path);
                        debugLogger.Log("LocalBinaryManager", $"Retryable error installing {binaryId}: {ex.Message}. Retrying in {(retry + 1) * 2}s...", LogLevel.Warning);
                        await Task.Delay(TimeSpan.FromSeconds((retry + 1) * 2), ct);
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
                TryDelete(path);
                debugLogger.Log("LocalBinaryManager", $"Source {source.Description} failed for {binaryId}: {ex.Message}", LogLevel.Warning);
                if (attempt == urls.Count - 1) throw; // Last mirror failed, propagate exception
            }
        }

        throw new InvalidOperationException($"All sources failed for binary: {binaryId}", lastError);
    }

    private async Task InstallFromSourceAsync(BinarySource source, string path, CancellationToken ct)
    {
        var url = source.Url ?? await ResolveGitHubLatestAssetAsync(source, ct);
        if (url.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractExecutableFromTarGzAsync(url, source.EntryName ?? Path.GetFileNameWithoutExtension(path), path, ct);
            return;
        }

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(path);
        await input.CopyToAsync(output, ct);
    }

    private async Task<string> ResolveGitHubLatestAssetAsync(BinarySource source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source.GitHubRepo) || string.IsNullOrWhiteSpace(source.AssetContains))
            throw new InvalidOperationException("GitHub binary source is incomplete.");

        var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(
            $"https://api.github.com/repos/{source.GitHubRepo}/releases/latest",
            ct) ?? throw new InvalidOperationException($"No latest release metadata returned for {source.GitHubRepo}.");

        var asset = release.Assets.FirstOrDefault(candidate =>
            candidate.Name.Contains(source.AssetContains, StringComparison.OrdinalIgnoreCase));

        return asset?.BrowserDownloadUrl
            ?? throw new InvalidOperationException($"No release asset containing '{source.AssetContains}' found for {source.GitHubRepo}.");
    }

    private async Task ExtractExecutableFromTarGzAsync(string url, string entryName, string path, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var download = await response.Content.ReadAsStreamAsync(ct);
        await using var gzip = new GZipStream(download, CompressionMode.Decompress);
        using var reader = new TarReader(gzip, leaveOpen: false);

        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not TarEntryType.RegularFile || entry.DataStream is null)
                continue;

            var normalized = entry.Name.Replace('\\', '/');
            if (!string.Equals(Path.GetFileName(normalized), entryName, StringComparison.OrdinalIgnoreCase))
                continue;

            await using var output = File.Create(path);
            await entry.DataStream.CopyToAsync(output, ct);
            return;
        }

        throw new InvalidDataException($"Archive did not contain executable '{entryName}'.");
    }

    private string GetBinaryPath(string binaryId)
    {
        // Use internal app storage for binaries
        var baseDir = FileSystem.AppDataDirectory;
        var binDir = Path.Combine(baseDir, "binaries");
        Directory.CreateDirectory(binDir);
        return Path.Combine(binDir, binaryId);
    }

    private void SetExecutablePermission(string path)
    {
        try
        {
            // On Android, we use shell to chmod +x the file in internal storage
            var runtime = Java.Lang.Runtime.GetRuntime();
            var process = runtime.Exec(new string[] { "sh", "-c", $"chmod 700 {QuoteShellArg(path)}" });
            var exitCode = process.WaitFor();
            if (exitCode != 0)
            {
                debugLogger.Log("LocalBinaryManager", $"chmod returned exit code {exitCode} for {path}", LogLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            debugLogger.Log("LocalBinaryManager", $"Warning: Could not set executable permission for {path}: {ex.Message}. This may cause execution failure on Android 10+.", LogLevel.Warning);
        }
    }

    private static string QuoteShellArg(string value) => $"'{value.Replace("'", "'\\''")}'";

    private static string? TryGetPackagedBinaryPath(string binaryId)
    {
#if ANDROID
        var nativeLibraryDir = global::Android.App.Application.Context.ApplicationInfo?.NativeLibraryDir;
        if (string.IsNullOrWhiteSpace(nativeLibraryDir) || !Directory.Exists(nativeLibraryDir))
            return null;

        var candidates = new[]
        {
            $"lib{binaryId}.so",
            $"lib{binaryId.Replace("-", "_", StringComparison.Ordinal)}.so",
            binaryId
        };

        foreach (var candidate in candidates)
        {
            var candidatePath = Path.Combine(nativeLibraryDir, candidate);
            if (File.Exists(candidatePath))
                return candidatePath;
        }
#endif
        return null;
    }

    private static bool IsDownloadedExecutionBlockedOnAndroid()
    {
#if ANDROID
        return global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Q;
#else
        return false;
#endif
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QuantumZ/1.0");
        return client;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record BinarySource(string? Url, string? GitHubRepo, string? AssetContains, string? EntryName)
    {
        public string Description => Url ?? $"latest GitHub release {GitHubRepo} asset containing '{AssetContains}'";

        public static BinarySource Direct(string url) => new(url, null, null, null);

        public static BinarySource GitHubLatest(string repo, string assetContains, string entryName) => new(null, repo, assetContains, entryName);
    }

    private sealed record GitHubRelease([property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
