using System;

namespace QuantumZ.Core.Models;

/// <summary>
/// Represents one observable event emitted by the QuantumZ audio and AI pipeline.
/// </summary>
public record DebugEvent(
    DateTime Timestamp,
    string Component,
    LogLevel Level,
    string Message,
    object? Payload = null);
