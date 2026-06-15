using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using QuantumZ.Core.Interfaces;

namespace QuantumZ.Infrastructure.Services
{
    public class LlamaLocalManager(IDebugLogger debugLogger, ILocalBinaryManager binaryManager) : ILlamaLocalManager
    {
        private const string BinaryId = "llama-server";
        private const string LocalUrl = "http://localhost:8025";
        private bool _isRunning;

        public bool IsServerRunning 
        { 
            get => _isRunning; 
            private set => _isRunning = value; 
        }

        public bool IsBinaryAvailable() => binaryManager.IsBinaryInstalled(BinaryId);

        public async ValueTask<bool> EnsureServerRunningAsync()
        {
            debugLogger.Log("LlamaLocalManager", "Ensuring local llama server is running...");

            try
            {
                await binaryManager.EnsureBinaryAsync(BinaryId);
            }
            catch (Exception ex)
            {
                debugLogger.Log("LlamaLocalManager", $"Failed to ensure llama-server binary: {ex.Message}");
                return false;
            }

            if (await CheckHealthAsync())
            {
                debugLogger.Log("LlamaLocalManager", "Local llama server is already running and healthy.");
                IsServerRunning = true;
                return true;
            }

            debugLogger.Log("LlamaLocalManager", "Starting local llama server process...");
            if (await StartServerAsync())
            {
                // Poll health check for up to 5 seconds with short intervals for faster startup detection
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(500);
                    if (await CheckHealthAsync())
                    {
                        debugLogger.Log("LlamaLocalManager", $"Local llama server started successfully after {i * 500}ms.");
                        IsServerRunning = true;
                        return true;
                    }
                }
            }

            debugLogger.Log("LlamaLocalManager", "Failed to start local llama server or health check failed.");
            IsServerRunning = false;
            return false;
        }

        public async ValueTask StopServerAsync()
        {
            debugLogger.Log("LlamaLocalManager", "Stopping local llama server...");
            try
            {
                // Use pkill to stop the process by name
                ExecuteShellCommand($"pkill -f {BinaryId}");
                IsServerRunning = false;
                debugLogger.Log("LlamaLocalManager", "Stop command sent successfully.");
            }
            catch (Exception ex)
            {
                debugLogger.Log("LlamaLocalManager", $"Error stopping server: {ex.Message}");
            }
        }

        private async ValueTask<bool> StartServerAsync()
        {
            try
            {
                // Note: In a real scenario, we would need to pass the model path and other arguments.
                // For this implementation, we follow the user's request for shell commands via Runtime exec.
                // We use 'nohup' or '&' to ensure it runs in background.
                var path = await binaryManager.EnsureBinaryAsync(BinaryId);
                string command = $"nohup {path} --port 8025 > /dev/null 2>&1 &";
                ExecuteShellCommand(command);
                return true;
            }
            catch (Exception ex)
            {
                debugLogger.Log("LlamaLocalManager", $"Error executing start command: {ex.Message}");
                return false;
            }
        }

        private async ValueTask<bool> CheckHealthAsync()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(2);
                var response = await client.GetAsync($"{LocalUrl}/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private void ExecuteShellCommand(string command)
        {
            try
            {
                // Use Java Runtime to execute shell commands on Android
                var runtime = Java.Lang.Runtime.GetRuntime();
                runtime.Exec(new string[] { "sh", "-c", command });
            }
            catch (Exception ex)
            {
                debugLogger.Log("LlamaLocalManager", $"Shell execution failed: {ex.Message}");
                throw;
            }
        }
    }
}
