using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Storage;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Discovers available models from local app storage and OpenAI-compatible provider endpoints.
/// </summary>
public sealed class ModelRegistry(
    HttpClient httpClient,
    ISettingsService settings,
    IDebugLogger debugLogger,
    ILlamaLocalManager llamaLocalManager) : IModelRegistry
{
    public const string LocalLlamaBaseUrl = "http://localhost:8025/v1";

    private static readonly string[] LlmExtensions = [".gguf", ".bin", ".safetensors"];
    private static readonly string[] TtsExtensions = [".onnx", ".pt", ".bin", ".gguf", ".json"];

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private IReadOnlyList<ModelProfile>? _cachedProfiles;
    private string? _cacheSettingsSignature;

    public async ValueTask<IReadOnlyList<ModelProfile>> DiscoverModelsAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var settingsSignature = GetSettingsSignature();
        if (!forceRefresh && _cachedProfiles is not null && string.Equals(_cacheSettingsSignature, settingsSignature, StringComparison.Ordinal))
            return _cachedProfiles;

        var profiles = new List<ModelProfile>();
        profiles.AddRange(DiscoverLocalModels());

        foreach (var endpoint in GetCandidateOpenAiEndpoints())
        {
            profiles.AddRange(await DiscoverOpenAiModelsAsync(endpoint.BaseUrl, endpoint.Location, endpoint.Provider, ct));
        }

        profiles.Add(CreateAndroidTtsProfile());

        _cachedProfiles = [.. profiles
            .GroupBy(profile => profile.RegistryKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(profile => profile.Capability)
            .ThenBy(profile => GetLocationRank(profile.Capability, profile.Location))
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)];

        debugLogger.Log("ModelRegistry", $"Discovered {_cachedProfiles.Count} model profile(s).", LogLevel.Info);
        _cacheSettingsSignature = settingsSignature;
        return _cachedProfiles;
    }

    public async ValueTask<IReadOnlyList<ModelProfile>> GetModelsAsync(ProviderCapability capability, CancellationToken ct = default)
    {
        var profiles = await DiscoverModelsAsync(ct: ct);
        return [.. profiles.Where(profile => profile.Capability == capability)];
    }

    public async ValueTask<ModelProfile?> ResolvePreferredModelAsync(ProviderCapability capability, string? preferredModelId = null, CancellationToken ct = default)
    {
        var models = await GetModelsAsync(capability, ct);
        var preferred = FirstNonEmpty(preferredModelId, GetConfiguredModelId(capability));

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var selected = models.FirstOrDefault(model => ModelMatchesSelection(model, preferred));
            if (selected is not null)
                return selected;
        }

        return models
            .Where(model => model.IsAvailable)
            .OrderBy(model => GetLocationRank(capability, model.Location))
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private IEnumerable<ModelProfile> DiscoverLocalModels()
    {
        var appData = FileSystem.AppDataDirectory;

        var configuredWhisperModelPath = settings.WhisperModelPath?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredWhisperModelPath) && File.Exists(configuredWhisperModelPath))
        {
            yield return CreateLocalFileProfile(configuredWhisperModelPath, ProviderCapability.Stt, "whisper.cpp", ProviderLocation.Local);
        }

        foreach (var profile in DiscoverFiles(Path.Combine(appData, "models", "llm"), ProviderCapability.Llm, "llama.cpp", ProviderLocation.Local, LlmExtensions))
            yield return profile;

        foreach (var profile in DiscoverFiles(Path.Combine(appData, "models", "stt", "whisper"), ProviderCapability.Stt, "whisper.cpp", ProviderLocation.Local, [".bin", ".gguf"]))
            yield return profile;

        foreach (var profile in DiscoverFiles(Path.Combine(appData, "models", "tts", "kokoro"), ProviderCapability.Tts, "kokoro", ProviderLocation.Local, TtsExtensions))
            yield return profile;

        foreach (var profile in DiscoverFiles(Path.Combine(appData, "models", "tts", "piper"), ProviderCapability.Tts, "piper", ProviderLocation.Local, TtsExtensions))
            yield return profile;

        foreach (var profile in DiscoverFiles(Path.Combine(appData, "models", "vad", "silero"), ProviderCapability.Vad, "silero", ProviderLocation.Local, [".onnx", ".pt"]))
            yield return profile;

        foreach (var profile in DiscoverFiles(Path.Combine(appData, "models", "embeddings"), ProviderCapability.Embedding, "local", ProviderLocation.Local, LlmExtensions))
            yield return profile;

        foreach (var profile in DiscoverFiles(Path.Combine(appData, "models", "rerankers"), ProviderCapability.Reranker, "local", ProviderLocation.Local, LlmExtensions))
            yield return profile;
    }

    private static IEnumerable<ModelProfile> DiscoverFiles(string directory, ProviderCapability capability, string provider, ProviderLocation location, string[] extensions)
    {
        if (!Directory.Exists(directory))
            yield break;

        foreach (var path in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (!extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                continue;

            yield return CreateLocalFileProfile(path, capability, provider, location);
        }
    }

    private static ModelProfile CreateLocalFileProfile(string path, ProviderCapability capability, string provider, ProviderLocation location)
    {
        var id = Path.GetFileNameWithoutExtension(path);
        return new ModelProfile(
            Id: id,
            DisplayName: id,
            Capability: capability,
            Provider: provider,
            Location: location,
            IsAvailable: true,
            LocalPath: path,
            Quantization: InferQuantization(id),
            SupportsToolCalling: capability == ProviderCapability.Llm,
            SupportsVision: capability == ProviderCapability.Vision || id.Contains("vision", StringComparison.OrdinalIgnoreCase));
    }

    private async ValueTask<IReadOnlyList<ModelProfile>> DiscoverOpenAiModelsAsync(string baseUrl, ProviderLocation location, string provider, CancellationToken ct)
    {
        var normalizedBaseUrl = NormalizeOpenAiBaseUrl(baseUrl);
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
            return [];

        if (location == ProviderLocation.Local && !await llamaLocalManager.CheckHealthAsync(ct))
            return [];

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

            var response = await httpClient.GetAsync(BuildEndpoint(normalizedBaseUrl, "models"), timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                debugLogger.Log("ModelRegistry", $"Model discovery failed for {normalizedBaseUrl}: {(int)response.StatusCode} {response.ReasonPhrase}", LogLevel.Warning);
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAiModelsResponse>(_jsonOptions, timeoutCts.Token)
                ?? new OpenAiModelsResponse([]);

            return [.. (result.Data ?? [])
                .Where(model => !string.IsNullOrWhiteSpace(model.Id))
                .Select(model => ToRemoteProfile(model.Id, normalizedBaseUrl, provider, location))];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException or InvalidOperationException)
        {
            debugLogger.Log("ModelRegistry", $"Model discovery failed for {normalizedBaseUrl}: {ex.Message}", LogLevel.Warning);
            return [];
        }
    }

    private static ModelProfile ToRemoteProfile(string id, string endpoint, string provider, ProviderLocation location)
    {
        var capability = InferCapability(id);
        return new ModelProfile(
            Id: id,
            DisplayName: id,
            Capability: capability,
            Provider: provider,
            Location: location,
            IsAvailable: true,
            Endpoint: endpoint,
            Quantization: InferQuantization(id),
            SupportsToolCalling: capability == ProviderCapability.Llm,
            SupportsVision: capability == ProviderCapability.Vision || id.Contains("vision", StringComparison.OrdinalIgnoreCase),
            ContextLength: InferContextLength(id));
    }

    private static ModelProfile CreateAndroidTtsProfile() => new(
        Id: "builtin.android-tts",
        DisplayName: "Android Built-In TTS",
        Capability: ProviderCapability.Tts,
        Provider: "android",
        Location: ProviderLocation.BuiltIn,
        IsAvailable: true);

    private IEnumerable<(string BaseUrl, ProviderLocation Location, string Provider)> GetCandidateOpenAiEndpoints()
    {
        var endpoints = new List<(string BaseUrl, ProviderLocation Location, string Provider)>
        {
            (settings.LlmUrl, ProviderLocation.Remote, "openai-compatible"),
            (LocalLlamaBaseUrl, ProviderLocation.Local, "llama.cpp")
        };

        if (!string.IsNullOrWhiteSpace(settings.TtsUrl))
            endpoints.Add((settings.TtsUrl, ProviderLocation.Remote, "openai-compatible-tts"));

        return endpoints
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.BaseUrl))
            .Select(endpoint => (NormalizeOpenAiBaseUrl(endpoint.BaseUrl), endpoint.Location, endpoint.Provider))
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.Item1))
            .GroupBy(endpoint => endpoint.Item1, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private string? GetConfiguredModelId(ProviderCapability capability) => capability switch
    {
        ProviderCapability.Llm => FirstNonEmpty(settings.SelectedModelName, settings.LlamaModelId),
        ProviderCapability.Stt => settings.SttModelId,
        ProviderCapability.Tts => settings.TtsModelId,
        _ => null
    };

    private static bool ModelMatchesSelection(ModelProfile model, string selection) =>
        string.Equals(model.Id, selection, StringComparison.OrdinalIgnoreCase)
        || string.Equals(model.DisplayName, selection, StringComparison.OrdinalIgnoreCase)
        || string.Equals(model.Provider, selection, StringComparison.OrdinalIgnoreCase)
        || string.Equals($"{model.Provider}:{model.Id}", selection, StringComparison.OrdinalIgnoreCase);

    private static int GetLocationRank(ProviderCapability capability, ProviderLocation location) => capability switch
    {
        ProviderCapability.Tts => location switch
        {
            ProviderLocation.Local => 0,
            ProviderLocation.BuiltIn => 1,
            ProviderLocation.Hybrid => 2,
            ProviderLocation.Remote => 3,
            _ => 4
        },
        ProviderCapability.Llm => location switch
        {
            ProviderLocation.Remote => 0,
            ProviderLocation.Hybrid => 1,
            ProviderLocation.Local => 2,
            ProviderLocation.BuiltIn => 3,
            _ => 4
        },
        _ => location switch
        {
            ProviderLocation.Local => 0,
            ProviderLocation.BuiltIn => 1,
            ProviderLocation.Hybrid => 2,
            ProviderLocation.Remote => 3,
            _ => 4
        }
    };

    private static ProviderCapability InferCapability(string modelId)
    {
        if (ContainsAny(modelId, "tts", "speech", "kokoro", "piper", "voice"))
            return ProviderCapability.Tts;

        if (ContainsAny(modelId, "whisper", "transcrib", "stt"))
            return ProviderCapability.Stt;

        if (ContainsAny(modelId, "embed", "bge", "nomic"))
            return ProviderCapability.Embedding;

        if (ContainsAny(modelId, "rerank"))
            return ProviderCapability.Reranker;

        if (ContainsAny(modelId, "vision", "vlm", "llava"))
            return ProviderCapability.Vision;

        return ProviderCapability.Llm;
    }

    private static string? InferQuantization(string modelId)
    {
        var markers = new[] { "Q2_K", "Q3_K", "Q4_K_M", "Q4_K_S", "Q5_K_M", "Q5_K_S", "Q6_K", "Q8_0", "F16", "BF16" };
        return markers.FirstOrDefault(marker => modelId.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static int? InferContextLength(string modelId)
    {
        if (modelId.Contains("128k", StringComparison.OrdinalIgnoreCase)) return 131072;
        if (modelId.Contains("64k", StringComparison.OrdinalIgnoreCase)) return 65536;
        if (modelId.Contains("32k", StringComparison.OrdinalIgnoreCase)) return 32768;
        if (modelId.Contains("16k", StringComparison.OrdinalIgnoreCase)) return 16384;
        if (modelId.Contains("8k", StringComparison.OrdinalIgnoreCase)) return 8192;
        return null;
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private string GetSettingsSignature() =>
        string.Join('|',
            settings.LlmUrl?.Trim() ?? string.Empty,
            settings.SttUrl?.Trim() ?? string.Empty,
            settings.TtsUrl?.Trim() ?? string.Empty,
            settings.SelectedModelName?.Trim() ?? string.Empty,
            settings.LlamaModelId?.Trim() ?? string.Empty,
            settings.SttModelId?.Trim() ?? string.Empty,
            settings.TtsModelId?.Trim() ?? string.Empty,
            settings.WhisperModelPath?.Trim() ?? string.Empty,
            settings.UseOnDeviceStt.ToString());

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string NormalizeOpenAiBaseUrl(string? url)
    {
        var normalized = url?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        return normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized}/v1";
    }

    private static string BuildEndpoint(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private sealed record OpenAiModelsResponse(List<OpenAiModel>? Data);
    private sealed record OpenAiModel(string Id);
}
