using System.Net.Http.Headers;
using Microsoft.Maui.Storage;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Core.Models.Settings;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Performs truthful local setup readiness checks and catalog-backed model downloads.
/// </summary>
public sealed class LocalSetupService(
    ISettingsService settingsService,
    INativeRuntimeService nativeRuntimeService,
    IDebugLogger logger) : ILocalSetupService, IDisposable
{
    private const string LocalSetupProviderName = "Local Setup";

    private readonly HttpClient _httpClient = CreateHttpClient();

    public async ValueTask<IReadOnlyList<SetupChecklistItem>> GetChecklistAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runtimeAvailability = await GetRuntimeAvailabilityAsync(cancellationToken);
        IReadOnlyList<SetupChecklistItem> items = [.. ModelCatalogService.Catalog.Select(entry => ToChecklistItem(entry, runtimeAvailability))];
        return items;
    }

    public async ValueTask<InstallResult> InstallAsync(
        string itemId,
        string? authToken = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var entry = ModelCatalogService.Catalog.FirstOrDefault(candidate => string.Equals(candidate.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return new InstallResult(false, $"Unknown local setup catalog item '{itemId}'.", ModelInstallStatus.Failed);

        if (IsBuiltInFallback(entry))
        {
            ConfigureInstalledEntry(entry, localPath: null);
            return new InstallResult(true, $"{entry.DisplayName} selected as a release-safe fallback.", ModelInstallStatus.Installed);
        }

        if (entry.RequiresAuth && string.IsNullOrWhiteSpace(authToken))
        {
            return new InstallResult(
                false,
                $"{entry.DisplayName} requires a temporary Hugging Face token and prior license acceptance. Token was not stored; provide one or use Remote mode.",
                ModelInstallStatus.RequiresAuth);
        }

        if (string.IsNullOrWhiteSpace(entry.DownloadUrl))
        {
            var status = entry.RequiresAuth ? ModelInstallStatus.RequiresAuth : ModelInstallStatus.Failed;
            return new InstallResult(
                false,
                $"{entry.DisplayName} does not have a verified direct download URL in this release catalog. {entry.Notes}",
                status);
        }

        var targetPath = GetTargetPath(entry);
        if (File.Exists(targetPath))
        {
            ConfigureInstalledEntry(entry, targetPath);
            progress?.Report(1.0d);
            return await BuildInstalledResultAsync(entry, targetPath, cancellationToken);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? FileSystem.AppDataDirectory);
            var tempPath = Path.Combine(Path.GetDirectoryName(targetPath) ?? FileSystem.AppDataDirectory, $".{entry.FileName}.{Guid.NewGuid():N}.tmp");

            await DownloadToFileAsync(entry, tempPath, authToken, progress, cancellationToken);
            var downloadedBytes = new FileInfo(tempPath).Length;
            if (downloadedBytes <= 0)
                throw new InvalidDataException("Downloaded model file was empty.");

            if (File.Exists(targetPath))
                File.Delete(targetPath);

            File.Move(tempPath, targetPath);
            ConfigureInstalledEntry(entry, targetPath);
            progress?.Report(1.0d);

            logger.Log("LocalSetup", $"Installed {entry.Id} to {targetPath} ({downloadedBytes} bytes).", LogLevel.Info);
            return await BuildInstalledResultAsync(entry, targetPath, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or OperationCanceledException)
        {
            logger.Log("LocalSetup", $"Installation failed for {entry.Id}: {ex.Message}", LogLevel.Error);
            return new InstallResult(false, $"Failed to install {entry.DisplayName}: {ex.Message}", ModelInstallStatus.Failed);
        }
    }

    public async ValueTask<bool> AreRequiredAssetsReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sttRuntimeReady = await nativeRuntimeService.IsRuntimeAvailableAsync(NativeRuntimeKind.Stt, cancellationToken);
        var llmRuntimeReady = await nativeRuntimeService.IsRuntimeAvailableAsync(NativeRuntimeKind.Llm, cancellationToken);
        var sttReady = HasReadyWhisperStt(sttRuntimeReady);
        var vadReady = IsRmsVadSelected() || HasInstalledEntry("silero-vad-v5-onnx");
        var ttsReady = IsAndroidTtsSelected();
        var llmReady = HasReadyLocalLlm(llmRuntimeReady);
        var ready = sttReady && vadReady && ttsReady && llmReady;

        if (!ready)
        {
            logger.Log(
                "LocalSetup",
                $"Local setup is not ready. STT={sttReady} (runtime={sttRuntimeReady}), VAD={vadReady}, TTS={ttsReady} (Android built-in fallback selected={IsAndroidTtsSelected()}), LLM={llmReady} (runtime={llmRuntimeReady}). Packaged native runtimes are required for local LLM/STT providers; Piper is optional.",
                LogLevel.Warning);
        }

        return ready;
    }

    public void Dispose() => _httpClient.Dispose();

    private SetupChecklistItem ToChecklistItem(ModelCatalogEntry entry, IReadOnlyDictionary<NativeRuntimeKind, bool> runtimeAvailability)
    {
        var status = GetStatus(entry, runtimeAvailability);
        var installed = status == ModelInstallStatus.Installed;

        return new SetupChecklistItem
        {
            Id = entry.Id,
            Label = BuildLabel(entry),
            Capability = entry.Capability,
            IsRequired = entry.IsRequiredForLocalSetup,
            IsInstalled = installed,
            Progress = installed ? 1.0d : 0.0d,
            Status = status,
            StatusText = BuildStatusText(entry, status)
        };
    }

    private ModelInstallStatus GetStatus(ModelCatalogEntry entry, IReadOnlyDictionary<NativeRuntimeKind, bool> runtimeAvailability)
    {
        if (IsBuiltInFallback(entry))
            return ModelInstallStatus.Installed;

        var fileInstalled = HasInstalledEntry(entry.Id);
        if (!fileInstalled)
            return entry.RequiresAuth ? ModelInstallStatus.RequiresAuth : ModelInstallStatus.NotInstalled;

        return RequiresMissingRuntime(entry, runtimeAvailability) ? ModelInstallStatus.RequiresRuntime : ModelInstallStatus.Installed;
    }

    private string BuildStatusText(ModelCatalogEntry entry, ModelInstallStatus status) => status switch
    {
        ModelInstallStatus.Installed when IsBuiltInFallback(entry) => "Release-safe built-in fallback is available.",
        ModelInstallStatus.Installed => "Installed and runtime requirement is satisfied.",
        ModelInstallStatus.RequiresAuth => "Requires user-provided token and license acceptance; token is not stored.",
        ModelInstallStatus.RequiresRuntime => "Model file exists, but the required packaged native runtime is missing.",
        ModelInstallStatus.Downloading => "Downloading...",
        ModelInstallStatus.Failed => "Install failed.",
        _ when string.IsNullOrWhiteSpace(entry.DownloadUrl) => "Not installed; no verified direct download URL is enabled for this release.",
        _ => FormatDownloadStatus(entry)
    };

    private static string BuildLabel(ModelCatalogEntry entry)
    {
        var required = entry.IsRequiredForLocalSetup ? "Required" : "Optional";
        var provisional = entry.IsProvisional ? " · provisional" : string.Empty;
        return $"{entry.DisplayName} ({entry.Capability}, {required}{provisional})";
    }

    private static string FormatDownloadStatus(ModelCatalogEntry entry)
    {
        if (entry.ExpectedBytes is not { } bytes)
            return "Ready to download.";

        var mib = bytes / 1024d / 1024d;
        return $"Ready to download (~{mib:0} MB).";
    }

    private static bool IsBuiltInFallback(ModelCatalogEntry entry) =>
        string.Equals(entry.Id, "android-tts-built-in", StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.Id, "rms-vad-built-in", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresMissingRuntime(ModelCatalogEntry entry, IReadOnlyDictionary<NativeRuntimeKind, bool> runtimeAvailability) =>
        TryGetRequiredRuntimeKind(entry) is { } kind
        && (!runtimeAvailability.TryGetValue(kind, out var available) || !available);

    private async Task DownloadToFileAsync(
        ModelCatalogEntry entry,
        string tempPath,
        string? authToken,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, entry.DownloadUrl);
            if (!string.IsNullOrWhiteSpace(authToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken.Trim());

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? entry.ExpectedBytes ?? -1L;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);

            var buffer = new byte[128 * 1024];
            long totalRead = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;
                if (totalBytes > 0)
                    progress?.Report(Math.Clamp((double)totalRead / totalBytes, 0.0d, 1.0d));
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private async ValueTask<InstallResult> BuildInstalledResultAsync(ModelCatalogEntry entry, string targetPath, CancellationToken cancellationToken)
    {
        if (await RequiresMissingRuntimeAsync(entry, cancellationToken))
        {
            return new InstallResult(
                false,
                $"{entry.DisplayName} was downloaded, but the required packaged native runtime is missing. Android 10+ cannot safely execute downloaded binaries.",
                ModelInstallStatus.RequiresRuntime,
                targetPath);
        }

        return new InstallResult(true, $"{entry.DisplayName} installed.", ModelInstallStatus.Installed, targetPath);
    }

    private async ValueTask<bool> RequiresMissingRuntimeAsync(ModelCatalogEntry entry, CancellationToken cancellationToken) =>
        TryGetRequiredRuntimeKind(entry) is { } kind
        && !await nativeRuntimeService.IsRuntimeAvailableAsync(kind, cancellationToken);

    private void ConfigureInstalledEntry(ModelCatalogEntry entry, string? localPath)
    {
        switch (entry.Capability)
        {
            case ProviderCapability.Stt when !string.IsNullOrWhiteSpace(localPath):
                settingsService.UseOnDeviceStt = true;
                settingsService.WhisperModelPath = localPath;
                settingsService.SttSettings = BuildLocalProviderSettings(entry.Id, localPath);
                settingsService.PipelineSettings = settingsService.PipelineSettings with
                {
                    Stt = BuildLocalStage(localPath)
                };
                break;

            case ProviderCapability.Vad when string.Equals(entry.Id, "rms-vad-built-in", StringComparison.OrdinalIgnoreCase):
                settingsService.VadSettings = BuildFallbackProviderSettings("Built-In RMS VAD", "rms-vad-built-in");
                settingsService.PipelineSettings = settingsService.PipelineSettings with
                {
                    Vad = new StageSettings { Enabled = true, Mode = ModelMode.BuiltIn }
                };
                break;

            case ProviderCapability.Vad when !string.IsNullOrWhiteSpace(localPath):
                settingsService.VadSettings = BuildLocalProviderSettings(entry.Id, localPath);
                settingsService.PipelineSettings = settingsService.PipelineSettings with
                {
                    Vad = BuildLocalStage(localPath)
                };
                break;

            case ProviderCapability.Tts when string.Equals(entry.Id, "android-tts-built-in", StringComparison.OrdinalIgnoreCase):
                settingsService.UseLocalTts = false;
                settingsService.TtsSettings = BuildFallbackProviderSettings("Android Built-In TTS", "builtin.android-tts");
                settingsService.PipelineSettings = settingsService.PipelineSettings with
                {
                    Tts = new StageSettings { Enabled = true, Mode = ModelMode.BuiltIn }
                };
                break;

            case ProviderCapability.Tts when !string.IsNullOrWhiteSpace(localPath):
                settingsService.UseLocalTts = true;
                settingsService.TtsSettings = BuildLocalProviderSettings(entry.Id, localPath);
                settingsService.PipelineSettings = settingsService.PipelineSettings with
                {
                    Tts = BuildLocalStage(localPath)
                };
                break;

            case ProviderCapability.Llm when !string.IsNullOrWhiteSpace(localPath):
                settingsService.LlmSettings = new ServiceProviderSettings(
                    LocalSetupProviderName,
                    [new ProviderConfig(LocalSetupProviderName, ModelRegistry.LocalLlamaBaseUrl, localPath)]);
                settingsService.PipelineSettings = settingsService.PipelineSettings with
                {
                    Llm = BuildLocalStage(localPath)
                };
                break;
        }
    }

    private bool HasReadyWhisperStt(bool runtimeAvailable)
    {
        var configured = settingsService.WhisperModelPath?.Trim();
        var modelExists = !string.IsNullOrWhiteSpace(configured) && File.Exists(configured)
            || ModelCatalogService.Catalog
                .Where(entry => entry.Capability == ProviderCapability.Stt && entry.IsRequiredForLocalSetup)
                .Any(entry => HasInstalledEntry(entry.Id));

        return modelExists && runtimeAvailable;
    }

    private bool HasReadyLocalLlm(bool runtimeAvailable) =>
        runtimeAvailable
        && ResolveLocalLlmModelPath() is { } modelPath
        && File.Exists(modelPath);

    private async ValueTask<IReadOnlyDictionary<NativeRuntimeKind, bool>> GetRuntimeAvailabilityAsync(CancellationToken cancellationToken)
    {
        var statuses = await nativeRuntimeService.GetStatusesAsync(cancellationToken);
        return statuses.ToDictionary(status => status.Kind, status => status.IsPackaged);
    }

    private static NativeRuntimeKind? TryGetRequiredRuntimeKind(ModelCatalogEntry entry) => entry.Capability switch
    {
        ProviderCapability.Stt => NativeRuntimeKind.Stt,
        ProviderCapability.Llm => NativeRuntimeKind.Llm,
        ProviderCapability.Tts when entry.Id.Contains("piper", StringComparison.OrdinalIgnoreCase) => NativeRuntimeKind.Tts,
        _ => null
    };

    private bool IsRmsVadSelected() =>
        settingsService.PipelineSettings.Vad.Mode == ModelMode.BuiltIn
        || string.Equals(settingsService.GetActiveProvider("VAD")?.ModelId, "rms-vad-built-in", StringComparison.OrdinalIgnoreCase);

    private bool IsAndroidTtsSelected() =>
        settingsService.PipelineSettings.Tts.Mode == ModelMode.BuiltIn
        || string.Equals(settingsService.GetActiveProvider("TTS")?.ModelId, "builtin.android-tts", StringComparison.OrdinalIgnoreCase)
        || string.Equals(settingsService.GetActiveProvider("TTS")?.ModelId, "android-tts-built-in", StringComparison.OrdinalIgnoreCase);

    private string? ResolveLocalLlmModelPath()
    {
        var selected = settingsService.GetActiveProvider("LLM")?.ModelId?.Trim();
        if (!string.IsNullOrWhiteSpace(selected) && File.Exists(selected))
            return selected;

        var configured = settingsService.PipelineSettings.Llm.Local?.ModelPath?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var llmDirectory = Path.Combine(FileSystem.AppDataDirectory, "models", "llm");
        if (!Directory.Exists(llmDirectory))
            return null;

        return Directory.EnumerateFiles(llmDirectory, "*.gguf", SearchOption.AllDirectories)
            .OrderByDescending(path => Path.GetFileName(path).Contains("q4", StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private bool HasInstalledEntry(string entryId)
    {
        var entry = ModelCatalogService.Catalog.FirstOrDefault(candidate => string.Equals(candidate.Id, entryId, StringComparison.OrdinalIgnoreCase));
        return entry is not null && !string.IsNullOrWhiteSpace(entry.TargetRelativePath) && File.Exists(GetTargetPath(entry));
    }

    private static ServiceProviderSettings BuildLocalProviderSettings(string modelId, string localPath) => new(
        LocalSetupProviderName,
        [new ProviderConfig(LocalSetupProviderName, localPath, modelId)]);

    private static ServiceProviderSettings BuildFallbackProviderSettings(string providerName, string modelId) => new(
        providerName,
        [new ProviderConfig(providerName, string.Empty, modelId)]);

    private static StageSettings BuildLocalStage(string modelPath) => new()
    {
        Enabled = true,
        Mode = ModelMode.Local,
        Local = new LocalModelConfig { ModelPath = modelPath }
    };

    private static string GetTargetPath(ModelCatalogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.TargetRelativePath))
            return string.Empty;

        var basePath = Path.GetFullPath(FileSystem.AppDataDirectory);
        var targetPath = Path.GetFullPath(Path.Combine(basePath, entry.TargetRelativePath));
        if (!targetPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Catalog target path escapes app data: {entry.TargetRelativePath}");

        return targetPath;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QuantumZ/1.0");
        return client;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
