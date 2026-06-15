using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

public class LlamaAIClient(
    HttpClient httpClient,
    ISettingsService settings,
    IDebugLogger debugLogger,
    IDialogService dialogService,
    ILlamaLocalManager llamaLocalManager,
    IModelRegistry modelRegistry) : IAIClient, ILlmProvider
{
    private const string RemoteFallbackBaseUrl = ModelRegistry.RemoteLlamaBaseUrl;
    private const string LocalBaseUrl = ModelRegistry.LocalLlamaBaseUrl;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProviderDescriptor Descriptor { get; } = new(
        Id: "hybrid.openai-compatible-llm",
        DisplayName: "OpenAI-Compatible LLM",
        Capability: ProviderCapability.Llm,
        Location: ProviderLocation.Hybrid);

    public bool IsReady => !string.IsNullOrWhiteSpace(settings.LlmUrl) || !string.IsNullOrWhiteSpace(LocalBaseUrl);

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        await foreach (var _ in GetExecutionCandidatesAsync(ct))
            return true;

        return false;
    }

    public async ValueTask<AiResponse> SendPromptAsync(AiRequest request, CancellationToken ct = default)
    {
        Exception? lastError = null;

        await foreach (var candidate in GetExecutionCandidatesAsync(ct))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var endpoint = BuildEndpoint(candidate.BaseUrl, "chat/completions");
                var llamaRequest = new LlamaChatRequest(
                    model: candidate.ModelId,
                    messages: request.History.Concat([new ChatMessage("user", request.Prompt)]).ToList(),
                    temperature: request.Temperature,
                    max_tokens: request.MaxTokens);

                debugLogger.LogEvent(new DebugEvent(DateTime.Now, "LLM", LogLevel.Info, $"Sending prompt to {endpoint} using model {candidate.ModelId}.", new { PromptLength = request.Prompt.Length, candidate.ModelId, candidate.BaseUrl }));
                var response = await httpClient.PostAsJsonAsync(endpoint, llamaRequest, _jsonOptions, ct);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<LlamaChatResponse>(_jsonOptions, ct)
                             ?? throw new InvalidOperationException("Failed to deserialize AI response.");
                debugLogger.LogEvent(new DebugEvent(DateTime.Now, "LLM", LogLevel.Info, "Received LLM response.", new { ResponseLength = result.Choices[0].Message.Content?.Length ?? 0, RequestedModelId = candidate.ModelId, ServerReportedModel = result.Model }));

                var choice = result.Choices[0];
                var toolCalls = new List<ToolCall>();

                if (choice.ToolCalls != null)
                {
                    toolCalls = choice.ToolCalls.Select(tc => new ToolCall(
                        Id: tc.Id,
                        Name: tc.Function.Name,
                        ArgumentsJson: tc.Function.Arguments
                    )).ToList();
                }

                return new AiResponse(
                    Content: choice.Message.Content ?? "",
                    ToolCalls: toolCalls,
                    ModelId: candidate.ModelId,
                    UsageTokens: result.Usage?.TotalTokens ?? 0);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                debugLogger.Log("AIClient", $"LLM request failed for {candidate.BaseUrl}/{candidate.ModelId}; trying fallback. {ex.Message}", LogLevel.Warning);
            }
        }

        debugLogger.Log("AIClient", "Error: AI Server unreachable both remotely and locally.");
        await dialogService.ShowAlertAsync("Connection Error", "Unable to connect to the AI server. Please check your network or local llama.cpp instance.");
        throw new HttpRequestException("AI Server is unreachable.", lastError);
    }

    public async IAsyncEnumerable<string> StreamPromptAsync(AiRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        Exception? lastError = null;

        await foreach (var candidate in GetExecutionCandidatesAsync(ct))
        {
            var endpoint = BuildEndpoint(candidate.BaseUrl, "chat/completions");
            var llamaRequest = new LlamaChatRequest(
                model: candidate.ModelId,
                messages: request.History.Concat([new ChatMessage("user", request.Prompt)]).ToList(),
                temperature: request.Temperature,
                max_tokens: request.MaxTokens,
                stream: true);

            debugLogger.LogEvent(new DebugEvent(DateTime.Now, "LLM", LogLevel.Info, $"Sending streaming prompt to {endpoint} using model {candidate.ModelId}.", new { PromptLength = request.Prompt.Length, candidate.ModelId, candidate.BaseUrl }));

            using var response = await httpClient.PostAsJsonAsync(endpoint, llamaRequest, _jsonOptions, ct);
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                debugLogger.Log("AIClient", $"Streaming LLM request failed for {candidate.BaseUrl}/{candidate.ModelId}; trying fallback. {ex.Message}", LogLevel.Warning);
                continue;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line == "") continue;
                if (line.StartsWith("data: "))
                {
                    var data = line["data: ".Length..];
                    if (data == "[DONE]") yield break;

                    string? content = null;
                    try
                    {
                        var chunk = JsonSerializer.Deserialize<LlamaStreamResponse>(data, _jsonOptions);
                        content = chunk?.Choices[0]?.Delta?.Content;
                    }
                    catch (JsonException) { /* Ignore malformed chunks */ }

                    if (!string.IsNullOrEmpty(content))
                    {
                        yield return content;
                    }
                }
            }

            yield break;
        }

        debugLogger.Log("AIClient", "Error: AI Server unreachable both remotely and locally.");
        await dialogService.ShowAlertAsync("Connection Error", "Unable to connect to the AI server. Please check your network or local llama.cpp instance.");
        throw new HttpRequestException("AI Server is unreachable.", lastError);
    }

    // --- DTOs for llama.cpp / OpenAI API ---

    private record LlamaChatRequest(
        string model,
        List<ChatMessage> messages,
        float temperature,
        int max_tokens,
        bool stream = false
    );

    private record LlamaChatResponse(
        string Model,
        List<LlamaChoice> Choices,
        LlamaUsage Usage
    );

    private record LlamaChoice(
        LlamaMessage Message,
        List<LlamaToolCall>? ToolCalls
    );

    private record LlamaMessage(
        string Role,
        string? Content,
        string? ToolCallId = null
    );

    private record LlamaToolCall(
        string Id,
        LlamaFunction Function
    );

    private record LlamaFunction(
        string Name,
        string Arguments
    );

    private record LlamaUsage(
        long TotalTokens
    );

    private record LlamaStreamResponse(
        List<LlamaStreamChoice> Choices
    );

    private record LlamaStreamChoice(
        LlamaStreamDelta Delta
    );

    private record LlamaStreamDelta(
        string? Content
    );
    public async Task<List<string>> GetAvailableModelsAsync()
    {
        var models = await modelRegistry.GetModelsAsync(ProviderCapability.Llm);
        return [.. models.Select(model => model.Id).Distinct(StringComparer.OrdinalIgnoreCase)];
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
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private async IAsyncEnumerable<LlmExecutionCandidate> GetExecutionCandidatesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var preferred = FirstNonEmpty(settings.SelectedModelName, settings.LlamaModelId);
        var registryModel = await modelRegistry.ResolvePreferredModelAsync(ProviderCapability.Llm, preferred, ct);
        var candidates = new List<LlmExecutionCandidate>();
        var configuredBaseUrl = NormalizeOpenAiBaseUrl(settings.LlmUrl);

        if (!string.IsNullOrWhiteSpace(preferred) && !string.IsNullOrWhiteSpace(configuredBaseUrl))
            candidates.Add(new LlmExecutionCandidate(configuredBaseUrl, preferred));

        if (registryModel is { Endpoint: not null })
            candidates.Add(new LlmExecutionCandidate(registryModel.Endpoint, registryModel.Id));

        var configuredModel = FirstNonEmpty(preferred, registryModel?.Id);
        if (!string.IsNullOrWhiteSpace(configuredModel))
        {
            if (!string.Equals(configuredBaseUrl, LocalBaseUrl, StringComparison.OrdinalIgnoreCase))
                candidates.Add(new LlmExecutionCandidate(configuredBaseUrl, configuredModel));

            candidates.Add(new LlmExecutionCandidate(RemoteFallbackBaseUrl, configuredModel));
            candidates.Add(new LlmExecutionCandidate(LocalBaseUrl, configuredModel));
        }

        debugLogger.LogEvent(new DebugEvent(DateTime.Now, "LLM", LogLevel.Trace, "Resolved LLM execution candidates.", new { PreferredModelId = preferred, RegistryModelId = registryModel?.Id, ConfiguredBaseUrl = configuredBaseUrl, CandidateCount = candidates.Count }));

        foreach (var candidate in candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.BaseUrl) && !string.IsNullOrWhiteSpace(candidate.ModelId))
            .DistinctBy(candidate => $"{candidate.BaseUrl}|{candidate.ModelId}", StringComparer.OrdinalIgnoreCase))
        {
            if (await IsEndpointAvailableAsync(candidate.BaseUrl, ct))
                yield return candidate;
        }
    }

    private async ValueTask<bool> IsEndpointAvailableAsync(string baseUrl, CancellationToken ct)
    {
        if (string.Equals(NormalizeOpenAiBaseUrl(baseUrl), LocalBaseUrl, StringComparison.OrdinalIgnoreCase)
            && !await llamaLocalManager.EnsureServerRunningAsync())
        {
            return false;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var response = await httpClient.GetAsync(BuildEndpoint(baseUrl, "models"), cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            debugLogger.Log("AIClient", $"Endpoint availability check failed for {baseUrl}: {ex.Message}", LogLevel.Warning);
            return false;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private record LlamaModelsResponse(List<LlamaModel>? Data);
    private record LlamaModel(string Id);
    private sealed record LlmExecutionCandidate(string BaseUrl, string ModelId);
}
