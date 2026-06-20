namespace QuantumZ.Core.Models;

/// <summary>
/// Identifies an on-device native runtime required by one pipeline capability.
/// </summary>
public enum NativeRuntimeKind
{
    /// <summary>Local large-language-model inference runtime.</summary>
    Llm,

    /// <summary>Local speech-to-text inference runtime.</summary>
    Stt,

    /// <summary>Local text-to-speech inference runtime.</summary>
    Tts,

    /// <summary>Local voice-activity-detection runtime.</summary>
    Vad
}

/// <summary>
/// Describes a packaged native shared library expected by QuantumZ for on-device AI.
/// </summary>
public sealed record NativeRuntimeRequirement
{
    /// <summary>Gets the native runtime category.</summary>
    public required NativeRuntimeKind Kind { get; init; }

    /// <summary>Gets the logical library name used by <c>NativeLibrary.TryLoad</c>, for example <c>quantumz_llama</c>.</summary>
    public required string LibraryName { get; init; }

    /// <summary>Gets the Android APK shared-library file name, for example <c>libquantumz_llama.so</c>.</summary>
    public required string AndroidFileName { get; init; }

    /// <summary>Gets the pipeline capability enabled by this runtime.</summary>
    public required ProviderCapability Capability { get; init; }

    /// <summary>Gets a value indicating whether local setup completion must block when this runtime is absent.</summary>
    public required bool RequiredForOnDeviceSetup { get; init; }

    /// <summary>Gets the human-readable runtime purpose and wrapper contract expectation.</summary>
    public required string Description { get; init; }
}

/// <summary>
/// Reports whether a native runtime requirement is currently satisfied by the app package.
/// </summary>
public sealed record NativeRuntimeStatus
{
    /// <summary>Gets the native runtime category.</summary>
    public required NativeRuntimeKind Kind { get; init; }

    /// <summary>Gets the logical library name used by <c>NativeLibrary.TryLoad</c>.</summary>
    public required string LibraryName { get; init; }

    /// <summary>Gets the Android APK shared-library file name.</summary>
    public required string AndroidFileName { get; init; }

    /// <summary>Gets the pipeline capability enabled by this runtime.</summary>
    public required ProviderCapability Capability { get; init; }

    /// <summary>Gets a value indicating whether the runtime is detectable in the current app package.</summary>
    public required bool IsPackaged { get; init; }

    /// <summary>Gets a user-facing runtime availability description.</summary>
    public required string StatusText { get; init; }
}
