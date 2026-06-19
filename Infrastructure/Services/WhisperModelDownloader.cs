using System.Net.Http.Headers;
using QuantumZ.Core.Interfaces;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Downloads Whisper GGML models from Hugging Face if they are not present locally.
/// </summary>
public sealed class WhisperModelDownloader(HttpClient httpClient, ISettingsService settings)
{
    public const string DefaultModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";
    public const string DefaultModelFileName = "ggml-base.bin";

    /// <summary>
    /// Returns the effective local path for the whisper model.
    /// </summary>
    public string GetEffectiveModelPath()
    {
        var configured = settings.WhisperModelPath?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        // Determine filename based on selected model ID
        var fileName = settings.GetActiveProvider("STT")?.ModelId?.Trim() switch
        {
            "whisper-tiny" => "ggml-tiny.bin",
            "whisper-small" => "ggml-small.bin",
            "whisper-medium" => "ggml-medium.bin",
            _ => DefaultModelFileName
        };

        return Path.Combine(FileSystem.AppDataDirectory, "models", "stt", "whisper", fileName);
    }

    /// <summary>
    /// Returns true if the model file already exists locally.
    /// </summary>
    public bool ModelExists()
    {
        var path = GetEffectiveModelPath();
        return File.Exists(path);
    }

    /// <summary>
    /// Downloads the Whisper model if missing. Reports progress via the provided callback.
    /// </summary>
    public async Task<bool> EnsureModelAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var path = GetEffectiveModelPath();
        if (File.Exists(path))
            return true;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var modelUrl = settings.GetActiveProvider("STT")?.ModelId?.Trim() switch
        {
            "whisper-tiny" => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",
            "whisper-small" => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
            "whisper-medium" => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
            _ => DefaultModelUrl
        };

        using var response = await httpClient.GetAsync(modelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[8192];
        long totalRead = 0;
        int read;
        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;
            if (totalBytes > 0)
            {
                progress?.Report((double)totalRead / totalBytes);
            }
        }

        return true;
    }
}
