using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly HttpClient _httpClient = new();
    
    // Mapping of binaries to their trusted download sources (MIT/Apache licensed versions)
    private static readonly Dictionary<string, List<string>> BinarySources = new()
    {
        {
            "llama-server", new List<string> {
                "https://github.com/ggml-org/llama.cpp/releases/latest/download/llama-server-android-arm64",
                "https://github.com/ggerganov/llama.cpp/releases/latest/download/llama-server-android-arm64"
            }
        },
        {
            "piper", new List<string> {
                "https://github.com/rhasspy/piper/releases/latest/download/piper-android-arm64",
                "https://github.com/rhasspy/piper/releases/latest/download/piper_android_arm64"
            }
        },
        {
            "whisper-cpp", new List<string> {
                "https://github.com/ggml-org/whisper.cpp/releases/latest/download/whisper-android-arm64",
                "https://github.com/ggerganov/whisper.cpp/releases/latest/download/whisper-android-arm64"
            }
        }
    };

    public bool IsBinaryInstalled(string binaryId)
    {
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

        if (IsBinaryInstalled(binaryId))
        {
            SetExecutablePermission(path);
            debugLogger.Log("LocalBinaryManager", $"Binary {binaryId} already installed at {path}", LogLevel.Info);
            return path;
        }

        int maxRetries = 3;
        for (int attempt = 0; attempt < urls.Count; attempt++)
        {
            var url = urls[attempt];
            try
            {
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        debugLogger.Log("LocalBinaryManager", $"Downloading binary {binaryId} from {url} (Attempt {retry + 1}/{maxRetries})...", LogLevel.Info);
                        var data = await _httpClient.GetByteArrayAsync(url, ct);
                        await File.WriteAllBytesAsync(path, data, ct);

                        // Attempt to set execution permissions on Android
                        SetExecutablePermission(path);

                        debugLogger.Log("LocalBinaryManager", $"Successfully installed {binaryId} at {path}", LogLevel.Info);
                        return path;
                    }
                    catch (HttpRequestException ex) when (retry < maxRetries - 1)
                    {
                        debugLogger.Log("LocalBinaryManager", $"Retryable error downloading {binaryId}: {ex.Message}. Retrying in {(retry + 1) * 2}s...", LogLevel.Warning);
                        await Task.Delay(TimeSpan.FromSeconds((retry + 1) * 2), ct);
                    }
                }
            }
            catch (Exception ex)
            {
                debugLogger.Log("LocalBinaryManager", $"Source {url} failed for {binaryId}: {ex.Message}", LogLevel.Warning);
                if (attempt == urls.Count - 1) throw; // Last mirror failed, propagate exception
            }
        }

        throw new Exception($"All sources failed for binary: {binaryId}");
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
}
