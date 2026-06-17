using System.Threading;
using System.Threading.Tasks;

namespace QuantumZ.Core.Interfaces
{
    /// <summary>
    /// Manages the lifecycle of the local llama.cpp server on the device.
    /// </summary>
    public interface ILlamaLocalManager
    {
        /// <summary>
        /// Ensures that the local llama server is running and available for requests.
        /// Returns true if the server is running or was successfully started.
        /// </summary>
        ValueTask<bool> EnsureServerRunningAsync();

        /// <summary>
        /// Stops the local llama server process.
        /// </summary>
        ValueTask StopServerAsync();

        /// <summary>
        /// Checks if the llama-server binary exists at the designated path.
        /// </summary>
        bool IsBinaryAvailable();

        /// <summary>
        /// Checks whether the local llama.cpp server health endpoint is reachable without starting or downloading anything.
        /// </summary>
        ValueTask<bool> CheckHealthAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets a value indicating whether the server is currently running.
        /// </summary>
        bool IsServerRunning { get; }
    }
}
