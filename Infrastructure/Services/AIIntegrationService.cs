using QuantumZ.Core.Interfaces;
using System.Text;

namespace QuantumZ.Infrastructure.Services;

public class AIIntegrationService(IAIClient aiClient, IMcpOrchestrator mcpOrchestrator)
{
    private const int MaxIterations = 6;

    public async ValueTask<string> ExecutePromptAsync(AiRequest request, CancellationToken ct = default)
    {
        var currentHistory = new List<ChatMessage>(request.History)
        {
            new("user", request.Prompt)
        };

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            var aiRequest = request with { History = currentHistory };
            var response = await aiClient.SendPromptAsync(aiRequest, ct);

            if (response.ToolCalls is not { Count: > 0 })
                return response.Content;

            currentHistory.Add(new ChatMessage("assistant", response.Content, ToolCallId: response.ToolCalls[0].Id));

            foreach (var toolCall in response.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();
                var result = await mcpOrchestrator.ExecuteToolAsync(toolCall.Name, toolCall.ArgumentsJson, ct);
                currentHistory.Add(new ChatMessage("tool", result.Content, ToolCallId: toolCall.Id));
            }
        }

        var finalRequest = request with { History = currentHistory, EnableToolCalling = false };
        var finalResponse = await aiClient.SendPromptAsync(finalRequest, ct);
        return string.IsNullOrWhiteSpace(finalResponse.Content)
            ? "I could not produce a final response from the available tool results."
            : finalResponse.Content;
    }
}
