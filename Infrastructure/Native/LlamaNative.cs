using System.Runtime.InteropServices;
using System.Text;

namespace QuantumZ.Infrastructure.Native;

internal static partial class LlamaNative
{
    private const string LibraryName = "quantumz_llama";

    public static IntPtr Create(string modelPath, string paramsJson)
    {
        var handle = qz_llama_create(modelPath, paramsJson);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create QuantumZ llama runtime. {GetLastError()}");

        return handle;
    }

    public static string Infer(IntPtr handle, string prompt, int maxTokens, int outputCapacity = 8192)
    {
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Cannot run llama inference because the native handle is null.");

        if (outputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputCapacity), outputCapacity, "Output capacity must be positive.");

        var output = new byte[outputCapacity];
        var result = qz_llama_infer(handle, prompt, maxTokens, output, output.Length);
        if (result <= 0)
            throw new InvalidOperationException($"QuantumZ llama inference failed. {GetLastError()}");

        return DecodeUtf8(output);
    }

    public static void Destroy(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            qz_llama_destroy(handle);
    }

    public static string GetLastError()
    {
        var error = qz_llama_last_error();
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

    [DllImport(LibraryName, EntryPoint = "qz_llama_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr qz_llama_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string paramsJson);

    [DllImport(LibraryName, EntryPoint = "qz_llama_infer", CallingConvention = CallingConvention.Cdecl)]
    private static extern int qz_llama_infer(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string prompt,
        int maxTokens,
        [Out] byte[] output,
        int outputCapacity);

    [DllImport(LibraryName, EntryPoint = "qz_llama_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void qz_llama_destroy(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "qz_llama_last_error", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr qz_llama_last_error();
}
