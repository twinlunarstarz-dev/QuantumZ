using QuantumZ.Core.Models;

namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Base contract for a provider managed by the provider router.
/// </summary>
public interface IProvider
{
    /// <summary>
    /// Gets static metadata describing the provider and its execution location.
    /// </summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Gets whether the provider has enough local configuration to attempt execution.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Performs a lightweight availability check before routing work to the provider.
    /// </summary>
    ValueTask<bool> IsAvailableAsync(CancellationToken ct = default);
}
