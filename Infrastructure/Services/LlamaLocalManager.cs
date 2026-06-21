using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.Infrastructure.Native;

namespace QuantumZ.Infrastructure.Services
{
    public class LlamaLocalManager(IDebugLogger debugLogger, INativeRuntimeService nativeRuntimeService, ISettingsService settings) : ILlamaLocalManager, IDisposable
    {
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private IntPtr _handle;
        private string? _loadedModelPath;
        private bool _isRunning;

        public bool IsServerRunning
        {
            get => _isRunning;
            private set => _isRunning = value;
        }

        public bool IsBinaryAvailable() => nativeRuntimeService.IsRuntimeAvailableAsync(NativeRuntimeKind.Llm).AsTask().GetAwaiter().GetResult();

        public async ValueTask<bool> EnsureServerRunningAsync()
        {
            debugLogger.Log("LlamaLocalManager", "Ensuring local llama server is running...");

            await _lifecycleGate.WaitAsync();
            try
            {
                return await EnsureRuntimeLoadedCoreAsync(CancellationToken.None);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private async ValueTask<bool> EnsureRuntimeLoadedCoreAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!await nativeRuntimeService.IsRuntimeAvailableAsync(NativeRuntimeKind.Llm, ct))
            {
                debugLogger.Log("LlamaLocalManager", "Local llama runtime is unavailable because libquantumz_llama.so is not packaged for this Android ABI.", LogLevel.Warning);
                DestroyHandle();
                return false;
            }

            var modelPath = ResolveModelPath();
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                debugLogger.Log("LlamaLocalManager", "Local llama runtime is disabled because no local GGUF model was found in AppData/models/llm or selected as an absolute model path.", LogLevel.Warning);
                DestroyHandle();
                return false;
            }

            if (!Path.GetFileName(modelPath).Contains("q4", StringComparison.OrdinalIgnoreCase))
                debugLogger.Log("LlamaLocalManager", $"Selected local model is not clearly Q4 quantized: {modelPath}", LogLevel.Warning);

            if (_handle != IntPtr.Zero && string.Equals(_loadedModelPath, modelPath, StringComparison.Ordinal))
            {
                IsServerRunning = true;
                return true;
            }

            DestroyHandle();

            try
            {
                _handle = LlamaNative.Create(modelPath, BuildParamsJson());
                _loadedModelPath = modelPath;
                IsServerRunning = true;
                debugLogger.Log("LlamaLocalManager", $"Loaded local llama native runtime for model '{Path.GetFileName(modelPath)}'.", LogLevel.Info);
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
            {
                debugLogger.Log("LlamaLocalManager", $"Failed to load local llama native runtime: {ex.Message}", LogLevel.Error);
                DestroyHandle();
                return false;
            }
        }

        public async ValueTask StopServerAsync()
        {
            await _lifecycleGate.WaitAsync();
            try
            {
                DestroyHandle();
                debugLogger.Log("LlamaLocalManager", "Local llama native runtime handle released.", LogLevel.Info);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private string? ResolveModelPath()
        {
            var selected = settings.GetActiveProvider("LLM")?.ModelId ?? "";
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

        public async ValueTask<bool> CheckHealthAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var runtimeAvailable = await nativeRuntimeService.IsRuntimeAvailableAsync(NativeRuntimeKind.Llm, ct);
            var modelPath = ResolveModelPath();
            IsServerRunning = runtimeAvailable && !string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath);
            return IsServerRunning;
        }

        public async ValueTask<string> InferAsync(string prompt, int maxTokens, CancellationToken ct = default)
        {
            await _lifecycleGate.WaitAsync(ct);
            try
            {
                if (!await EnsureRuntimeLoadedCoreAsync(ct))
                    throw new InvalidOperationException("Local llama native runtime is unavailable. Ensure libquantumz_llama.so is packaged and a local GGUF model exists.");

                ct.ThrowIfCancellationRequested();
                return LlamaNative.Infer(_handle, prompt, maxTokens);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
            {
                debugLogger.Log("LlamaLocalManager", $"Local llama inference failed: {ex.Message}", LogLevel.Error);
                throw;
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public void Dispose()
        {
            DestroyHandle();
            _lifecycleGate.Dispose();
            GC.SuppressFinalize(this);
        }

        private string BuildParamsJson()
        {
            var parameters = settings.GetActiveProvider("LLM")?.Parameters ?? [];
            if (parameters.Count == 0)
                return "{}";

            var payload = new Dictionary<string, object?>();
            foreach (var pair in parameters)
                payload[pair.Key] = pair.Value;


            return JsonSerializer.Serialize(payload);
        }

        private void DestroyHandle()
        {
            if (_handle != IntPtr.Zero)
            {
                LlamaNative.Destroy(_handle);
                _handle = IntPtr.Zero;
            }

            _loadedModelPath = null;
            IsServerRunning = false;
        }
    }
}
