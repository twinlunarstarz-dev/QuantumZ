using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

public interface ILogSummaryService
{
    /// <summary>
    /// Triggers a summarization of the captured logs for the given period and persists it to memory.
    /// </summary>
    ValueTask<DailySummary> SummarizePeriodAsync(DateTime start, DateTime end);
}