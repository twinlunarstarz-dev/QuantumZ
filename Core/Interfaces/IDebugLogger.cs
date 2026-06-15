namespace QuantumZ.Core.Interfaces;

using QuantumZ.Core.Models;

using System.Collections.ObjectModel;

/// <summary>
/// Captures and streams diagnostic events from the audio and AI pipeline.
/// </summary>
public interface IDebugLogger
{
    /// <summary>
    /// Gets the bounded in-memory log collection used by the debug overlay UI.
    /// </summary>
    ObservableCollection<DebugEvent> Events { get; }

    /// <summary>
    /// Gets the real-time stream of debug events for overlay and diagnostic subscribers.
    /// </summary>
    IObservable<DebugEvent> EventStream { get; }

    /// <summary>
    /// Emits a fully structured debug event.
    /// </summary>
    void LogEvent(DebugEvent @event);

    /// <summary>
    /// Emits a text debug event for the specified pipeline component.
    /// </summary>
    void Log(string component, string message, LogLevel level = LogLevel.Info);

    /// <summary>
    /// Emits a structured state change event for the specified pipeline component.
    /// </summary>
    void LogStateChange(string component, string state, object? payload = null);

    /// <summary>
    /// Clears buffered debug events from the overlay collection.
    /// </summary>
    void ClearLogs();

    /// <summary>
    /// Clears buffered debug events from the overlay collection.
    /// </summary>
    void Clear();
}
