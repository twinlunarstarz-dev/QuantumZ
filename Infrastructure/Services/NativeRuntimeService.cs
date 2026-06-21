using System.Runtime.InteropServices;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Detects QuantumZ Android native wrapper libraries packaged with the application.
/// </summary>
/// <remarks>
/// These are stable QuantumZ C ABI wrapper libraries, not raw upstream project libraries.
/// Future wrappers should expose flat init/load/infer/free-style entry points while keeping
/// managed P/Invoke signatures insulated from upstream llama.cpp and whisper.cpp churn.
/// Piper TTS was dropped for v1; Android TTS is the on-device voice fallback.
/// </remarks>
public sealed class NativeRuntimeService(IDebugLogger logger) : INativeRuntimeService
{
    private static readonly NativeRuntimeRequirement[] Requirements =
    [
        new NativeRuntimeRequirement
        {
            Kind = NativeRuntimeKind.Llm,
            LibraryName = "quantumz_llama",
            AndroidFileName = "libquantumz_llama.so",
            Capability = ProviderCapability.Llm,
            RequiredForOnDeviceSetup = true,
            Description = "QuantumZ llama.cpp wrapper for local LLM init/load/infer/free operations."
        },
        new NativeRuntimeRequirement
        {
            Kind = NativeRuntimeKind.Stt,
            LibraryName = "quantumz_whisper",
            AndroidFileName = "libquantumz_whisper.so",
            Capability = ProviderCapability.Stt,
            RequiredForOnDeviceSetup = true,
            Description = "QuantumZ whisper.cpp wrapper for local STT init/load/transcribe/free operations."
        },
        // Piper TTS (NativeRuntimeKind.Tts) is not shipped in v1.
        // Android TTS is the on-device voice fallback.
    ];

    public IReadOnlyList<NativeRuntimeRequirement> GetRequirements() => Requirements;

    public ValueTask<IReadOnlyList<NativeRuntimeStatus>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<NativeRuntimeStatus> statuses = [.. Requirements.Select(BuildStatus)];
        return ValueTask.FromResult(statuses);
    }

    public ValueTask<bool> IsRuntimeAvailableAsync(NativeRuntimeKind kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requirement = Requirements.FirstOrDefault(candidate => candidate.Kind == kind);
        return ValueTask.FromResult(requirement is not null && IsPackaged(requirement));
    }

    private NativeRuntimeStatus BuildStatus(NativeRuntimeRequirement requirement)
    {
        var isPackaged = IsPackaged(requirement);
        return new NativeRuntimeStatus
        {
            Kind = requirement.Kind,
            LibraryName = requirement.LibraryName,
            AndroidFileName = requirement.AndroidFileName,
            Capability = requirement.Capability,
            IsPackaged = isPackaged,
            StatusText = BuildStatusText(requirement, isPackaged)
        };
    }

    private bool IsPackaged(NativeRuntimeRequirement requirement)
    {
        if (TryGetPackagedLibraryPath(requirement.AndroidFileName) is not null)
            return true;

        return TryLoadLibrary(requirement.LibraryName);
    }

    private string? TryGetPackagedLibraryPath(string androidFileName)
    {
#if ANDROID
        try
        {
            var nativeLibraryDir = global::Android.App.Application.Context.ApplicationInfo?.NativeLibraryDir;
            if (string.IsNullOrWhiteSpace(nativeLibraryDir) || !Directory.Exists(nativeLibraryDir))
                return null;

            var candidatePath = Path.Combine(nativeLibraryDir, androidFileName);
            return File.Exists(candidatePath) ? candidatePath : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.Log("NativeRuntime", $"Unable to inspect Android native library directory: {ex.Message}", LogLevel.Warning);
            return null;
        }
#else
        _ = androidFileName;
        return null;
#endif
    }

    private bool TryLoadLibrary(string libraryName)
    {
#if ANDROID
        try
        {
            if (!NativeLibrary.TryLoad(libraryName, out var handle))
                return false;

            NativeLibrary.Free(handle);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            logger.Log("NativeRuntime", $"Unable to load native runtime '{libraryName}': {ex.Message}", LogLevel.Warning);
            return false;
        }
#else
        _ = libraryName;
        return false;
#endif
    }

    private static string BuildStatusText(NativeRuntimeRequirement requirement, bool isPackaged)
    {
        if (isPackaged)
            return $"Packaged native runtime {requirement.AndroidFileName} is available.";

#if ANDROID
        return $"Packaged native runtime {requirement.AndroidFileName} is missing from the Android native library directory.";
#else
        return $"Native runtime {requirement.AndroidFileName} is only probed in Android package builds.";
#endif
    }
}
