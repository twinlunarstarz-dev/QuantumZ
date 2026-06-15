using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Routes to local Kokoro/Piper model assets when they are installed in app storage.
/// </summary>
public sealed class LocalAiTtsProvider(IModelRegistry modelRegistry, ISettingsService settings, IDebugLogger debugLogger, ILocalBinaryManager binaryManager) : ITtsProvider
{
    public ProviderDescriptor Descriptor { get; } = new(
        Id: "local.ai-tts",
        DisplayName: "Local AI TTS (Kokoro/Piper)",
        Capability: ProviderCapability.Tts,
        Location: ProviderLocation.Local);

    public bool IsReady => true;

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var selected = await modelRegistry.ResolvePreferredModelAsync(ProviderCapability.Tts, settings.TtsModelId, ct);
        return selected is { Location: ProviderLocation.Local };
    }

    public async ValueTask<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        var selected = await modelRegistry.ResolvePreferredModelAsync(ProviderCapability.Tts, settings.TtsModelId, ct)
            ?? throw new InvalidOperationException("No local TTS model is available.");

        if (selected.Location != ProviderLocation.Local)
            throw new InvalidOperationException("The selected TTS model is not local.");

        try
        {
            var piperBin = await binaryManager.EnsureBinaryAsync("piper");
            var tempFile = Path.Combine(Path.GetTempPath(), $"tts_{Guid.NewGuid()}.wav");
            
            // Piper CLI: echo "text" | piper --model model_path --output_file output_path
            // We need the actual path to the .onnx model from selected.Id or registry metadata
            var modelPath = selected.LocalPath ?? throw new InvalidOperationException("Model file path not found for local TTS.");

            debugLogger.Log("LocalAiTtsProvider", $"Synthesizing text using Piper: {text}", LogLevel.Info);

            // Use shell to pipe input and run piper
            var command = $"echo \"{text}\" | {piperBin} --model {modelPath} --output_file {tempFile}";
            ExecuteShellCommand(command);

            if (!File.Exists(tempFile))
                throw new FileNotFoundException("Piper failed to produce output wav file.");

            var audioBytes = await File.ReadAllBytesAsync(tempFile, ct);
            File.Delete(tempFile);

            return audioBytes;
        }
        catch (Exception ex)
        {
            debugLogger.Log("LocalAiTtsProvider", $"Piper synthesis failed: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    private void ExecuteShellCommand(string command)
    {
        var runtime = Java.Lang.Runtime.GetRuntime();
        runtime.Exec(new string[] { "sh", "-c", command });
    }
}
