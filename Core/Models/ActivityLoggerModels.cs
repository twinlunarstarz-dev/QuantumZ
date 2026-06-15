namespace QuantumZ.Core.Models;

public record LogEntry(
    Guid Id,
    string Text,
    DateTime Timestamp,
    string? Metadata = null
);

public record DailySummary(
    DateTime Date,
    string SummaryText,
    List<string> KeyEvents,
    int TotalFragmentsProcessed
);