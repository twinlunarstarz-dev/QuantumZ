using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// TTS engine using the llama.cpp server's audio/speech endpoint.
/// </summary>
public sealed class TtsService(HttpClient httpClient, ISettingsService settings, IDebugLogger debugLogger, IModelRegistry modelRegistry) : ITtsEngine, ITtsProvider
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProviderDescriptor Descriptor { get; } = new(
        Id: "remote.openai-compatible-tts",
        DisplayName: "Remote OpenAI-Compatible TTS",
        Capability: ProviderCapability.Tts,
        Location: ProviderLocation.Remote);

    public bool IsReady => !string.IsNullOrWhiteSpace(settings.TtsUrl);

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!IsReady)
            return false;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            var endpoint = await ResolveRemoteBaseUrlAsync(timeoutCts.Token);
            var response = await httpClient.GetAsync($"{endpoint.TrimEnd('/')}/models", timeoutCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            debugLogger.Log("TTS", $"Remote TTS availability check failed: {ex.Message}", LogLevel.Warning);
            return false;
        }
    }

    public async ValueTask<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.TtsUrl))
            throw new InvalidOperationException("TTS server URL is not configured.");

        var selected = await ResolveRemoteModelAsync(ct);
        var modelId = selected?.Id ?? settings.TtsModelId;
        var baseUrl = selected?.Endpoint ?? settings.TtsUrl;
        var endpoint = $"{baseUrl.TrimEnd('/')}/audio/speech";

        var requestBody = new TtsRequest(
            model: modelId,
            input: text,
            voice: "alloy"
        );

        try
        {
            debugLogger.LogEvent(new DebugEvent(DateTime.Now, "TTS", LogLevel.Info, "Synthesizing text.", new { TextLength = text.Length, ModelId = modelId }));
            var response = await httpClient.PostAsJsonAsync(endpoint, requestBody, _jsonOptions, ct);
            response.EnsureSuccessStatusCode();
            var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);
            debugLogger.LogEvent(new DebugEvent(DateTime.Now, "TTS", LogLevel.Info, $"Synthesis complete: {audioBytes.Length} bytes", new { Bytes = audioBytes.Length }));
            return audioBytes;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("QuantumZ", $"TTS Synthesis failed: {ex}");
            debugLogger.Log("TTS", $"Synthesis Error: {ex.Message}", LogLevel.Error);
            throw; // Re-throw to be handled by the caller (e.g., MicrophoneForegroundService)
        }
    }

    private record TtsRequest(string model, string input, string voice);

    private async ValueTask<string> ResolveRemoteBaseUrlAsync(CancellationToken ct)
    {
        var selected = await ResolveRemoteModelAsync(ct);
        return selected?.Endpoint ?? settings.TtsUrl;
    }

    private async ValueTask<ModelProfile?> ResolveRemoteModelAsync(CancellationToken ct)
    {
        var models = await modelRegistry.GetModelsAsync(ProviderCapability.Tts, ct);
        return models
            .Where(model => model.Location is ProviderLocation.Remote or ProviderLocation.Hybrid)
            .OrderBy(model => ModelMatchesSelection(model, settings.TtsModelId) ? 0 : 1)
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool ModelMatchesSelection(ModelProfile model, string selection) =>
        !string.IsNullOrWhiteSpace(selection)
        && (string.Equals(model.Id, selection, StringComparison.OrdinalIgnoreCase)
            || string.Equals(model.DisplayName, selection, StringComparison.OrdinalIgnoreCase)
            || string.Equals($"{model.Provider}:{model.Id}", selection, StringComparison.OrdinalIgnoreCase));
}
