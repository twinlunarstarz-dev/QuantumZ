using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Manages local Obsidian-compatible activity memory.
/// </summary>
public interface IMemoryService
{
    /// <summary>
    /// Stores a daily summary in local Obsidian-compatible markdown.
    /// </summary>
    ValueTask StoreSummaryAsync(DailySummary summary, CancellationToken ct = default);

    /// <summary>
    /// Retrieves summaries for a date range.
    /// </summary>
    ValueTask<List<DailySummary>> GetSummariesAsync(DateTime start, DateTime end, CancellationToken ct = default);

    /// <summary>
    /// Searches local memory for entries matching a query.
    /// </summary>
    ValueTask<List<MemorySearchResult>> SearchMemoryAsync(string query, int limit = 10, CancellationToken ct = default);

    /// <summary>
    /// Returns the file path of the local vault root.
    /// </summary>
    string LocalVaultPath { get; }
}

public record MemorySearchResult(
    string FilePath,
    string Title,
    string Excerpt,
    DateTime Date,
    double RelevanceScore
);
