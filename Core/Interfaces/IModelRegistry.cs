using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Discovers and resolves capability-based model profiles without hardcoding model names.
/// </summary>
public interface IModelRegistry
{
    /// <summary>
    /// Discovers model profiles from local storage, llama.cpp servers, and configured provider endpoints.
    /// </summary>
    ValueTask<IReadOnlyList<ModelProfile>> DiscoverModelsAsync(bool forceRefresh = false, CancellationToken ct = default);

    /// <summary>
    /// Gets all discovered model profiles matching a pipeline capability.
    /// </summary>
    ValueTask<IReadOnlyList<ModelProfile>> GetModelsAsync(ProviderCapability capability, CancellationToken ct = default);

    /// <summary>
    /// Resolves a preferred model for a capability using user selection first, then availability and location policy.
    /// </summary>
    ValueTask<ModelProfile?> ResolvePreferredModelAsync(ProviderCapability capability, string? preferredModelId = null, CancellationToken ct = default);
}
