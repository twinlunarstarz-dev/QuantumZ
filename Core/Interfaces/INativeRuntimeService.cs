using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Provides package-level detection for native on-device AI runtimes.
/// </summary>
public interface INativeRuntimeService
{
    /// <summary>
    /// Gets the native shared-library requirements known to this application release.
    /// </summary>
    /// <returns>The expected native runtime requirements.</returns>
    IReadOnlyList<NativeRuntimeRequirement> GetRequirements();

    /// <summary>
    /// Gets current package-detection status for all known native runtimes.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels runtime probing.</param>
    /// <returns>Runtime availability rows for setup and diagnostics.</returns>
    ValueTask<IReadOnlyList<NativeRuntimeStatus>> GetStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether one native runtime kind is available in the current app package.
    /// </summary>
    /// <param name="kind">The native runtime kind to probe.</param>
    /// <param name="cancellationToken">A token that cancels runtime probing.</param>
    /// <returns><c>true</c> when the runtime is detectable as packaged and loadable.</returns>
    ValueTask<bool> IsRuntimeAvailableAsync(NativeRuntimeKind kind, CancellationToken cancellationToken = default);
}
