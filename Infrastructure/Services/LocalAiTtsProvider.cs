using System.Text.Json;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Infrastructure.Native;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Local AI TTS provider. NOT SHIPPED in v1.
/// Piper TTS support was dropped for v1; Android TTS is the on-device voice fallback.
/// This provider always reports unavailable in v1.
/// </summary>
public sealed class LocalAiTtsProvider(IModelRegistry modelRegistry, ISettingsService settings, IDebugLogger debugLogger, INativeRuntimeService nativeRuntimeService) : ITtsProvider, IDisposable
{
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private IntPtr _handle;
    private string? _loadedModelPath;
    private string? _loadedConfigPath;

    public ProviderDescriptor Descriptor { get; } = new(
        Id: "local.ai-tts",
        DisplayName: "Local AI TTS (Not available in v1)",
        Capability: ProviderCapability.Tts,
        Location: ProviderLocation.Local);

    public bool IsReady => true;

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // Piper TTS is not shipped in v1. Android TTS is the on-device voice fallback.
        debugLogger.Log("LocalAiTtsProvider", "Local AI TTS (Piper) is not available in v1. Using Android TTS fallback.", LogLevel.Info);
        return false;
    }

    public async ValueTask<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        // Piper TTS is not shipped in v1. This method should never be called because IsAvailableAsync returns false.
        throw new InvalidOperationException("Local AI TTS (Piper) is not available in v1. Use Android TTS instead.");
    }

    public void Dispose()
    {
        DestroyHandle();
        _runtimeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async ValueTask<IntPtr> EnsureHandleAsync(string modelPath, string configPath, CancellationToken ct)
    {
        // Piper TTS is not shipped in v1. This method should never be called.
        throw new InvalidOperationException("Local AI TTS (Piper) is not available in v1.");
    }

    private string BuildParamsJson()
    {
        var parameters = settings.GetActiveProvider("TTS")?.Parameters ?? [];
        if (parameters.Count == 0)
            return "{}";

        var payload = new Dictionary<string, object?>();
        foreach (var pair in parameters)
            payload[pair.Key] = pair.Value;


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
