namespace QuantumZ.Infrastructure.Utilities;

/// <summary>
/// Wraps raw 16-bit mono PCM audio data in a standard WAV RIFF header.
/// </summary>
public static class PcmToWavConverter
{
    public static byte[] Convert(int sampleRate, short channels, byte[] pcmData)
    {
        const short bitsPerSample = 16;
        const short audioFormat = 1; // PCM
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var pcmLength = pcmData.Length;
        var wavLength = 36 + pcmLength;

        using var ms = new MemoryStream(44 + pcmLength);
        using var writer = new BinaryWriter(ms);

        // RIFF chunk descriptor
        writer.Write("RIFF"u8);
        writer.Write(wavLength);
        writer.Write("WAVE"u8);

        // fmt sub-chunk
        writer.Write("fmt "u8);
        writer.Write(16); // Subchunk1Size
        writer.Write(audioFormat);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        // data sub-chunk
        writer.Write("data"u8);
        writer.Write(pcmLength);
        writer.Write(pcmData);

        return ms.ToArray();
    }
}
