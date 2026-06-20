using System.Threading;
using System.Threading.Tasks;

namespace QuantumZ.Core.Interfaces
{
    /// <summary>
    /// Manages the lifecycle of the local llama.cpp runtime on the device.
    /// </summary>
    public interface ILlamaLocalManager
    {
        /// <summary>
        /// Ensures that the local llama native runtime is loaded and available for requests.
        /// Returns true if the runtime is loaded or was successfully loaded.
        /// </summary>
        ValueTask<bool> EnsureServerRunningAsync();

        /// <summary>
        /// Releases the local llama native runtime handle.
        /// </summary>
        ValueTask StopServerAsync();

        /// <summary>
        /// Checks if the packaged llama native runtime is available.
        /// </summary>
        bool IsBinaryAvailable();

        /// <summary>
        /// Checks whether the packaged llama native runtime and local model are available without starting or downloading anything.
        /// </summary>
        ValueTask<bool> CheckHealthAsync(CancellationToken ct = default);

        /// <summary>
        /// Runs a single native llama inference request against the loaded local model.
        /// </summary>
        /// <param name="prompt">The fully formatted prompt to send to the model.</param>
        /// <param name="maxTokens">The maximum number of tokens to generate.</param>
        /// <param name="ct">A token that cancels runtime preparation before native inference begins.</param>
        /// <returns>The generated model text.</returns>
        ValueTask<string> InferAsync(string prompt, int maxTokens, CancellationToken ct = default);

        /// <summary>
        /// Gets a value indicating whether the server is currently running.
        /// </summary>
        bool IsServerRunning { get; }
    }
}
