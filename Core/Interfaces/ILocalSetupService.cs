using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Provides release-safe local setup checklist and model installation orchestration.
/// </summary>
public interface ILocalSetupService
{
    /// <summary>
    /// Gets the current local setup checklist, including asset, runtime, fallback, and gated-model status.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels checklist evaluation.</param>
    /// <returns>The current checklist items for local setup.</returns>
    ValueTask<IReadOnlyList<SetupChecklistItem>> GetChecklistAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs or selects one local setup item from the catalog.
    /// </summary>
    /// <param name="itemId">The catalog item identifier.</param>
    /// <param name="authToken">A temporary authentication token for gated downloads; it is not persisted.</param>
    /// <param name="progress">Optional progress reporter from <c>0.0</c> to <c>1.0</c>.</param>
    /// <param name="cancellationToken">A token that cancels installation.</param>
    /// <returns>The installation result.</returns>
    ValueTask<InstallResult> InstallAsync(string itemId, string? authToken = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conservatively verifies whether all required local assets and native runtimes are present or release-safe fallbacks are selected.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels readiness evaluation.</param>
    /// <returns><c>true</c> only when local setup can truthfully be marked complete.</returns>
    ValueTask<bool> AreRequiredAssetsReadyAsync(CancellationToken cancellationToken = default);
}
