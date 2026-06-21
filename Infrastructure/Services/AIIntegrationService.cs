using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

public class AIIntegrationService(
    IAIClient aiClient,
    IMcpOrchestrator mcpOrchestrator,
    ISettingsService settingsService,
    IDebugLogger debugLogger) : IAIIntegrationService
{
    public async ValueTask<string> ExecutePromptAsync(AiRequest request, CancellationToken ct = default)
    {
        // Resolve system prompt: explicit caller value wins, then VoiceAssistantSettings default.
        var resolvedSystemPrompt = !string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? request.SystemPrompt
            : settingsService.VoiceAssistantSettings.SystemPrompt;

        // Discover MCP tools once per request when tool calling is active and the
        // caller has not already pre-supplied a tool list.
        IReadOnlyList<McpToolDefinition>? resolvedTools = request.AvailableTools;
        if (request.EnableToolCalling && resolvedTools is not { Count: > 0 })
        {
            try
            {
                var discovered = await mcpOrchestrator.DiscoverToolsAsync(ct);
                if (discovered.Count > 0)
                    resolvedTools = [.. discovered.Select(t => new McpToolDefinition(t.Name, t.Description, t.InputSchemaJson))];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Non-fatal: continue without MCP tools
                debugLogger.Log("AIIntegration", $"MCP tool discovery failed: {ex.Message}", LogLevel.Warning);
            }
        }

        // Build a single enriched request so every iteration of the tool loop carries
        // both the resolved system prompt and the pre-discovered tool list.
        var enrichedRequest = request with
        {
            SystemPrompt = resolvedSystemPrompt,
            AvailableTools = resolvedTools
        };

        var currentHistory = new List<ChatMessage>(enrichedRequest.History);
        if (!string.IsNullOrWhiteSpace(enrichedRequest.Prompt))
            currentHistory.Add(new ChatMessage("user", enrichedRequest.Prompt));

        var maxIterations = settingsService.VoiceAssistantSettings.MaxToolCallIterations;
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            var aiRequest = enrichedRequest with { Prompt = string.Empty, History = currentHistory };
            var response = await aiClient.SendPromptAsync(aiRequest, ct);

            if (response.ToolCalls is not { Count: > 0 })
                return response.Content;

            currentHistory.Add(new ChatMessage("assistant", response.Content, ToolCalls: response.ToolCalls));

            foreach (var toolCall in response.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();

                ToolResult result;
                try
                {
                    result = await mcpOrchestrator.ExecuteToolAsync(toolCall.Name, toolCall.ArgumentsJson, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    debugLogger.Log("AIIntegration", $"MCP tool '{toolCall.Name}' execution failed: {ex.Message}", LogLevel.Error);
                    result = new ToolResult(false, string.Empty, ex.Message);
                }

                var toolContent = result.Success
                    ? result.Content
                    : $"Tool '{toolCall.Name}' failed: {result.ErrorMessage ?? "unknown error"}";

                currentHistory.Add(new ChatMessage("tool", toolContent, ToolCallId: toolCall.Id));
            }
        }

        var finalRequest = enrichedRequest with { Prompt = string.Empty, History = currentHistory, EnableToolCalling = false };
        var finalResponse = await aiClient.SendPromptAsync(finalRequest, ct);
        return string.IsNullOrWhiteSpace(finalResponse.Content)
            ? "I could not produce a final response from the available tool results."
            : finalResponse.Content;
    }
}