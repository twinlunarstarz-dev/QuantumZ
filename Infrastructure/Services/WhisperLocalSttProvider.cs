using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Infrastructure.Native;

namespace QuantumZ.Infrastructure.Services;

public sealed class WhisperLocalSttProvider(IModelRegistry modelRegistry, ISettingsService settings, IDebugLogger debugLogger, INativeRuntimeService nativeRuntimeService) : ISttProvider, IDisposable
{
    private const int SampleRate = 16000;
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private IntPtr _handle;
    private string? _loadedModelPath;

    public ProviderDescriptor Descriptor { get; } = new(
        Id: "local.whisper",
        DisplayName: "Local Whisper STT",
        Capability: ProviderCapability.Stt,
        Location: ProviderLocation.Local);

    public bool IsReady => true;

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!settings.UseOnDeviceStt)
            return false;

        try
        {
            if (!await nativeRuntimeService.IsRuntimeAvailableAsync(NativeRuntimeKind.Stt, ct))
            {
                debugLogger.Log("WhisperLocalSttProvider", "Local Whisper is unavailable because libquantumz_whisper.so is not packaged for this Android ABI.", LogLevel.Warning);
                return false;
            }

            var modelPath = await ResolveWhisperModelPathAsync(ct);
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                debugLogger.Log("WhisperLocalSttProvider", "Local Whisper is unavailable because no model file was found.", LogLevel.Warning);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            debugLogger.Log("WhisperLocalSttProvider", $"Availability check failed: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    public async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureHandleAsync(ct);
        }
        catch (Exception ex)
        {
            debugLogger.Log("WhisperLocalSttProvider", $"Initialization failed: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    public async ValueTask<string> TranscribeAsync(byte[] pcm16Audio, CancellationToken ct = default)
    {
        try
        {
            var handle = await EnsureHandleAsync(ct);
            debugLogger.Log("WhisperLocalSttProvider", $"Transcribing raw PCM16 audio using native Whisper model '{Path.GetFileName(_loadedModelPath)}'.", LogLevel.Info);
            ct.ThrowIfCancellationRequested();
            return WhisperNative.TranscribePcm16(handle, pcm16Audio, SampleRate);
        }
        catch (Exception ex)
        {
            debugLogger.Log("WhisperLocalSttProvider", $"Transcription failed: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    public void Dispose()
    {
        DestroyHandle();
        _runtimeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async ValueTask<string?> ResolveWhisperModelPathAsync(CancellationToken ct)
    {
        var configuredPath = settings.WhisperModelPath?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        var selected = await modelRegistry.ResolvePreferredModelAsync(ProviderCapability.Stt, settings.GetActiveProvider("STT")?.ModelId ?? "", ct);
        if (selected is { Location: ProviderLocation.Local, LocalPath: not null } && File.Exists(selected.LocalPath))
            return selected.LocalPath;

        return null;
    }

    private async ValueTask<IntPtr> EnsureHandleAsync(CancellationToken ct)
    {
        await _runtimeGate.WaitAsync(ct);
        try
        {
            if (!await nativeRuntimeService.IsRuntimeAvailableAsync(NativeRuntimeKind.Stt, ct))
                throw new InvalidOperationException("Local Whisper native runtime is unavailable. Package libquantumz_whisper.so for this Android ABI.");

            var modelPath = await ResolveWhisperModelPathAsync(ct)
                ?? throw new InvalidOperationException("No local Whisper model is available.");

            if (!File.Exists(modelPath))
                throw new FileNotFoundException("Local Whisper model file does not exist.", modelPath);

            if (_handle != IntPtr.Zero && string.Equals(_loadedModelPath, modelPath, StringComparison.Ordinal))
                return _handle;

            DestroyHandle();
            _handle = WhisperNative.Create(modelPath, BuildParamsJson());
            _loadedModelPath = modelPath;
            debugLogger.Log("WhisperLocalSttProvider", $"Loaded native Whisper runtime for model '{Path.GetFileName(modelPath)}'.", LogLevel.Info);
            return _handle;
        }
        finally
        {
            _runtimeGate.Release();
        }
    }

    private string BuildParamsJson()
    {
        var parameters = settings.GetActiveProvider("STT")?.Parameters ?? [];
        var additionalParameters = settings.PipelineSettings.Stt.Local?.AdditionalParameters;
        if (parameters.Count == 0 && string.IsNullOrWhiteSpace(additionalParameters))
            return "{}";

        var payload = new Dictionary<string, object?>();
        foreach (var pair in parameters)
            payload[pair.Key] = pair.Value;

        if (!string.IsNullOrWhiteSpace(additionalParameters))
            payload["additionalParameters"] = additionalParameters;

        return JsonSerializer.Serialize(payload);
    }

    private void DestroyHandle()
    {
        if (_handle != IntPtr.Zero)
        {
            WhisperNative.Destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _loadedModelPath = null;
    }
}
