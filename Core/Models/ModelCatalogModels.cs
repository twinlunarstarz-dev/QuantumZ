namespace QuantumZ.Core.Models;

/// <summary>
/// Describes the installation state for a local model catalog entry.
/// </summary>
public enum ModelInstallStatus
{
    /// <summary>The model asset has not been installed.</summary>
    NotInstalled,

    /// <summary>The model asset or release-safe fallback is available.</summary>
    Installed,

    /// <summary>The model asset is currently downloading.</summary>
    Downloading,

    /// <summary>The model requires an authentication token before installation can proceed.</summary>
    RequiresAuth,

    /// <summary>The model file is present, but a required packaged native runtime is missing.</summary>
    RequiresRuntime,

    /// <summary>The model installation failed.</summary>
    Failed
}

/// <summary>
/// Describes a release-vetted or provisional model asset that can appear in local setup.
/// </summary>
public sealed record ModelCatalogEntry
{
    /// <summary>Gets the stable catalog identifier used by setup commands.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the pipeline capability served by this model or fallback.</summary>
    public required ProviderCapability Capability { get; init; }

    /// <summary>Gets the user-facing model or fallback name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the upstream repository or platform source for the entry.</summary>
    public required string SourceRepository { get; init; }

    /// <summary>Gets the direct model download URL, or an empty string when no verified direct download is available.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Gets the expected local file name, or an empty string for built-in fallbacks.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the approximate expected download size in bytes when known.</summary>
    public required long? ExpectedBytes { get; init; }

    /// <summary>Gets the app-data-relative target path for the asset, or an empty string for built-in fallbacks.</summary>
    public required string TargetRelativePath { get; init; }

    /// <summary>Gets the license note that must be shown or considered before enabling the entry.</summary>
    public required string License { get; init; }

    /// <summary>Gets a value indicating whether a user-provided token is required to download this entry.</summary>
    public required bool RequiresAuth { get; init; }

    /// <summary>Gets a value indicating whether upstream license acceptance is required before download.</summary>
    public required bool RequiresLicenseAcceptance { get; init; }

    /// <summary>Gets a value indicating whether the entry is provisional and not guaranteed for release.</summary>
    public required bool IsProvisional { get; init; }

    /// <summary>Gets a value indicating whether this entry must be ready before local setup can complete.</summary>
    public required bool IsRequiredForLocalSetup { get; init; }

    /// <summary>Gets additional release-readiness notes for the entry.</summary>
    public required string? Notes { get; init; }
}

/// <summary>
/// Represents one user-facing local setup checklist row.
/// </summary>
public sealed record SetupChecklistItem
{
    /// <summary>Gets the catalog item identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the user-facing checklist label.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the pipeline capability associated with the checklist item.</summary>
    public required ProviderCapability Capability { get; init; }

    /// <summary>Gets a value indicating whether the item is required for local setup completion.</summary>
    public required bool IsRequired { get; init; }

    /// <summary>Gets a value indicating whether the asset or fallback is ready.</summary>
    public required bool IsInstalled { get; init; }

    /// <summary>Gets the current download progress from <c>0.0</c> to <c>1.0</c>.</summary>
    public required double Progress { get; init; }

    /// <summary>Gets the current installation status.</summary>
    public required ModelInstallStatus Status { get; init; }

    /// <summary>Gets the user-facing status message.</summary>
    public required string StatusText { get; init; }
}

/// <summary>
/// Reports the result of a local model installation attempt.
/// </summary>
/// <param name="Success">Indicates whether the item was installed or selected successfully.</param>
/// <param name="Message">A user-facing result message.</param>
/// <param name="Status">The final install status.</param>
/// <param name="LocalPath">The installed local asset path, when applicable.</param>
public sealed record InstallResult(bool Success, string Message, ModelInstallStatus Status, string? LocalPath = null);
