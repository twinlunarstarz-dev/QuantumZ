using System.Runtime.InteropServices;
using System.Text;

namespace QuantumZ.Infrastructure.Native;

internal static partial class WhisperNative
{
    private const string LibraryName = "quantumz_whisper";

    public static IntPtr Create(string modelPath, string paramsJson)
    {
        var handle = qz_whisper_create(modelPath, paramsJson);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create QuantumZ Whisper runtime. {GetLastError()}");

        return handle;
    }

    public static string TranscribePcm16(IntPtr handle, ReadOnlySpan<byte> pcm16Audio, int sampleRate, int outputCapacity = 4096)
    {
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Cannot run Whisper transcription because the native handle is null.");

        if (pcm16Audio.Length == 0)
            return string.Empty;

        if (pcm16Audio.Length % sizeof(short) != 0)
            throw new ArgumentException("PCM16 audio length must contain complete 16-bit samples.", nameof(pcm16Audio));

        if (outputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputCapacity), outputCapacity, "Output capacity must be positive.");

        var pcm16Samples = MemoryMarshal.Cast<byte, short>(pcm16Audio).ToArray();
        var nativePcm = Marshal.AllocHGlobal(pcm16Samples.Length * sizeof(short));
        try
        {
            Marshal.Copy(pcm16Samples, 0, nativePcm, pcm16Samples.Length);
            var output = new byte[outputCapacity];
            var result = qz_whisper_transcribe_pcm16(handle, nativePcm, pcm16Samples.Length, sampleRate, output, output.Length);
            if (result <= 0)
                throw new InvalidOperationException($"QuantumZ Whisper transcription failed. {GetLastError()}");

            return DecodeUtf8(output);
        }
        finally
        {
            Marshal.FreeHGlobal(nativePcm);
        }
    }

    public static void Destroy(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            qz_whisper_destroy(handle);
    }

    public static string GetLastError()
    {
        var error = qz_whisper_last_error();
        return error == IntPtr.Zero
            ? "No native error details were provided."
            : Marshal.PtrToStringUTF8(error) ?? "No native error details were provided.";
    }

    private static string DecodeUtf8(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
            length = buffer.Length;

        return Encoding.UTF8.GetString(buffer, 0, length).Trim();
    }

    [DllImport(LibraryName, EntryPoint = "qz_whisper_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr qz_whisper_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string paramsJson);

    [DllImport(LibraryName, EntryPoint = "qz_whisper_transcribe_pcm16", CallingConvention = CallingConvention.Cdecl)]
    private static extern int qz_whisper_transcribe_pcm16(
        IntPtr handle,
        IntPtr pcm16,
        int sampleCount,
        int sampleRate,
        [Out] byte[] output,
        int outputCapacity);

    [DllImport(LibraryName, EntryPoint = "qz_whisper_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void qz_whisper_destroy(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "qz_whisper_last_error", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr qz_whisper_last_error();
}
