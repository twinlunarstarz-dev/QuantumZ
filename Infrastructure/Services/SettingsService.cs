using System.Text.Json;
using Microsoft.Maui.Storage;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models.Settings;

namespace QuantumZ.Infrastructure.Services;

public sealed class SettingsService : ISettingsService
{
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
    private const string TtsModelIdDefault = "tts-default";

    private const string McpServersFile = "mcp_servers.json";
    private const string GlobalSettingsFile = "global_settings.json";
    private const string WakeWordsFile = "wake_words.json";
    private const string SummarizationTriggersFile = "summarization_triggers.json";
    private const string PipelineSettingsFile = "pipeline_settings.json";
    private const string VoiceAssistantSettingsFile = "voice_assistant_settings.json";

    public event Action<ISettingsService>? SettingsChanged;

    public ServiceProviderSettings LlmSettings
    {
        get => NormalizeServiceProviderSettings("LLM", LoadComplexFromJson<ServiceProviderSettings>(LlmSettingsFile) ?? MigrateLegacySetting(LlmUrlKeyLegacy, "LLM-Primary"));
        set { SaveComplexToJson(LlmSettingsFile, value); NotifySettingsChanged(); }
    }

    public ServiceProviderSettings VadSettings
    {
        get => NormalizeServiceProviderSettings("VAD", LoadComplexFromJson<ServiceProviderSettings>(VadSettingsFile) ?? MigrateLegacySetting(VadUrlKeyLegacy, "VAD-Primary"));
        set { SaveComplexToJson(VadSettingsFile, value); NotifySettingsChanged(); }
    }

    public ServiceProviderSettings SttSettings
    {
        get => NormalizeServiceProviderSettings("STT", LoadComplexFromJson<ServiceProviderSettings>(SttSettingsFile) ?? MigrateLegacySetting(SttUrlKeyLegacy, "STT-Primary"));
        set { SaveComplexToJson(SttSettingsFile, value); NotifySettingsChanged(); }
    }

    public ServiceProviderSettings TtsSettings
    {
        get => NormalizeServiceProviderSettings("TTS", LoadComplexFromJson<ServiceProviderSettings>(TtsSettingsFile) ?? MigrateLegacySetting(TtsUrlKeyLegacy, "TTS-Primary"));
        set { SaveComplexToJson(TtsSettingsFile, value); NotifySettingsChanged(); }
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

    public PipelineSettings PipelineSettings
    {
        get => LoadComplexFromJson<PipelineSettings>(PipelineSettingsFile) ?? new PipelineSettings();
        set { SaveComplexToJson(PipelineSettingsFile, value); NotifySettingsChanged(); }
    }

    public VoiceAssistantSettings VoiceAssistantSettings
    {
        get => LoadComplexFromJson<VoiceAssistantSettings>(VoiceAssistantSettingsFile) ?? new VoiceAssistantSettings();
        set { SaveComplexToJson(VoiceAssistantSettingsFile, value); NotifySettingsChanged(); }
    }

    public List<string> WakeWords
    {
        get => GlobalSettings.WakeWords;
        set { GlobalSettings = GlobalSettings with { WakeWords = value }; NotifySettingsChanged(); }
    }

    public List<McpServerConfig> McpServers
    {
        get => LoadComplexFromJson<List<McpServerConfig>>(McpServersFile) ?? [];
        set { SaveComplexToJson(McpServersFile, value); NotifySettingsChanged(); }
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
        try { return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)); }
        catch { return null; }
    }

    private void SaveListToJson(string fileName, List<string> data)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(data));
    }

    private T? LoadComplexFromJson<T>(string fileName) where T : class
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)); }
        catch { return null; }
    }

    private void SaveComplexToJson<T>(string fileName, T data)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        var tempPath = path + ".tmp";

        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(data));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Atomic save failed for {fileName}: {ex}");
            throw;
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
