using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using QuantumZ.Core.Interfaces;

namespace QuantumZ.Infrastructure.Services
{
    public class LlamaLocalManager(IDebugLogger debugLogger, ILocalBinaryManager binaryManager, ISettingsService settings) : ILlamaLocalManager
    {
        private const string BinaryId = "llama-server";
        private const string LocalUrl = "http://localhost:8025";
        private const int LocalPort = 8025;
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
                for (var i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    if (await CheckHealthAsync())
                    {
                        debugLogger.Log("LlamaLocalManager", $"Local llama server started successfully after {(i + 1) * 500}ms.");
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
                ExecuteShellCommand($"pkill -f {ShellQuote(BinaryId)}");
                IsServerRunning = false;
                debugLogger.Log("LlamaLocalManager", "Stop command sent successfully.");
            }
            catch (Exception ex)
            {
                debugLogger.Log("LlamaLocalManager", $"Error stopping server: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async ValueTask<bool> StartServerAsync()
        {
            try
            {
                var binaryPath = await binaryManager.EnsureBinaryAsync(BinaryId);
                var modelPath = ResolveModelPath();
                if (string.IsNullOrWhiteSpace(modelPath))
                {
                    debugLogger.Log("LlamaLocalManager", "No local GGUF model was found. Place a Q4 GGUF in AppData/models/llm or select an absolute model path.");
                    return false;
                }

                if (!Path.GetFileName(modelPath).Contains("q4", StringComparison.OrdinalIgnoreCase))
                    debugLogger.Log("LlamaLocalManager", $"Selected local model is not clearly Q4 quantized: {modelPath}");

                var command = string.Join(' ',
                    "nohup",
                    ShellQuote(binaryPath),
                    "-m", ShellQuote(modelPath),
                    "--host", "127.0.0.1",
                    "--port", LocalPort.ToString(),
                    "-c", "4096",
                    ">", "/dev/null", "2>&1", "&");

                ExecuteShellCommand(command);
                debugLogger.Log("LlamaLocalManager", $"llama-server launch requested for model '{Path.GetFileName(modelPath)}'.");
                return true;
            }
            catch (Exception ex)
            {
                debugLogger.Log("LlamaLocalManager", $"Error executing start command: {ex.Message}");
                return false;
            }
        }

        private string? ResolveModelPath()
        {
            var selected = FirstNonEmpty(settings.SelectedModelName, settings.LlamaModelId);
            if (!string.IsNullOrWhiteSpace(selected) && File.Exists(selected))
                return selected;

            var modelDirectory = Path.Combine(FileSystem.AppDataDirectory, "models", "llm");
            if (!Directory.Exists(modelDirectory))
                return null;

            var models = Directory.EnumerateFiles(modelDirectory, "*.gguf", SearchOption.AllDirectories).ToList();
            if (models.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(selected))
            {
                var match = models.FirstOrDefault(path =>
                    string.Equals(Path.GetFileName(path), selected, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileNameWithoutExtension(path), selected, StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                    return match;
            }

            return models
                .OrderByDescending(path => Path.GetFileName(path).Contains("q4", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .First();
        }

        private async ValueTask<bool> CheckHealthAsync()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                using var response = await client.GetAsync($"{LocalUrl}/health");
                if (response.IsSuccessStatusCode)
                    return true;

                using var modelsResponse = await client.GetAsync($"{LocalUrl}/v1/models");
                return modelsResponse.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

        private static string ShellQuote(string value) =>
            "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

        private void ExecuteShellCommand(string command)
        {
            try
            {
                var runtime = Java.Lang.Runtime.GetRuntime();
                runtime.Exec(new[] { "sh", "-c", command });
            }
            catch (Exception ex)
            {
                debugLogger.Log("LlamaLocalManager", $"Shell execution failed: {ex.Message}");
                throw;
            }
        }
    }
}
