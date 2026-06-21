using System.Text.Json;
using Microsoft.Maui.Storage;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models.Settings;

namespace QuantumZ.Infrastructure.Services;

public sealed class SettingsService(IDebugLogger logger) : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private const string LlamaModelIdKey = "llama_model_id";
    private const string SelectedModelNameKey = "selected_model_name";
    private const string SttModelIdKey = "stt_model_id";
    private const string TtsModelIdKey = "tts_model_id";

    private const string LlmSettingsFile = "llm_settings.json";
    private const string VadSettingsFile = "vad_settings.json";
    private const string SttSettingsFile = "stt_settings.json";
    private const string TtsSettingsFile = "tts_settings.json";

    // Legacy keys for migration
    private const string LlmUrlKeyLegacy = "llm_server_url";
    private const string SttUrlKeyLegacy = "stt_server_url";
    private const string VadUrlKeyLegacy = "vad_server_url";
    private const string TtsUrlKeyLegacy = "tts_server_url";
    private const string LegacyLlamaServerUrlKey = "llama_server_url";
    private const string AudioRoutingKey = "audio_routing";
    private const string LoggingIntervalKey = "logging_interval";
    private const string EnableActivityLoggingKey = "enable_activity_logging";
    private const string EnableActivityAnalysisKey = "enable_activity_analysis";
    private const string UseOnDeviceSttKey = "use_on_device_stt";
    private const string WhisperModelPathKey = "whisper_model_path";
    private const string ObsidianVaultPathKey = "obsidian_vault_path";
    private const string ShowAdvancedNetworkSettingsKey = "show_advanced_network_settings";

    private const string ServerUrlDefault = "http://localhost:8025/v1";
    private const string LlamaModelIdDefault = "llama3";
    private const string SelectedModelNameDefault = "llama3";
    private const string SttModelIdDefault = "whisper-base";
    private const string TtsModelIdDefault = "builtin.android-tts";

    private const string McpServersFile = "mcp_servers.json";
    private const string GlobalSettingsFile = "global_settings.json";
    private const string WakeWordsFile = "wake_words.json";
    private const string SummarizationTriggersFile = "summarization_triggers.json";
    private const string VoiceAssistantSettingsFile = "voice_assistant_settings.json";
    private const string SetupSettingsFile = "setup_settings.json";
    private const string SecureSecretPrefix = "quantumz.secure.";

    private static readonly string[] SecretParameterNames = ["api_key", "ApiKey", "apiKey", "API_KEY"];

    public event Action<ISettingsService>? SettingsChanged;

    public ServiceProviderSettings LlmSettings
    {
        get => LoadProviderSettings("LLM", LlmSettingsFile, LlmUrlKeyLegacy);
        set { SaveProviderSettings(LlmSettingsFile, value); NotifySettingsChanged(); }
    }

    public ServiceProviderSettings VadSettings
    {
        get => LoadProviderSettings("VAD", VadSettingsFile, VadUrlKeyLegacy);
        set { SaveProviderSettings(VadSettingsFile, value); NotifySettingsChanged(); }
    }

    public ServiceProviderSettings SttSettings
    {
        get => LoadProviderSettings("STT", SttSettingsFile, SttUrlKeyLegacy);
        set { SaveProviderSettings(SttSettingsFile, value); NotifySettingsChanged(); }
    }

    public ServiceProviderSettings TtsSettings
    {
        get => LoadProviderSettings("TTS", TtsSettingsFile, TtsUrlKeyLegacy);
        set { SaveProviderSettings(TtsSettingsFile, value); NotifySettingsChanged(); }
    }

    private ServiceProviderSettings MigrateLegacySetting(string legacyKey, string defaultName)
    {
        var url = Preferences.Default.Get(legacyKey, ServerUrlDefault);
        return new ServiceProviderSettings(
            ActiveProviderName: defaultName,
            Providers: [new ProviderConfig(defaultName, url)]
        );
    }


    public GlobalAssistantSettings GlobalSettings
    {
        get => LoadComplexFromJson<GlobalAssistantSettings>(GlobalSettingsFile) ?? new GlobalAssistantSettings();
        set { SaveComplexToJson(GlobalSettingsFile, value); NotifySettingsChanged(); }
    }


    public VoiceAssistantSettings VoiceAssistantSettings
    {
        get => LoadComplexFromJson<VoiceAssistantSettings>(VoiceAssistantSettingsFile) ?? new VoiceAssistantSettings();
        set { SaveComplexToJson(VoiceAssistantSettingsFile, value); NotifySettingsChanged(); }
    }

    public SetupSettings SetupSettings
    {
        get => LoadComplexFromJson<SetupSettings>(SetupSettingsFile) ?? new SetupSettings();
        set { SaveComplexToJson(SetupSettingsFile, value); NotifySettingsChanged(); }
    }

    public List<string> WakeWords
    {
        get => GlobalSettings.WakeWords;
        set { GlobalSettings = GlobalSettings with { WakeWords = value }; NotifySettingsChanged(); }
    }

    public List<McpServerConfig> McpServers
    {
        get => LoadMcpServers();
        set { SaveMcpServers(value); NotifySettingsChanged(); }
    }

    public AudioRoutingPreference AudioRouting
    {
        get => (AudioRoutingPreference)Preferences.Default.Get(AudioRoutingKey, (int)AudioRoutingPreference.Default);
        set { Preferences.Default.Set(AudioRoutingKey, (int)value); NotifySettingsChanged(); }
    }

    public TimeSpan LoggingInterval
    {
        get => TimeSpan.FromTicks(Preferences.Default.Get(LoggingIntervalKey, TimeSpan.FromMinutes(15).Ticks));
        set { Preferences.Default.Set(LoggingIntervalKey, value.Ticks); NotifySettingsChanged(); }
    }

    public List<string> SummarizationTriggers
    {
        get => LoadListFromJson(SummarizationTriggersFile) ?? [];
        set { SaveListToJson(SummarizationTriggersFile, value); NotifySettingsChanged(); }
    }

    public bool EnableActivityLogging
    {
        get => Preferences.Default.Get(EnableActivityLoggingKey, true);
        set { Preferences.Default.Set(EnableActivityLoggingKey, value); NotifySettingsChanged(); }
    }

    public bool EnableActivityAnalysis
    {
        get => Preferences.Default.Get(EnableActivityAnalysisKey, false);
        set { Preferences.Default.Set(EnableActivityAnalysisKey, value); NotifySettingsChanged(); }
    }

    public bool UseOnDeviceStt
    {
        get => GlobalSettings.UseOnDeviceStt;
        set { GlobalSettings = GlobalSettings with { UseOnDeviceStt = value }; NotifySettingsChanged(); }
    }

    public bool UseLocalLlm
    {
        get => GlobalSettings.UseLocalLlm;
        set { GlobalSettings = GlobalSettings with { UseLocalLlm = value }; NotifySettingsChanged(); }
    }

    public bool UseLocalTts
    {
        get => GlobalSettings.UseLocalTts;
        set { GlobalSettings = GlobalSettings with { UseLocalTts = value }; NotifySettingsChanged(); }
    }

    public string WhisperModelPath
    {
        get => Preferences.Default.Get(WhisperModelPathKey, string.Empty);
        set { Preferences.Default.Set(WhisperModelPathKey, value); NotifySettingsChanged(); }
    }

    public string ObsidianVaultPath
    {
        get => Preferences.Default.Get(ObsidianVaultPathKey, Path.Combine(FileSystem.AppDataDirectory, "quantumz_vault"));
        set { Preferences.Default.Set(ObsidianVaultPathKey, value); NotifySettingsChanged(); }
    }

    public bool ShowAdvancedNetworkSettings
    {
        get => Preferences.Default.Get(ShowAdvancedNetworkSettingsKey, false);
        set { Preferences.Default.Set(ShowAdvancedNetworkSettingsKey, value); NotifySettingsChanged(); }
    }

    private void NotifySettingsChanged() => SettingsChanged?.Invoke(this);

    public ProviderConfig? GetActiveProvider(string service) => service.ToUpper() switch
    {
        "LLM" => LlmSettings.Providers.FirstOrDefault(p => p.Name == LlmSettings.ActiveProviderName),
        "VAD" => VadSettings.Providers.FirstOrDefault(p => p.Name == VadSettings.ActiveProviderName),
        "STT" => SttSettings.Providers.FirstOrDefault(p => p.Name == SttSettings.ActiveProviderName),
        "TTS" => TtsSettings.Providers.FirstOrDefault(p => p.Name == TtsSettings.ActiveProviderName),
        _ => null
    };

    private List<string>? LoadListFromJson(string fileName)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path), JsonOptions); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            HandleCorruptSettingsFile(path, fileName, ex);
            return null;
        }
    }

    private void SaveListToJson(string fileName, List<string> data)
    {
        SaveComplexToJson(fileName, data);
    }

    private T? LoadComplexFromJson<T>(string fileName) where T : class
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            HandleCorruptSettingsFile(path, fileName, ex);
            return null;
        }
    }

    private void SaveComplexToJson<T>(string fileName, T data)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? FileSystem.AppDataDirectory);
            WriteAllTextDurably(tempPath, JsonSerializer.Serialize(data, JsonOptions));

            if (File.Exists(path))
            {
                TryAtomicReplace(tempPath, path, backupPath);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch (Exception ex)
        {
            logger.Log("Settings", $"Atomic save failed for {fileName}: {ex.Message}", Core.Models.LogLevel.Error);
            throw;
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private ServiceProviderSettings LoadProviderSettings(string serviceName, string fileName, string legacyKey)
    {
        var settings = NormalizeServiceProviderSettings(
            serviceName,
            LoadComplexFromJson<ServiceProviderSettings>(fileName) ?? MigrateLegacySetting(legacyKey, $"{serviceName}-Primary"));

        var hydrated = HydrateProviderSecrets(serviceName, settings, out var migratedCleartextSecret);
        if (migratedCleartextSecret)
            SaveProviderSettings(fileName, hydrated);

        return hydrated;
    }

    private void SaveProviderSettings(string fileName, ServiceProviderSettings settings)
    {
        SaveComplexToJson(fileName, PersistProviderSecretsAndRedact(fileName, settings));
    }

    private ServiceProviderSettings PersistProviderSecretsAndRedact(string fileName, ServiceProviderSettings settings)
    {
        var providers = settings.Providers.Select(provider =>
        {
            var parameters = new Dictionary<string, string>(provider.Parameters, StringComparer.OrdinalIgnoreCase);
            var secret = GetSecretParameter(parameters);
            var secureKey = BuildSecureProviderKey(fileName, provider.Name);

            if (!string.IsNullOrWhiteSpace(secret))
                SetSecureValue(secureKey, secret.Trim());
            else
                RemoveSecureValue(secureKey);

            RemoveSecretParameters(parameters);
            return provider with { Parameters = parameters };
        }).ToList();

        return settings with { Providers = providers };
    }

    private ServiceProviderSettings HydrateProviderSecrets(string serviceName, ServiceProviderSettings settings, out bool migratedCleartextSecret)
    {
        var migrated = false;

        var providers = settings.Providers.Select(provider =>
        {
            var parameters = new Dictionary<string, string>(provider.Parameters, StringComparer.OrdinalIgnoreCase);
            var cleartextSecret = GetSecretParameter(parameters);
            var secureKey = BuildSecureProviderKey(GetProviderSettingsFile(serviceName), provider.Name);
            var secureSecret = GetSecureValue(secureKey);
            var secret = FirstNonEmpty(cleartextSecret, secureSecret);

            if (!string.IsNullOrWhiteSpace(cleartextSecret))
            {
                SetSecureValue(secureKey, cleartextSecret.Trim());
                RemoveSecretParameters(parameters);
                migrated = true;
            }

            if (!string.IsNullOrWhiteSpace(secret))
            {
                parameters["api_key"] = secret.Trim();
                parameters["ApiKey"] = secret.Trim();
            }

            return provider with { Parameters = parameters };
        }).ToList();

        migratedCleartextSecret = migrated;
        return settings with { Providers = providers };
    }

    private List<McpServerConfig> LoadMcpServers()
    {
        var servers = LoadComplexFromJson<List<McpServerConfig>>(McpServersFile) ?? [];
        var migratedCleartextSecret = false;

        var hydrated = servers.Select(server =>
        {
            var secureKey = BuildSecureMcpServerKey(server);
            var secret = FirstNonEmpty(server.ApiKey, GetSecureValue(secureKey));
            if (!string.IsNullOrWhiteSpace(server.ApiKey))
            {
                SetSecureValue(secureKey, server.ApiKey.Trim());
                migratedCleartextSecret = true;
            }

            return string.IsNullOrWhiteSpace(secret)
                ? server with { ApiKey = null }
                : server with { ApiKey = secret.Trim() };
        }).ToList();

        if (migratedCleartextSecret)
            SaveMcpServers(hydrated);

        return hydrated;
    }

    private void SaveMcpServers(List<McpServerConfig> servers)
    {
        foreach (var server in servers)
        {
            var secureKey = BuildSecureMcpServerKey(server);
            if (string.IsNullOrWhiteSpace(server.ApiKey))
                RemoveSecureValue(secureKey);
            else
                SetSecureValue(secureKey, server.ApiKey.Trim());
        }

        SaveComplexToJson(McpServersFile, servers.Select(server => server with { ApiKey = null }).ToList());
    }

    private void HandleCorruptSettingsFile(string path, string fileName, Exception ex)
    {
        logger.Log("Settings", $"Failed to parse {fileName}; preserving corrupt file before falling back to defaults. {ex.Message}", Core.Models.LogLevel.Error);

        var corruptPath = Path.Combine(
            Path.GetDirectoryName(path) ?? FileSystem.AppDataDirectory,
            $"{Path.GetFileName(path)}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.corrupt");

        try
        {
            File.Copy(path, corruptPath, overwrite: false);
        }
        catch (Exception backupEx) when (backupEx is IOException or UnauthorizedAccessException)
        {
            logger.Log("Settings", $"Unable to back up corrupt {fileName}: {backupEx.Message}", Core.Models.LogLevel.Warning);
        }
    }

    private static void WriteAllTextDurably(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static void TryAtomicReplace(string tempPath, string targetPath, string backupPath)
    {
        TryDeleteFile(backupPath);

        try
        {
            File.Replace(tempPath, targetPath, backupPath);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            File.Move(tempPath, targetPath, overwrite: true);
        }
    }

    private static string? GetSecretParameter(Dictionary<string, string> parameters)
    {
        foreach (var key in SecretParameterNames)
        {
            if (parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static void RemoveSecretParameters(Dictionary<string, string> parameters)
    {
        foreach (var key in SecretParameterNames)
            parameters.Remove(key);
    }

    private static string GetProviderSettingsFile(string serviceName) => serviceName.ToUpperInvariant() switch
    {
        "LLM" => LlmSettingsFile,
        "VAD" => VadSettingsFile,
        "STT" => SttSettingsFile,
        "TTS" => TtsSettingsFile,
        _ => throw new ArgumentException("Invalid service name", nameof(serviceName))
    };

    private static string BuildSecureProviderKey(string fileName, string providerName) =>
        $"{SecureSecretPrefix}provider.{fileName}.{providerName}.api_key";

    private static string BuildSecureMcpServerKey(McpServerConfig server) =>
        $"{SecureSecretPrefix}mcp.{server.Name}.{server.Endpoint}.api_key";

    private string? GetSecureValue(string key)
    {
        try
        {
            return SecureStorage.Default.GetAsync(key).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.Log("Settings", $"Secure setting '{key}' could not be read and will be treated as missing. {ex.Message}", Core.Models.LogLevel.Warning);
            RemoveSecureValue(key);
            return null;
        }
    }

    private void SetSecureValue(string key, string value)
    {
        try
        {
            SecureStorage.Default.SetAsync(key, value).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.Log("Settings", $"Secure setting '{key}' could not be saved. {ex.Message}", Core.Models.LogLevel.Error);
            throw;
        }
    }

    private void RemoveSecureValue(string key)
    {
        try
        {
            SecureStorage.Default.Remove(key);
        }
        catch (Exception ex)
        {
            logger.Log("Settings", $"Secure setting '{key}' could not be removed. {ex.Message}", Core.Models.LogLevel.Warning);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ServiceProviderSettings NormalizeServiceProviderSettings(string serviceName, ServiceProviderSettings? settings)
    {
        if (settings == null) return MigrateLegacySetting(serviceName switch
        {
            "LLM" => LlmUrlKeyLegacy,
            "VAD" => VadUrlKeyLegacy,
            "STT" => SttUrlKeyLegacy,
            "TTS" => TtsUrlKeyLegacy,
            _ => throw new ArgumentException("Invalid service name")
        }, $"{serviceName}-Primary");

        var providers = settings.Providers ?? [];
        var activeProviderName = settings.ActiveProviderName;

        if (providers.Count == 0)
        {
            // Recovery seeding: seed default provider if list is empty
            var defaultName = $"{serviceName}-Primary";
            providers = [new ProviderConfig(defaultName, ServerUrlDefault)];
            activeProviderName = defaultName;
        }
        else if (string.IsNullOrWhiteSpace(activeProviderName) || !providers.Any(p => p.Name == activeProviderName))
        {
            // Ensure ActiveProviderName points to a valid provider in the list
            activeProviderName = providers[0].Name;
        }

        return settings with { Providers = providers, ActiveProviderName = activeProviderName };
    }
}
