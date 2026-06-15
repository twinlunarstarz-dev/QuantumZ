using System;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Provides audio level events for UI visualization.
/// </summary>
public interface IAudioVisualizer
{
    /// <summary>
    /// Fired when the audio level changes (0.0 to 1.0).
    /// </summary>
    event EventHandler<double>? AudioLevelChanged;

    /// <summary>
    /// Fired when the listening state changes.
    /// </summary>
    event EventHandler<ListeningState>? StateChanged;

    /// <summary>
    /// Fired when voice activity detection status changes (e.g., user is speaking).
    /// </summary>
    event EventHandler<bool>? ActivityDetectedChanged;

    /// <summary>
    /// Reports a new audio level to subscribers.
    /// </summary>
    void ReportAudioLevel(double level);

    /// <summary>
    /// Reports a state change to subscribers.
    /// </summary>
    void ReportState(ListeningState state);

    /// <summary>
    /// Reports voice activity detection status to subscribers.
    /// </summary>
    void ReportActivityDetected(bool detected);
}

public enum ListeningState
{
    Idle,
    Listening,
    Processing,
    Speaking
}
