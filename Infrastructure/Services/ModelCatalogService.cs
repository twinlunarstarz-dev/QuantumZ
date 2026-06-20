using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Provides the release-readiness local model catalog used by setup.
/// </summary>
public sealed class ModelCatalogService
{
    /// <summary>Gets the release-readiness model catalog.</summary>
    public static IReadOnlyList<ModelCatalogEntry> Catalog { get; } =
    [
        new ModelCatalogEntry
        {
            Id = "whisper-tiny",
            Capability = ProviderCapability.Stt,
            DisplayName = "Whisper tiny STT (ggml)",
            SourceRepository = "ggerganov/whisper.cpp",
            DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",
            FileName = "ggml-tiny.bin",
            ExpectedBytes = 75L * 1024L * 1024L,
            TargetRelativePath = Path.Combine("models", "stt", "whisper", "ggml-tiny.bin"),
            License = "MIT implementation; model weights license must be reviewed by user",
            RequiresAuth = false,
            RequiresLicenseAcceptance = false,
            IsProvisional = false,
            IsRequiredForLocalSetup = true,
            Notes = "Requires the packaged QuantumZ whisper wrapper runtime libquantumz_whisper.so on Android 10+ before it can be used."
        },
        new ModelCatalogEntry
        {
            Id = "whisper-small",
            Capability = ProviderCapability.Stt,
            DisplayName = "Whisper small STT (ggml)",
            SourceRepository = "ggerganov/whisper.cpp",
            DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
            FileName = "ggml-small.bin",
            ExpectedBytes = 460L * 1024L * 1024L,
            TargetRelativePath = Path.Combine("models", "stt", "whisper", "ggml-small.bin"),
            License = "MIT implementation; model weights license must be reviewed by user",
            RequiresAuth = false,
            RequiresLicenseAcceptance = false,
            IsProvisional = false,
            IsRequiredForLocalSetup = false,
            Notes = "Optional larger STT model. Requires the packaged QuantumZ whisper wrapper runtime libquantumz_whisper.so on Android 10+."
        },
        new ModelCatalogEntry
        {
            Id = "silero-vad-v5-onnx",
            Capability = ProviderCapability.Vad,
            DisplayName = "Silero VAD ONNX v5",
            SourceRepository = "snakers4/silero-vad",
            DownloadUrl = string.Empty,
            FileName = "silero_vad.onnx",
            ExpectedBytes = null,
            TargetRelativePath = Path.Combine("models", "vad", "silero_vad.onnx"),
            License = "Silero VAD license must be verified before enabling packaged distribution",
            RequiresAuth = false,
            RequiresLicenseAcceptance = true,
            IsProvisional = true,
            IsRequiredForLocalSetup = false,
            Notes = "Direct ONNX v5 URL is intentionally unset until verified; built-in RMS VAD is the release-safe fallback."
        },
        new ModelCatalogEntry
        {
            Id = "rms-vad-built-in",
            Capability = ProviderCapability.Vad,
            DisplayName = "Built-in RMS VAD fallback",
            SourceRepository = "QuantumZ",
            DownloadUrl = string.Empty,
            FileName = string.Empty,
            ExpectedBytes = null,
            TargetRelativePath = string.Empty,
            License = "QuantumZ built-in signal processing fallback",
            RequiresAuth = false,
            RequiresLicenseAcceptance = false,
            IsProvisional = false,
            IsRequiredForLocalSetup = true,
            Notes = "Release-safe VAD fallback. This is voice activity detection, not wake phrase recognition."
        },
        new ModelCatalogEntry
        {
            Id = "android-tts-built-in",
            Capability = ProviderCapability.Tts,
            DisplayName = "Android built-in TTS fallback",
            SourceRepository = "Android platform TextToSpeech",
            DownloadUrl = string.Empty,
            FileName = string.Empty,
            ExpectedBytes = null,
            TargetRelativePath = string.Empty,
            License = "Android platform service",
            RequiresAuth = false,
            RequiresLicenseAcceptance = false,
            IsProvisional = false,
            IsRequiredForLocalSetup = true,
            Notes = "Release-safe TTS fallback; no local voice model download is required."
        },
        new ModelCatalogEntry
        {
            Id = "piper-voice-optional",
            Capability = ProviderCapability.Tts,
            DisplayName = "Optional Piper local voice",
            SourceRepository = "rhasspy/piper voices",
            DownloadUrl = string.Empty,
            FileName = "voice.onnx",
            ExpectedBytes = null,
            TargetRelativePath = Path.Combine("models", "tts", "piper", "voice.onnx"),
            License = "Voice-dependent; verify the selected voice license before enabling",
            RequiresAuth = false,
            RequiresLicenseAcceptance = true,
            IsProvisional = true,
            IsRequiredForLocalSetup = false,
            Notes = "Optional only. Requires the packaged QuantumZ Piper wrapper runtime libquantumz_piper.so on Android 10+ and a verified compatible voice license."
        },
        new ModelCatalogEntry
        {
            Id = "gemma-provisional-q4",
            Capability = ProviderCapability.Llm,
            DisplayName = "Gemma Q4 local LLM (provisional)",
            SourceRepository = "google/gemma",
            DownloadUrl = string.Empty,
            FileName = "gemma-provisional-q4.gguf",
            ExpectedBytes = null,
            TargetRelativePath = Path.Combine("models", "llm", "gemma-provisional-q4.gguf"),
            License = "Gemma license; Hugging Face access and license acceptance required before download",
            RequiresAuth = true,
            RequiresLicenseAcceptance = true,
            IsProvisional = true,
            IsRequiredForLocalSetup = true,
            Notes = "User must provide a temporary Hugging Face token after accepting the model license, and the packaged QuantumZ llama wrapper runtime libquantumz_llama.so must be present. No token is persisted."
        }
    ];

    /// <summary>Gets the configured catalog entries.</summary>
    public IReadOnlyList<ModelCatalogEntry> Entries => Catalog;
}
