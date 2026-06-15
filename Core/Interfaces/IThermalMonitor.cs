using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Monitors device thermal state and notifies subscribers when temperatures cross thresholds.
/// </summary>
public interface IThermalMonitor
{
    /// <summary>
    /// Gets the most recently sampled thermal state.
    /// </summary>
    ThermalState CurrentState { get; }

    /// <summary>
    /// Raised when the sampled thermal state changes.
    /// </summary>
    event EventHandler<ThermalState>? StateChanged;

    /// <summary>
    /// Performs an immediate thermal sample and updates <see cref="CurrentState"/>.
    /// </summary>
    ValueTask<ThermalState> SampleAsync(CancellationToken ct = default);
}
