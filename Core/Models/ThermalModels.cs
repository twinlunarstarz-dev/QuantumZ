namespace QuantumZ.Core.Models;

/// <summary>
/// Describes the current thermal severity level for the device.
/// </summary>
public enum ThermalLevel
{
    /// <summary>
    /// Device temperatures are within normal operating range.
    /// </summary>
    Normal,

    /// <summary>
    /// Device is warm; prefer lighter models to prevent further heating.
    /// </summary>
    Warning,

    /// <summary>
    /// Device is hot; restrict execution to the smallest available models.
    /// </summary>
    Critical
}

/// <summary>
/// Represents a thermal classification tier for an LLM model based on capability and expected heat output.
/// </summary>
public enum ThermalModelTier
{
    /// <summary>
    /// Largest remote models (e.g., 31B+ parameters).
    /// </summary>
    HighCapacity,

    /// <summary>
    /// Mid-size remote models (e.g., ~12B parameters).
    /// </summary>
    MidCapacity,

    /// <summary>
    /// Medium local models (e.g., Gemma 4 E4B, ~4B parameters).
    /// </summary>
    LocalMedium,

    /// <summary>
    /// Smallest local models (e.g., Phi Mini).
    /// </summary>
    LocalSmallMini
}

/// <summary>
/// Snapshot of battery, CPU, and GPU temperatures at a point in time.
/// </summary>
public sealed record ThermalState(
    ThermalLevel Level,
    float BatteryTemperatureC,
    float? CpuTemperatureC,
    float? GpuTemperatureC,
    DateTimeOffset Timestamp);
