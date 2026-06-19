using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

public sealed class WhisperLocalSttProvider(IModelRegistry modelRegistry, ISettingsService settings, IDebugLogger debugLogger, ILocalBinaryManager binaryManager) : ISttProvider
{
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
            await binaryManager.EnsureBinaryAsync("whisper-cpp");
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
            await binaryManager.EnsureBinaryAsync("whisper-cpp");
        }
        catch (Exception ex)
        {
            debugLogger.Log("WhisperLocalSttProvider", $"Initialization failed: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    public async ValueTask<string> TranscribeAsync(byte[] pcm16Audio, CancellationToken ct = default)
    {
        var modelPath = await ResolveWhisperModelPathAsync(ct)
            ?? throw new InvalidOperationException("No local Whisper model is available.");

        string? audioFile = null;
        string? actualResultFile = null;
        string? stdoutFile = null;
        string? stderrFile = null;
        try
        {
            var whisperBin = await binaryManager.EnsureBinaryAsync("whisper-cpp");
            audioFile = Path.Combine(Path.GetTempPath(), $"stt_{Guid.NewGuid():N}.wav");
             
            // Whisper expects WAV format (PCM16). We need to wrap the raw PCM bytes in a WAV header.
            await WriteWavHeaderAsync(audioFile, pcm16Audio);

            debugLogger.Log("WhisperLocalSttProvider", $"Transcribing audio using Whisper model at {modelPath}", LogLevel.Info);

            var outputPrefix = Path.Combine(Path.GetTempPath(), $"stt_{Guid.NewGuid():N}");
            actualResultFile = $"{outputPrefix}.txt";
            stdoutFile = $"{outputPrefix}.stdout.log";
            stderrFile = $"{outputPrefix}.stderr.log";
            var command = $"{QuoteShellArg(whisperBin)} -m {QuoteShellArg(modelPath)} -f {QuoteShellArg(audioFile)} -otxt -of {QuoteShellArg(outputPrefix)} > {QuoteShellArg(stdoutFile)} 2> {QuoteShellArg(stderrFile)}";

            var exitCode = ExecuteShellCommand(command);
            if (exitCode != 0)
            {
                var stderr = File.Exists(stderrFile) ? await File.ReadAllTextAsync(stderrFile, ct) : string.Empty;
                throw new InvalidOperationException($"whisper.cpp exited with code {exitCode}. {stderr}".Trim());
            }
             
            if (!File.Exists(actualResultFile))
            {
                debugLogger.Log("WhisperLocalSttProvider", $"Transcription output file not found at {actualResultFile}", LogLevel.Error);
                return string.Empty;
            }

            var text = await File.ReadAllTextAsync(actualResultFile, ct);

            return text.Trim();
        }
        catch (Exception ex)
        {
            debugLogger.Log("WhisperLocalSttProvider", $"Transcription failed: {ex.Message}", LogLevel.Error);
            throw;
        }
        finally
        {
            TryDelete(audioFile);
            TryDelete(actualResultFile);
            TryDelete(stdoutFile);
            TryDelete(stderrFile);
        }
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

    private async ValueTask WriteWavHeaderAsync(string path, byte[] pcmData)
    {
        // Simple WAV header for 16kHz Mono PCM16
        using var stream = File.OpenWrite(path);
        byte[] header = new byte[44];
        
        // RIFF chunk descriptor
        header[0] = (byte)'R'; header[1] = (byte)'I'; header[2] = (byte)'F'; header[3] = (byte)'F';
        uint fileSize = (uint)(pcmData.Length + 36);
        BitConverter.TryWriteBytes(header.AsSpan(4, 4), fileSize);
        header[8] = (byte)'W'; header[9] = (byte)'A'; header[10] = (byte)'V'; header[11] = (byte)'E';

        // fmt sub-chunk
        header[12] = (byte)'f'; header[13] = (byte)'m'; header[14] = (byte)'t'; header[15] = (byte)' ';
        BitConverter.TryWriteBytes(header.AsSpan(16, 4), 16u); // Subchunk1Size
        BitConverter.TryWriteBytes(header.AsSpan(20, 2), (short)1); // AudioFormat (PCM)
        BitConverter.TryWriteBytes(header.AsSpan(22, 2), (short)1); // NumChannels
        BitConverter.TryWriteBytes(header.AsSpan(24, 4), 16000u); // SampleRate
        uint byteRate = 16000 * 1 * 2;
        BitConverter.TryWriteBytes(header.AsSpan(28, 4), byteRate);
        BitConverter.TryWriteBytes(header.AsSpan(32, 2), (short)2); // BlockAlign
        BitConverter.TryWriteBytes(header.AsSpan(34, 2), (short)16); // BitsPerSample

        // data sub-chunk
        header[36] = (byte)'d'; header[37] = (byte)'a'; header[38] = (byte)'t'; header[39] = (byte)'a';
        BitConverter.TryWriteBytes(header.AsSpan(40, 4), (uint)pcmData.Length);

        await stream.WriteAsync(header);
        await stream.WriteAsync(pcmData);
    }

    private int ExecuteShellCommand(string command)
    {
        try
        {
            var runtime = Java.Lang.Runtime.GetRuntime();
            var process = runtime.Exec(new string[] { "sh", "-c", command });
            return process.WaitFor(); 
        }
        catch (Exception ex)
        {
            debugLogger.Log("WhisperLocalSttProvider", $"Shell execution failed: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try { File.Delete(path); }
        catch { }
    }

    private static string QuoteShellArg(string value) => $"'{value.Replace("'", "'\\''")}'";
}
