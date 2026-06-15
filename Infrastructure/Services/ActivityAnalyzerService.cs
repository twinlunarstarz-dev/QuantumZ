using System.Text;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Optional service that analyzes recent activities and provides AI-generated
/// suggestions or warnings to the user.
/// </summary>
public sealed class ActivityAnalyzerService(
    IAIClient aiClient,
    IActivityLogger activityLogger,
    ISettingsService settings)
{
    public async ValueTask<AnalysisResult?> AnalyzeRecentPeriodAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        if (!settings.EnableActivityAnalysis)
            return null;

        var buffer = activityLogger as SqliteLogBufferService;
        if (buffer is null)
            return null;

        var logs = await buffer.GetLogsAsync(start, end);
        if (logs.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("Recent Activity Log:");
        foreach (var log in logs)
        {
            sb.AppendLine($"[{log.Timestamp:HH:mm}] {log.Text}");
        }

        var prompt = $"""
Analyze the following activity log for patterns, potential concerns, or helpful suggestions.
Return your response as JSON with exactly these fields:
- Suggestions: array of actionable suggestions for the user.
- Warnings: array of warnings about potential undesired outcomes.
- Mood: a single word describing the general tone of the period.

{sb}
""";

        var response = await aiClient.SendPromptAsync(new AiRequest(prompt, MaxTokens: 1024), ct);
        return ParseAnalysis(response.Content);
    }

    private static AnalysisResult ParseAnalysis(string content)
    {
        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}') + 1;
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content[jsonStart..jsonEnd];
                var dto = System.Text.Json.JsonSerializer.Deserialize<AnalysisDto>(json);
                return new AnalysisResult(
                    dto?.Suggestions ?? [],
                    dto?.Warnings ?? [],
                    dto?.Mood ?? "neutral"
                );
            }
        }
        catch { /* ignore parse errors */ }

        return new AnalysisResult([content], [], "neutral");
    }

    private record AnalysisDto(List<string> Suggestions, List<string> Warnings, string Mood);
}

public record AnalysisResult(
    List<string> Suggestions,
    List<string> Warnings,
    string Mood
);
