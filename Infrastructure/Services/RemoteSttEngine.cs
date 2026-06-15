using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Infrastructure.Utilities;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Remote STT engine using an OpenAI-compatible audio/transcriptions endpoint on llama.cpp.
/// </summary>
public sealed class RemoteSttEngine(HttpClient httpClient, ISettingsService settings, IDebugLogger debugLogger) : ISttEngine, ISttProvider
{
    private const int SampleRate = 16000;
    private const short Channels = 1;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProviderDescriptor Descriptor { get; } = new(
        Id: "remote.openai-compatible-stt",
        DisplayName: "Remote OpenAI-Compatible STT",
        Capability: ProviderCapability.Stt,
        Location: ProviderLocation.Remote);

    public bool IsReady => !string.IsNullOrWhiteSpace(settings.SttUrl);

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!IsReady)
            return false;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            var response = await httpClient.GetAsync($"{settings.SttUrl.TrimEnd('/')}/models", timeoutCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            debugLogger.Log("STT", $"Remote STT availability check failed: {ex.Message}", LogLevel.Warning);
            return false;
        }
    }

    public ValueTask InitializeAsync(CancellationToken ct = default)
    {
        // Remote engine is ready as long as the URL is configured.
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> TranscribeAsync(byte[] pcm16Audio, CancellationToken ct = default) =>
        TranscribeAsync(pcm16Audio, textProgress: null, ct);

    public async ValueTask<string> TranscribeAsync(byte[] pcm16Audio, IProgress<string>? textProgress, CancellationToken ct = default)
    {
        if (!IsReady)
            throw new InvalidOperationException("STT server URL is not configured for remote STT.");

        var endpoint = $"{settings.SttUrl.TrimEnd('/')}/audio/transcriptions";

        // Wrap raw PCM in a valid WAV header so the endpoint can parse it correctly.
        var wavBytes = PcmToWavConverter.Convert(SampleRate, Channels, pcm16Audio);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(settings.SttModelId), "model");
        content.Add(new StreamContent(new MemoryStream(wavBytes))
        {
            Headers = { ContentType = new MediaTypeHeaderValue("audio/wav") }
        }, "file", "audio.wav");

        debugLogger.LogEvent(new DebugEvent(DateTime.Now, "STT", LogLevel.Info, "Sending audio for transcription...", new { Bytes = pcm16Audio.Length, settings.SttModelId }));
        var response = await httpClient.PostAsync(endpoint, content, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TranscriptionResponse>(_jsonOptions, ct);
        var text = result?.Text ?? string.Empty;
        textProgress?.Report(text);
        debugLogger.LogEvent(new DebugEvent(DateTime.Now, "STT", LogLevel.Info, $"Transcribed text: {text}", new { TextLength = text.Length }));
        return text;
    }

    private record TranscriptionResponse(string Text);
}
