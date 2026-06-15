using System.Text;
using System.Text.RegularExpressions;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Local Obsidian-compatible memory storage. Writes markdown with frontmatter
/// and can optionally sync to an MCP-managed Obsidian vault.
/// </summary>
public sealed class ObsidianMemoryService(ISettingsService settings) : IMemoryService
{
    public string LocalVaultPath => settings.ObsidianVaultPath;

    public async ValueTask StoreSummaryAsync(DailySummary summary, CancellationToken ct = default)
    {
        var vaultDir = LocalVaultPath;
        Directory.CreateDirectory(vaultDir);

        var dateStr = summary.Date.ToString("yyyy-MM-dd");
        var filePath = Path.Combine(vaultDir, $"{dateStr}.md");

        var tags = string.Join(", ", summary.KeyEvents.Take(5).Select(e => $"\"{SanitizeTag(e)}\""));

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: Activity Summary {dateStr}");
        sb.AppendLine("type: note");
        sb.AppendLine("tags:");
        sb.AppendLine("  - activity");
        sb.AppendLine("  - summary");
        sb.AppendLine("  - QuantumZ");
        if (!string.IsNullOrWhiteSpace(tags))
        {
            sb.AppendLine($"keywords: [{tags}]");
        }
        sb.AppendLine($"total_fragments: {summary.TotalFragmentsProcessed}");
        sb.AppendLine("source: codebase");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# Daily Activity Summary — {dateStr}");
        sb.AppendLine();
        sb.AppendLine("## Overview");
        sb.AppendLine(summary.SummaryText);
        sb.AppendLine();
        sb.AppendLine("## Key Events");
        foreach (var evt in summary.KeyEvents)
        {
            sb.AppendLine($"- {evt}");
        }
        sb.AppendLine();
        sb.AppendLine($"**Total fragments processed:** {summary.TotalFragmentsProcessed}");

        await File.WriteAllTextAsync(filePath, sb.ToString(), ct);
    }

    public async ValueTask<List<DailySummary>> GetSummariesAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        var results = new List<DailySummary>();
        var vaultDir = LocalVaultPath;
        if (!Directory.Exists(vaultDir)) return results;

        foreach (var file in Directory.EnumerateFiles(vaultDir, "*.md"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (!DateTime.TryParseExact(fileName, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fileDate))
                continue;

            if (fileDate < start || fileDate > end) continue;

            var content = await File.ReadAllTextAsync(file, ct);
            var summary = ParseMarkdownSummary(content, fileDate);
            results.Add(summary);
        }

        return results.OrderBy(r => r.Date).ToList();
    }

    public ValueTask<List<MemorySearchResult>> SearchMemoryAsync(string query, int limit = 10, CancellationToken ct = default)
    {
        var results = new List<MemorySearchResult>();
        var vaultDir = LocalVaultPath;
        if (!Directory.Exists(vaultDir)) return ValueTask.FromResult(results);

        var lowerQuery = query.ToLowerInvariant();
        foreach (var file in Directory.EnumerateFiles(vaultDir, "*.md"))
        {
            var content = File.ReadAllText(file);
            if (!content.ToLowerInvariant().Contains(lowerQuery)) continue;

            var title = Path.GetFileNameWithoutExtension(file);
            var excerpt = ExtractExcerpt(content, lowerQuery);
            if (DateTime.TryParseExact(title, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date))
            {
                results.Add(new MemorySearchResult(file, title, excerpt, date, 1.0));
            }
        }

        return ValueTask.FromResult(results.Take(limit).ToList());
    }

    private static DailySummary ParseMarkdownSummary(string markdown, DateTime date)
    {
        var lines = markdown.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var summaryText = new StringBuilder();
        var keyEvents = new List<string>();
        var inOverview = false;
        var inKeyEvents = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## Overview"))
            {
                inOverview = true;
                inKeyEvents = false;
                continue;
            }
            if (trimmed.StartsWith("## Key Events"))
            {
                inOverview = false;
                inKeyEvents = true;
                continue;
            }
            if (trimmed.StartsWith("## ") || trimmed.StartsWith("# "))
            {
                inOverview = false;
                inKeyEvents = false;
                continue;
            }
            if (trimmed.StartsWith("**")) continue;
            if (trimmed.StartsWith("---")) continue;

            if (inOverview)
            {
                summaryText.AppendLine(trimmed);
            }
            else if (inKeyEvents && trimmed.StartsWith("- "))
            {
                keyEvents.Add(trimmed[2..].Trim());
            }
        }

        return new DailySummary(date, summaryText.ToString().Trim(), keyEvents, 0);
    }

    private static string ExtractExcerpt(string content, string lowerQuery)
    {
        var idx = content.ToLowerInvariant().IndexOf(lowerQuery, StringComparison.Ordinal);
        if (idx < 0) return content.Length > 120 ? content[..120] + "..." : content;

        var start = Math.Max(0, idx - 40);
        var length = Math.Min(120, content.Length - start);
        var excerpt = content.Substring(start, length);
        return $"...{excerpt}...";
    }

    private static string SanitizeTag(string input)
    {
        var sanitized = Regex.Replace(input.ToLowerInvariant(), "[^a-z0-9 ]", "");
        return sanitized.Trim().Replace(' ', '-');
    }
}
