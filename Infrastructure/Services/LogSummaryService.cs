using System.Text;
using System.Text.Json;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

public sealed class LogSummaryService : ILogSummaryService
{
    private readonly SqliteLogBufferService _buffer;
    private readonly IAIClient _aiClient;
    private readonly IMcpOrchestrator _mcp;
    private readonly ISettingsService _settings;

    public LogSummaryService(
        SqliteLogBufferService buffer, 
        IAIClient aiClient, 
        IMcpOrchestrator mcp, 
        ISettingsService settings)
    {
        _buffer = buffer;
        _aiClient = aiClient;
        _mcp = mcp;
        _settings = settings;
    }

    public async ValueTask<DailySummary> SummarizePeriodAsync(DateTime start, DateTime end)
    {
        var logs = await _buffer.GetLogsAsync(start, end);
        if (logs.Count == 0)
        {
            return new DailySummary(end.Date, "No activity recorded for this period.", [], 0);
        }

        var logText = new StringBuilder().Append("Activity Logs:\n").AppendLine();
        foreach (var entry in logs)
        {
            logText.AppendLine($"[{entry.Timestamp:HH:mm}] {entry.Text}");
        }

        var prompt = $@"Analyze the following activity logs and provide a structured summary for the date {end:yyyy-MM-dd}. 
Return your response as a JSON object with exactly these fields:
- SummaryText: A concise paragraph summarizing the day's main activities.
- KeyEvents: A list of the most important events or milestones identified in the logs.

Logs:
{logText}";

        var aiResponse = await _aiClient.SendPromptAsync(new AiRequest(prompt));
        var summary = ParseSummary(aiResponse.Content, logs.Count, end.Date);
        
        await PersistToObsidianAsync(summary);
        
        // Clean up old logs after successful summarization
        await _buffer.ClearLogsAsync(end);

        return summary;
    }

    private DailySummary ParseSummary(string content, int count, DateTime date)
    {
        try 
        {
            // Simple JSON extraction from AI response which might contain markdown blocks
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}') + 1;
            if (jsonStart == -1 || jsonEnd == 0) throw new Exception("No JSON found");

            var json = content[jsonStart..jsonEnd];
            var result = JsonSerializer.Deserialize<DailySummaryDto>(json);
            return new DailySummary(date, result?.SummaryText ?? "Unable to summarize", result?.KeyEvents ?? [], count);
        }
        catch 
        {
            return new DailySummary(date, content, ["Error parsing AI response"], count);
        }
    }

    private async ValueTask PersistToObsidianAsync(DailySummary summary)
    {
        var dateStr = summary.Date.ToString("yyyy-MM-dd");
        var path = $"wiki/projects/QuantumZ/logs/{dateStr}.md";
        
        var content = new StringBuilder();
        content.AppendLine("---");
        content.AppendLine($"title: Activity Summary {dateStr}");
        content.AppendLine("type: note");
        content.AppendLine("tags:");
        content.AppendLine("  - activity");
        content.AppendLine("  - summary");
        content.AppendLine("  - QuantumZ");
        content.AppendLine("source: codebase");
        content.AppendLine("---");
        content.AppendLine();
        content.AppendLine($"# Daily Activity Summary - {dateStr}");
        content.AppendLine();
        content.AppendLine("## Overview");
        content.AppendLine(summary.SummaryText);
        content.AppendLine();
        content.AppendLine("## Key Events");
        foreach (var eventItem in summary.KeyEvents)
        {
            content.AppendLine($"- {eventItem}");
        }
        content.AppendLine();
        content.AppendLine($"**Total fragments processed:** {summary.TotalFragmentsProcessed}");

        var args = JsonSerializer.Serialize(new 
        { 
            path = path, 
            content = content.ToString() 
        });

        await _mcp.ExecuteToolAsync("obsidian_write_note", args);
    }

    private record DailySummaryDto(string SummaryText, List<string> KeyEvents);
}
