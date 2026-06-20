using System.Text.Json;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Infrastructure.Native;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Routes to local Kokoro/Piper model assets when they are installed in app storage.
/// </summary>
public sealed class LocalAiTtsProvider(IModelRegistry modelRegistry, ISettingsService settings, IDebugLogger debugLogger, INativeRuntimeService nativeRuntimeService) : ITtsProvider, IDisposable
{
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private IntPtr _handle;
    private string? _loadedModelPath;
    private string? _loadedConfigPath;

    public ProviderDescriptor Descriptor { get; } = new(
        Id: "local.ai-tts",
        DisplayName: "Local AI TTS (Kokoro/Piper)",
        Capability: ProviderCapability.Tts,
        Location: ProviderLocation.Local);

    public bool IsReady => true;

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!await nativeRuntimeService.IsRuntimeAvailableAsync(NativeRuntimeKind.Tts, ct))
        {
            debugLogger.Log("LocalAiTtsProvider", "Local Piper TTS is unavailable because libquantumz_piper.so is not packaged for this Android ABI.", LogLevel.Warning);
            return false;
        }

        var selected = await modelRegistry.ResolvePreferredModelAsync(ProviderCapability.Tts, settings.GetActiveProvider("TTS")?.ModelId ?? "", ct);
        if (selected is not { Location: ProviderLocation.Local })
            return false;

        var voice = ResolvePiperVoiceFiles(selected.LocalPath);
        if (voice is not null)
            return true;

        debugLogger.Log("LocalAiTtsProvider", "Local Piper TTS is unavailable because a model .onnx and matching config .json were not found.", LogLevel.Warning);
        return false;
    }

    public async ValueTask<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        var selected = await modelRegistry.ResolvePreferredModelAsync(ProviderCapability.Tts, settings.GetActiveProvider("TTS")?.ModelId ?? "", ct)
            ?? throw new InvalidOperationException("No local TTS model is available.");

        if (selected.Location != ProviderLocation.Local)
            throw new InvalidOperationException("The selected TTS model is not local.");

        var voice = ResolvePiperVoiceFiles(selected.LocalPath)
            ?? throw new InvalidOperationException("Local Piper requires a model .onnx file and a matching config .json file.");

        try
        {
            var handle = await EnsureHandleAsync(voice.ModelPath, voice.ConfigPath, ct);
            debugLogger.Log("LocalAiTtsProvider", $"Synthesizing text using native Piper voice '{Path.GetFileName(voice.ModelPath)}'.", LogLevel.Info);
            ct.ThrowIfCancellationRequested();
            return PiperNative.SynthesizeWav(handle, text);
        }
        catch (Exception ex)
        {
            debugLogger.Log("LocalAiTtsProvider", $"Piper synthesis failed: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    public void Dispose()
    {
        DestroyHandle();
        _runtimeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async ValueTask<IntPtr> EnsureHandleAsync(string modelPath, string configPath, CancellationToken ct)
    {
        await _runtimeGate.WaitAsync(ct);
        try
        {
            if (!await nativeRuntimeService.IsRuntimeAvailableAsync(NativeRuntimeKind.Tts, ct))
                throw new InvalidOperationException("Local Piper native runtime is unavailable. Package libquantumz_piper.so for this Android ABI.");

            if (_handle != IntPtr.Zero
                && string.Equals(_loadedModelPath, modelPath, StringComparison.Ordinal)
                && string.Equals(_loadedConfigPath, configPath, StringComparison.Ordinal))
            {
                return _handle;
            }

            DestroyHandle();
            _handle = PiperNative.Create(modelPath, configPath, BuildParamsJson());
            _loadedModelPath = modelPath;
            _loadedConfigPath = configPath;
            debugLogger.Log("LocalAiTtsProvider", $"Loaded native Piper runtime for voice '{Path.GetFileName(modelPath)}'.", LogLevel.Info);
            return _handle;
        }
        finally
        {
            _runtimeGate.Release();
        }
    }

    private string BuildParamsJson()
    {
        var parameters = settings.GetActiveProvider("TTS")?.Parameters ?? [];
        var additionalParameters = settings.PipelineSettings.Tts.Local?.AdditionalParameters;
        if (parameters.Count == 0 && string.IsNullOrWhiteSpace(additionalParameters))
            return "{}";

        var payload = new Dictionary<string, object?>();
        foreach (var pair in parameters)
            payload[pair.Key] = pair.Value;

        if (!string.IsNullOrWhiteSpace(additionalParameters))
            payload["additionalParameters"] = additionalParameters;

        return JsonSerializer.Serialize(payload);
    }

    private static (string ModelPath, string ConfigPath)? ResolvePiperVoiceFiles(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
            return null;

        if (File.Exists(selectedPath) && string.Equals(Path.GetExtension(selectedPath), ".onnx", StringComparison.OrdinalIgnoreCase))
        {
            var configPath = FindConfigForModel(selectedPath);
            return configPath is null ? null : (selectedPath, configPath);
        }

        if (File.Exists(selectedPath) && string.Equals(Path.GetExtension(selectedPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            var modelPath = FindModelForConfig(selectedPath);
            return modelPath is null ? null : (modelPath, selectedPath);
        }

        return null;
    }

    private static string? FindConfigForModel(string modelPath)
    {
        var directConfig = $"{modelPath}.json";
        if (File.Exists(directConfig))
            return directConfig;

        var changedExtension = Path.ChangeExtension(modelPath, ".json");
        if (!string.IsNullOrWhiteSpace(changedExtension) && File.Exists(changedExtension))
            return changedExtension;

        var directory = Path.GetDirectoryName(modelPath);
        return string.IsNullOrWhiteSpace(directory)
            ? null
            : Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
    }

    private static string? FindModelForConfig(string configPath)
    {
        if (configPath.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase))
        {
            var directModel = configPath[..^".json".Length];
            if (File.Exists(directModel))
                return directModel;
        }

        var changedExtension = Path.ChangeExtension(configPath, ".onnx");
        if (!string.IsNullOrWhiteSpace(changedExtension) && File.Exists(changedExtension))
            return changedExtension;

        var directory = Path.GetDirectoryName(configPath);
        return string.IsNullOrWhiteSpace(directory)
            ? null
            : Directory.EnumerateFiles(directory, "*.onnx", SearchOption.TopDirectoryOnly).FirstOrDefault();
    }

    private void DestroyHandle()
    {
        if (_handle != IntPtr.Zero)
        {
            PiperNative.Destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _loadedModelPath = null;
        _loadedConfigPath = null;
    }
}
