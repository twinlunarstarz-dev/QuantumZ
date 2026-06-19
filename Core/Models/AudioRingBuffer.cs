using QuantumZ.Core.Interfaces;

namespace QuantumZ.Core.Models;

/// <summary>
/// Thread-safe circular buffer for continuous PCM16 audio at 16 kHz mono.
/// Oldest samples are overwritten once the buffer reaches capacity.
/// </summary>
public sealed class AudioRingBuffer : IAudioRingBuffer
{
    private readonly object _sync = new();
    private readonly short[] _buffer;
    private readonly int _capacity;
    private readonly int _sampleRate;
    private readonly float _capacitySeconds;

    private int _writePos;
    private int _count;

    /// <summary>Creates a ring buffer sized for <paramref name="capacitySeconds"/> at <paramref name="sampleRate"/> Hz.</summary>
    /// <param name="capacitySeconds">Maximum amount of audio retained, in seconds.</param>
    /// <param name="sampleRate">Sample rate in Hz (defaults to 16 kHz).</param>
    public AudioRingBuffer(float capacitySeconds = 10f, int sampleRate = 16000)
    {
        if (capacitySeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(capacitySeconds), "Capacity must be greater than zero.");
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be greater than zero.");

        _capacitySeconds = capacitySeconds;
        _sampleRate = sampleRate;
        _capacity = (int)(capacitySeconds * sampleRate);
        if (_capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacitySeconds), "Computed capacity must be greater than zero.");

        _buffer = new short[_capacity];
    }

    /// <inheritdoc />
    public int SampleCount
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    /// <inheritdoc />
    public float CapacitySeconds => _capacitySeconds;

    /// <inheritdoc />
    public void Write(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty)
            return;

        lock (_sync)
        {
            // When the incoming data is at least the buffer's capacity, only the
            // most recent capacity-worth of samples can be retained.
            if (samples.Length >= _capacity)
            {
                samples[^_capacity..].CopyTo(_buffer);
                _writePos = 0;
                _count = _capacity;
                return;
            }

            foreach (short sample in samples)
            {
                _buffer[_writePos] = sample;
                _writePos = (_writePos + 1) % _capacity;
                if (_count < _capacity)
                    _count++;
            }
        }
    }

    /// <inheritdoc />
    public short[] ReadLast(float seconds)
    {
        lock (_sync)
        {
            int requested = seconds <= 0f ? 0 : (int)(seconds * _sampleRate);
            int n = Math.Min(requested, _count);
            return CopyMostRecent(n);
        }
    }

    /// <inheritdoc />
    public short[] ReadAll()
    {
        lock (_sync)
        {
            return CopyMostRecent(_count);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_sync)
        {
            _writePos = 0;
            _count = 0;
        }
    }

    /// <summary>Copies the most recent <paramref name="n"/> samples in chronological order. Caller must hold <see cref="_sync"/>.</summary>
    private short[] CopyMostRecent(int n)
    {
        if (n <= 0)
            return [];

        short[] result = new short[n];
        int start = (_writePos - n + _capacity) % _capacity;
        for (int i = 0; i < n; i++)
        {
            result[i] = _buffer[(start + i) % _capacity];
        }

        return result;
    }
}
