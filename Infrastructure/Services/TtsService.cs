using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// TTS engine using an OpenAI-compatible audio/speech endpoint.
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

    public bool IsReady => !string.IsNullOrWhiteSpace(settings.GetActiveProvider("TTS")?.Url);

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!IsReady)
            return false;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            var endpoint = await ResolveRemoteBaseUrlAsync(timeoutCts.Token);
            using var response = await httpClient.GetAsync(BuildEndpoint(endpoint, "models"), timeoutCts.Token);
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
        if (string.IsNullOrWhiteSpace(settings.GetActiveProvider("TTS")?.Url))
            throw new InvalidOperationException("TTS server URL is not configured.");

        var selected = await ResolveRemoteModelAsync(ct);
        var modelId = FirstNonEmpty(selected?.Id, settings.GetActiveProvider("TTS")?.ModelId ?? "", "tts-default")!;
        var baseUrl = selected?.Endpoint ?? settings.GetActiveProvider("TTS")?.Url ?? "";
        var endpoint = BuildEndpoint(baseUrl, "audio/speech");

        var requestBody = new TtsRequest(
            model: modelId,
            input: text,
            voice: "alloy"
        );

        try
        {
            debugLogger.LogEvent(new DebugEvent(DateTime.Now, "TTS", LogLevel.Info, "Synthesizing text.", new { TextLength = text.Length, ModelId = modelId, Endpoint = endpoint }));
            using var response = await httpClient.PostAsJsonAsync(endpoint, requestBody, _jsonOptions, ct);
            response.EnsureSuccessStatusCode();
            var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);
            debugLogger.LogEvent(new DebugEvent(DateTime.Now, "TTS", LogLevel.Info, $"Synthesis complete: {audioBytes.Length} bytes", new { Bytes = audioBytes.Length }));
            return audioBytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"QuantumZ: TTS Synthesis failed: {ex}");
            debugLogger.Log("TTS", $"Synthesis Error: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    private record TtsRequest(string model, string input, string voice);

    private async ValueTask<string> ResolveRemoteBaseUrlAsync(CancellationToken ct)
    {
        var selected = await ResolveRemoteModelAsync(ct);
        return selected?.Endpoint ?? settings.GetActiveProvider("TTS")?.Url ?? "";
    }

    private async ValueTask<ModelProfile?> ResolveRemoteModelAsync(CancellationToken ct)
    {
        var models = await modelRegistry.GetModelsAsync(ProviderCapability.Tts, ct);
        return models
            .Where(model => model.Location is ProviderLocation.Remote or ProviderLocation.Hybrid)
            .OrderBy(model => ModelMatchesSelection(model, settings.GetActiveProvider("TTS")?.ModelId ?? "") ? 0 : 1)
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string NormalizeOpenAiBaseUrl(string? url)
    {
        var normalized = url?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        return normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized}/v1";
    }

    private static string BuildEndpoint(string baseUrl, string path) =>
        $"{NormalizeOpenAiBaseUrl(baseUrl).TrimEnd('/')}/{path.TrimStart('/')}";

    private static bool ModelMatchesSelection(ModelProfile model, string selection) =>
        !string.IsNullOrWhiteSpace(selection)
        && (string.Equals(model.Id, selection, StringComparison.OrdinalIgnoreCase)
            || string.Equals(model.DisplayName, selection, StringComparison.OrdinalIgnoreCase)
            || string.Equals($"{model.Provider}:{model.Id}", selection, StringComparison.OrdinalIgnoreCase));

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}