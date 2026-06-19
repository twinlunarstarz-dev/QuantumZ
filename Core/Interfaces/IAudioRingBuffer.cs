namespace QuantumZ.Core.Interfaces;

/// <summary>Thread-safe circular buffer for continuous PCM16 audio capture.</summary>
public interface IAudioRingBuffer
{
    /// <summary>Writes a span of PCM16 samples into the ring buffer, overwriting oldest data when full.</summary>
    void Write(ReadOnlySpan<short> samples);

    /// <summary>Returns the most recent <paramref name="seconds"/> of audio as a chronologically ordered array.</summary>
    short[] ReadLast(float seconds);

    /// <summary>Returns all buffered samples in chronological order.</summary>
    short[] ReadAll();

    /// <summary>Resets the buffer to empty.</summary>
    void Clear();

    /// <summary>Number of valid samples currently in the buffer.</summary>
    int SampleCount { get; }

    /// <summary>Maximum capacity of the buffer in seconds.</summary>
    float CapacitySeconds { get; }
}
