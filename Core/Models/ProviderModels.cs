namespace QuantumZ.Core.Models;

/// <summary>
/// Identifies the execution location for a capability provider.
/// </summary>
public enum ProviderLocation
{
    Remote,
    Hybrid,
    Local,
    BuiltIn
}

/// <summary>
/// Identifies the pipeline capability exposed by a provider.
/// </summary>
public enum ProviderCapability
{
    Vad,
    Stt,
    Llm,
    Tts,
    Embedding,
    Reranker,
    Vision
}

/// <summary>
/// Describes a provider registered with the provider router.
/// </summary>
public record ProviderDescriptor(
    string Id,
    string DisplayName,
    ProviderCapability Capability,
    ProviderLocation Location,
    int Priority = 0,
    ThermalModelTier? Tier = null);

/// <summary>
/// Describes an AI model discovered from local storage, OpenAI-compatible endpoints, or provider catalogs.
/// </summary>
public sealed record ModelProfile(
    string Id,
    string DisplayName,
    ProviderCapability Capability,
    string Provider,
    ProviderLocation Location,
    bool IsAvailable,
    string? Endpoint = null,
    string? LocalPath = null,
    string? Quantization = null,
    bool SupportsToolCalling = false,
    bool SupportsVision = false,
    int? ContextLength = null,
    string License = "MIT/Apache-compatible implementation")
{
    /// <summary>
    /// Gets a stable key for de-duplicating equivalent models from the same source.
    /// </summary>
    public string RegistryKey => $"{Capability}:{Provider}:{Location}:{Id}";
}

/// <summary>
/// Represents the current VAD activity transition for a PCM audio window.
/// </summary>
public enum VadActivityState
{
    /// <summary>
    /// No speech is active in the current window.
    /// </summary>
    Silence,

    /// <summary>
    /// Speech has just crossed the provider's start threshold.
    /// </summary>
    SpeechStarted,

    /// <summary>
    /// Speech remains active after a previous start event.
    /// </summary>
    SpeechContinued,

    /// <summary>
    /// Speech has ended after enough trailing silence.
    /// </summary>
    SpeechEnded
}

/// <summary>
/// Represents a voice activity detection result for one PCM audio window.
/// </summary>
public record VadResult(
    bool IsSpeechDetected,
    double Confidence,
    double Rms,
    VadActivityState ActivityState = VadActivityState.Silence);

/// <summary>
/// Event payload emitted when a VAD provider detects a speech activity transition.
/// </summary>
public sealed record VadActivityEventArgs(
    VadActivityState State,
    VadResult Result,
    DateTimeOffset Timestamp);
