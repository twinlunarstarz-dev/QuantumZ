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
    IModelRegistry modelRegistry,
    IMcpOrchestrator mcpOrchestrator) : IAIClient, ILlmProvider
{
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

    public bool IsReady => !string.IsNullOrWhiteSpace(settings.GetActiveProvider("LLM")?.Url) || !string.IsNullOrWhiteSpace(LocalBaseUrl);

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        await foreach (var _ in GetExecutionCandidatesAsync(ct))
            return true;

        return false;
    }

    public async ValueTask<AiResponse> SendPromptAsync(AiRequest request, CancellationToken ct = default)
    {
        Exception? lastError = null;
        var messages = BuildMessages(request);
        var toolDefinitions = await BuildToolDefinitionsAsync(request, ct);

        await foreach (var candidate in GetExecutionCandidatesAsync(ct))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var endpoint = BuildEndpoint(candidate.BaseUrl, "chat/completions");
                var llamaRequest = BuildChatRequest(candidate.ModelId, messages, request.Temperature, request.MaxTokens, stream: false, toolDefinitions);

                debugLogger.LogEvent(new DebugEvent(DateTime.Now, "LLM", LogLevel.Info, $"Sending prompt to {endpoint} using model {candidate.ModelId}.", new { PromptLength = request.Prompt.Length, candidate.ModelId, candidate.BaseUrl, ToolCount = toolDefinitions?.Count ?? 0 }));
                using var response = await httpClient.PostAsJsonAsync(endpoint, llamaRequest, _jsonOptions, ct);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<LlamaChatResponse>(_jsonOptions, ct)
                             ?? throw new InvalidOperationException("Failed to deserialize AI response.");
                var choice = result.Choices.FirstOrDefault()
                             ?? throw new InvalidOperationException("AI response did not contain any choices.");

                var toolCalls = choice.Message.ToolCalls?.Select(tc => new ToolCall(
                        Id: string.IsNullOrWhiteSpace(tc.Id) ? Guid.NewGuid().ToString("N") : tc.Id,
                        Name: tc.Function.Name,
                        ArgumentsJson: string.IsNullOrWhiteSpace(tc.Function.Arguments) ? "{}" : tc.Function.Arguments
                    )).ToList() ?? [];

                debugLogger.LogEvent(new DebugEvent(DateTime.Now, "LLM", LogLevel.Info, "Received LLM response.", new { ResponseLength = choice.Message.Content?.Length ?? 0, RequestedModelId = candidate.ModelId, ServerReportedModel = result.Model, ToolCallCount = toolCalls.Count }));

                return new AiResponse(
                    Content: choice.Message.Content ?? string.Empty,
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
        var messages = BuildMessages(request);

        await foreach (var candidate in GetExecutionCandidatesAsync(ct))
        {
            var endpoint = BuildEndpoint(candidate.BaseUrl, "chat/completions");
            var llamaRequest = BuildChatRequest(candidate.ModelId, messages, request.Temperature, request.MaxTokens, stream: true, toolDefinitions: null);

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
                if (line.Length == 0) continue;
                if (line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
                {
                    var data = line["data: ".Length..];
                    if (data == "[DONE]") yield break;

                    string? content = null;
                    try
                    {
                        var chunk = JsonSerializer.Deserialize<LlamaStreamResponse>(data, _jsonOptions);
                        content = chunk?.Choices.FirstOrDefault()?.Delta?.Content;
                    }
                    catch (JsonException) { }

                    if (!string.IsNullOrEmpty(content))
                        yield return content;
                }
            }

            yield break;
        }

        debugLogger.Log("AIClient", "Error: AI Server unreachable both remotely and locally.");
        await dialogService.ShowAlertAsync("Connection Error", "Unable to connect to the AI server. Please check your network or local llama.cpp instance.");
        throw new HttpRequestException("AI Server is unreachable.", lastError);
    }

    private static Dictionary<string, object?> BuildChatRequest(
        string modelId,
        List<Dictionary<string, object?>> messages,
        float temperature,
        int maxTokens,
        bool stream,
        List<Dictionary<string, object?>>? toolDefinitions)
    {
        var request = new Dictionary<string, object?>
        {
            ["model"] = modelId,
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens,
            ["stream"] = stream
        };

        if (toolDefinitions is { Count: > 0 })
        {
            request["tools"] = toolDefinitions;
            request["tool_choice"] = "auto";
        }

        return request;
    }

    private List<Dictionary<string, object?>> BuildMessages(AiRequest request)
    {
        var messages = new List<Dictionary<string, object?>>();

        // request.SystemPrompt (set by AIIntegrationService) takes precedence over the
        // legacy GlobalSettings.CustomSystemMessage fallback.
        var systemPrompt = !string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? request.SystemPrompt
            : settings.GlobalSettings.CustomSystemMessage;

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new Dictionary<string, object?> { ["role"] = "system", ["content"] = systemPrompt });

        messages.AddRange(request.History.Select(ToWireMessage));

        if (!string.IsNullOrWhiteSpace(request.Prompt))
            messages.Add(new Dictionary<string, object?> { ["role"] = "user", ["content"] = request.Prompt });

        return messages;
    }

    private static Dictionary<string, object?> ToWireMessage(ChatMessage message)
    {
        var wire = new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = message.Content
        };

        if (!string.IsNullOrWhiteSpace(message.ToolCallId))
            wire["tool_call_id"] = message.ToolCallId;

        if (message.ToolCalls is { Count: > 0 })
            wire["tool_calls"] = message.ToolCalls;

        return wire;
    }

    private async ValueTask<List<Dictionary<string, object?>>?> BuildToolDefinitionsAsync(AiRequest request, CancellationToken ct)
    {
        if (!request.EnableToolCalling)
            return null;

        try
        {
            IReadOnlyList<McpToolDefinition> tools;

            if (request.AvailableTools is { Count: > 0 })
            {
                // Use pre-discovered tools injected by AIIntegrationService — avoids redundant RPC round-trip.
                tools = request.AvailableTools;
            }
            else
            {
                // Fallback: discover directly when called outside of AIIntegrationService.
                var discovered = await mcpOrchestrator.DiscoverToolsAsync(ct);
                tools = [.. discovered.Select(t => new McpToolDefinition(t.Name, t.Description, t.InputSchemaJson))];
            }

            var definitions = tools
                .Where(tool => !string.IsNullOrWhiteSpace(tool.Name))
                .Select(tool => new Dictionary<string, object?>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?>
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = ParseToolSchema(tool.InputSchemaJson)
                    }
                })
                .ToList();

            return definitions.Count == 0 ? null : definitions;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            debugLogger.Log("AIClient", $"MCP tool discovery failed; continuing without tools. {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    private static object ParseToolSchema(string? inputSchemaJson)
    {
        if (!string.IsNullOrWhiteSpace(inputSchemaJson))
        {
            try
            {
                using var document = JsonDocument.Parse(inputSchemaJson);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
            }
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>()
        };
    }

    private record LlamaChatResponse(
        string Model,
        List<LlamaChoice> Choices,
        LlamaUsage? Usage
    );

    private record LlamaChoice(
        LlamaMessage Message
    );

    private record LlamaMessage(
        string Role,
        string? Content,
        [property: JsonPropertyName("tool_calls")] List<LlamaToolCall>? ToolCalls = null
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
        [property: JsonPropertyName("total_tokens")] long TotalTokens
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
        $"{NormalizeOpenAiBaseUrl(baseUrl).TrimEnd('/')}/{path.TrimStart('/')}";

    private async IAsyncEnumerable<LlmExecutionCandidate> GetExecutionCandidatesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var preferred = settings.GetActiveProvider("LLM")?.ModelId ?? "";
        var registryModel = await modelRegistry.ResolvePreferredModelAsync(ProviderCapability.Llm, preferred, ct);
        var candidates = new List<LlmExecutionCandidate>();
        var configuredBaseUrl = NormalizeOpenAiBaseUrl(settings.GetActiveProvider("LLM")?.Url ?? "");

        if (!string.IsNullOrWhiteSpace(preferred) && !string.IsNullOrWhiteSpace(configuredBaseUrl))
            candidates.Add(new LlmExecutionCandidate(configuredBaseUrl, preferred));

        var registryEndpoint = NormalizeOpenAiBaseUrl(registryModel?.Endpoint);
        if (registryModel is not null && !string.IsNullOrWhiteSpace(registryEndpoint))
            candidates.Add(new LlmExecutionCandidate(registryEndpoint, registryModel.Id));

        var configuredModel = FirstNonEmpty(preferred, registryModel?.Id);
        if (!string.IsNullOrWhiteSpace(configuredModel))
        {
            if (!string.IsNullOrWhiteSpace(configuredBaseUrl) && !string.Equals(configuredBaseUrl, LocalBaseUrl, StringComparison.OrdinalIgnoreCase))
                candidates.Add(new LlmExecutionCandidate(configuredBaseUrl, configuredModel));

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
            using var response = await httpClient.GetAsync(BuildEndpoint(baseUrl, "models"), cts.Token);
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
