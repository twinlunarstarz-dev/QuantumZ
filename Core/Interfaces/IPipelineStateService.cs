using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

/// <summary>Event args carrying the old and new pipeline states plus optional context message.</summary>
public sealed class PipelineStateChangedArgs(PipelineState previous, PipelineState current, string? message = null) : EventArgs
{
    /// <summary>The pipeline state prior to the transition.</summary>
    public PipelineState Previous { get; } = previous;

    /// <summary>The pipeline state after the transition.</summary>
    public PipelineState Current { get; } = current;

    /// <summary>Optional contextual message describing the transition.</summary>
    public string? Message { get; } = message;

    /// <summary>UTC timestamp at which the transition occurred.</summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Manages and broadcasts the current pipeline state to all subscribers.
/// The UI and audio services use this as the single source of truth for assistant state.
/// </summary>
public interface IPipelineStateService
{
    /// <summary>The current pipeline state.</summary>
    PipelineState CurrentState { get; }

    /// <summary>Fired on the UI thread whenever the pipeline state transitions.</summary>
    event EventHandler<PipelineStateChangedArgs> StateChanged;

    /// <summary>Transitions to <paramref name="newState"/> and fires <see cref="StateChanged"/>.</summary>
    void TransitionTo(PipelineState newState, string? message = null);

    /// <summary>Returns true if the current state is one of the provided states.</summary>
    bool IsInState(params PipelineState[] states);
}
