using System.Runtime.InteropServices;

namespace QuantumZ.Infrastructure.Native;

internal static partial class PiperNative
{
    private const string LibraryName = "quantumz_piper";

    public static IntPtr Create(string modelPath, string configPath, string paramsJson)
    {
        var handle = qz_piper_create(modelPath, configPath, paramsJson);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create QuantumZ Piper runtime. {GetLastError()}");

        return handle;
    }

    public static byte[] SynthesizeWav(IntPtr handle, string text, int outputCapacity = 2_097_152)
    {
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Cannot run Piper synthesis because the native handle is null.");

        if (outputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputCapacity), outputCapacity, "Output capacity must be positive.");

        var output = new byte[outputCapacity];
        var result = qz_piper_synthesize_wav(handle, text, output, output.Length, out var bytesWritten);
        if (result <= 0 || bytesWritten <= 0)
            throw new InvalidOperationException($"QuantumZ Piper synthesis failed. {GetLastError()}");

        if (bytesWritten > output.Length)
            throw new InvalidOperationException("QuantumZ Piper reported more bytes than the managed output buffer can hold.");

        return output[..bytesWritten];
    }

    public static void Destroy(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            qz_piper_destroy(handle);
    }

    public static string GetLastError()
    {
        var error = qz_piper_last_error();
        return error == IntPtr.Zero
            ? "No native error details were provided."
            : Marshal.PtrToStringUTF8(error) ?? "No native error details were provided.";
    }

    [DllImport(LibraryName, EntryPoint = "qz_piper_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr qz_piper_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string configPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string paramsJson);

    [DllImport(LibraryName, EntryPoint = "qz_piper_synthesize_wav", CallingConvention = CallingConvention.Cdecl)]
    private static extern int qz_piper_synthesize_wav(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        [Out] byte[] output,
        int outputCapacity,
        out int bytesWritten);

    [DllImport(LibraryName, EntryPoint = "qz_piper_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void qz_piper_destroy(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "qz_piper_last_error", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr qz_piper_last_error();
}
