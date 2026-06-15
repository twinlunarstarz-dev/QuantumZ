using System;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Provides real-time speech transcription state and events for UI updates.
/// </summary>
public interface ISpeechStateService
{
    /// <summary>
    /// The current raw transcription text being detected by the STT engine.
    /// </summary>
    string CurrentTranscription { get; }

    /// <summary>
    /// Fired when the real-time transcription changes.
    /// </summary>
    event EventHandler<string>? TranscriptionChanged;

    /// <summary>
    /// Updates the current transcription and notifies subscribers.
    /// </summary>
    /// <param name="text">The new transcription text.</param>
    void UpdateTranscription(string text);

    /// <summary>
    /// Clears the current transcription (e.g., when starting a new utterance).
    /// </summary>
    void ClearTranscription();
}
