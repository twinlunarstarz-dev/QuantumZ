using Microsoft.Maui.ApplicationModel;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Thread-safe implementation of <see cref="IPipelineStateService"/> that keeps a single
/// authoritative pipeline state and dispatches <see cref="StateChanged"/> on the UI thread.
/// </summary>
public sealed class PipelineStateService(IDebugLogger logger) : IPipelineStateService, IDisposable
{
    private readonly object _sync = new();
    private PipelineState _currentState = PipelineState.Idle;

    /// <inheritdoc />
    public PipelineState CurrentState
    {
        get { lock (_sync) { return _currentState; } }
    }

    /// <inheritdoc />
    public event EventHandler<PipelineStateChangedArgs>? StateChanged;

    /// <inheritdoc />
    public void TransitionTo(PipelineState newState, string? message = null)
    {
        PipelineState previous;

        lock (_sync)
        {
            if (newState == _currentState)
                return;

            previous = _currentState;
            _currentState = newState;
        }

        logger.Log(
            "Pipeline",
            $"[Pipeline] {previous} → {newState}{(message != null ? $": {message}" : "")}",
            LogLevel.Debug);

        var args = new PipelineStateChangedArgs(previous, newState, message);
        MainThread.BeginInvokeOnMainThread(() => StateChanged?.Invoke(this, args));
    }

    /// <inheritdoc />
    public bool IsInState(params PipelineState[] states)
    {
        PipelineState current;
        lock (_sync) { current = _currentState; }
        foreach (var s in states)
            if (s == current) return true;
        return false;
    }

    /// <summary>Clears all <see cref="StateChanged"/> subscribers.</summary>
    public void Dispose() => StateChanged = null;
}
