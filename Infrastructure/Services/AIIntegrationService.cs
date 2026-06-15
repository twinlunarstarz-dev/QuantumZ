using QuantumZ.Core.Interfaces;
using System.Text;

namespace QuantumZ.Infrastructure.Services;

public class AIIntegrationService(IAIClient aiClient, IMcpOrchestrator mcpOrchestrator)
{
    /// <summary>
    /// Executes a prompt and handles any necessary tool calling loops before returning the final response.
    /// </summary>
    public async ValueTask<string> ExecutePromptAsync(AiRequest request, CancellationToken ct = default)
    {
        var currentHistory = new List<ChatMessage>(request.History);
        var userMessage = new ChatMessage("user", request.Prompt);
        currentHistory.Add(userMessage);

        while (true)
        {
            var aiRequest = request with { History = currentHistory };
            var response = await aiClient.SendPromptAsync(aiRequest, ct);

            if (response.ToolCalls == null || response.ToolCalls.Count == 0)
            {
                return response.Content;
            }

            // Add AI's tool call request to history
            currentHistory.Add(new ChatMessage("assistant", response.Content, ToolCallId: response.ToolCalls[0].Id));

            foreach (var toolCall in response.ToolCalls)
            {
                var result = await mcpOrchestrator.ExecuteToolAsync(toolCall.Name, toolCall.ArgumentsJson, ct);
                
                // Feed the tool result back into history as a "tool" role message
                currentHistory.Add(new ChatMessage("tool", result.Content, ToolCallId: toolCall.Id));
            }

            // Loop again to let LLM synthesize the results
        }
    }
}
