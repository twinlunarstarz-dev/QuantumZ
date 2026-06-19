namespace QuantumZ.Core.Models;

/// <summary>Represents the current operational state of the voice assistant pipeline.</summary>
public enum PipelineState
{
    /// <summary>Microphone is off; assistant is completely inactive.</summary>
    Idle,
    /// <summary>Ring buffer active; ONNX model scoring every 80 ms chunk for the trigger phrase.</summary>
    ListeningForTrigger,
    /// <summary>Wake word confidence ≥ threshold; pre-roll audio snapshotted; waiting for query.</summary>
    TriggerDetected,
    /// <summary>Accumulating user query audio until end-of-speech silence detected.</summary>
    RecordingQuery,
    /// <summary>Full audio (pre-roll + query) is being transcribed by the STT engine.</summary>
    ProcessingSTT,
    /// <summary>Transcription sent to LLM; tool-call loop running.</summary>
    ProcessingLLM,
    /// <summary>TTS playback of the LLM response is in progress.</summary>
    Speaking,
    /// <summary>A recoverable error occurred; pipeline auto-recovers to ListeningForTrigger after 3 s.</summary>
    Error
}
