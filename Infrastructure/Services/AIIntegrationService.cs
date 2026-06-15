using QuantumZ.Core.Interfaces;

namespace QuantumZ.Infrastructure.Services;

public class AIIntegrationService(IAIClient aiClient, IMcpOrchestrator mcpOrchestrator)
{
    private const int MaxIterations = 6;

    public async ValueTask<string> ExecutePromptAsync(AiRequest request, CancellationToken ct = default)
    {
        var currentHistory = new List<ChatMessage>(request.History);
        if (!string.IsNullOrWhiteSpace(request.Prompt))
            currentHistory.Add(new ChatMessage("user", request.Prompt));

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            var aiRequest = request with { Prompt = string.Empty, History = currentHistory };
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
                    result = new ToolResult(false, string.Empty, ex.Message);
                }

                var toolContent = result.Success
                    ? result.Content
                    : $"Tool '{toolCall.Name}' failed: {result.ErrorMessage ?? "unknown error"}";

                currentHistory.Add(new ChatMessage("tool", toolContent, ToolCallId: toolCall.Id));
            }
        }

        var finalRequest = request with { Prompt = string.Empty, History = currentHistory, EnableToolCalling = false };
        var finalResponse = await aiClient.SendPromptAsync(finalRequest, ct);
        return string.IsNullOrWhiteSpace(finalResponse.Content)
            ? "I could not produce a final response from the available tool results."
            : finalResponse.Content;
    }
}