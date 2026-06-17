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

    public event Action<ISettingsService>? SettingsChanged;

    public ServiceProviderSettings LlmSettings
    {
        get => LoadComplexFromJson<ServiceProviderSettings>(LlmSettingsFile) ?? MigrateLegacySetting(LlmUrlKeyLegacy, "LLM-Primary");
        set { SaveComplexToJson(LlmSettingsFile, value); NotifySettingsChanged(); }
    }

    public ServiceProviderSettings VadSettings
    {
        get => LoadComplexFromJson<ServiceProviderSettings>(VadSettingsFile) ?? MigrateLegacySetting(VadUrlKeyLegacy, "VAD-Primary");
        set { SaveComplexToJson(VadSettingsFile, value); NotifySettingsChanged(); }
    }

    public ServiceProviderSettings SttSettings
    {
        get => LoadComplexFromJson<ServiceProviderSettings>(SttSettingsFile) ?? MigrateLegacySetting(SttUrlKeyLegacy, "STT-Primary");
        set { SaveComplexToJson(SttSettingsFile, value); NotifySettingsChanged(); }
    }

    public ServiceProviderSettings TtsSettings
    {
        get => LoadComplexFromJson<ServiceProviderSettings>(TtsSettingsFile) ?? MigrateLegacySetting(TtsUrlKeyLegacy, "TTS-Primary");
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

    public string LlmUrl => LlmSettings.Providers.FirstOrDefault(p => p.Name == LlmSettings.ActiveProviderName)?.Url ?? "";
    public string VadUrl => VadSettings.Providers.FirstOrDefault(p => p.Name == VadSettings.ActiveProviderName)?.Url ?? "";
    public string SttUrl => SttSettings.Providers.FirstOrDefault(p => p.Name == SttSettings.ActiveProviderName)?.Url ?? "";
    public string TtsUrl => TtsSettings.Providers.FirstOrDefault(p => p.Name == TtsSettings.ActiveProviderName)?.Url ?? "";

    public string LlamaModelId
    {
        get => LlmSettings.Providers.FirstOrDefault(p => p.Name == LlmSettings.ActiveProviderName)?.ModelId ?? "";
        set { var p = LlmSettings.Providers.ToList(); var idx = p.FindIndex(x => x.Name == LlmSettings.ActiveProviderName); if (idx >= 0) p[idx] = p[idx] with { ModelId = value }; LlmSettings = LlmSettings with { Providers = p }; NotifySettingsChanged(); }
    }

    public string SelectedModelName
    {
        get => LlamaModelId;
        set { LlamaModelId = value; }
    }

    public string SttModelId
    {
        get => SttSettings.Providers.FirstOrDefault(p => p.Name == SttSettings.ActiveProviderName)?.ModelId ?? "";
        set { var p = SttSettings.Providers.ToList(); var idx = p.FindIndex(x => x.Name == SttSettings.ActiveProviderName); if (idx >= 0) p[idx] = p[idx] with { ModelId = value }; SttSettings = SttSettings with { Providers = p }; NotifySettingsChanged(); }
    }

    public string TtsModelId
    {
        get => TtsSettings.Providers.FirstOrDefault(p => p.Name == TtsSettings.ActiveProviderName)?.ModelId ?? "";
        set { var p = TtsSettings.Providers.ToList(); var idx = p.FindIndex(x => x.Name == TtsSettings.ActiveProviderName); if (idx >= 0) p[idx] = p[idx] with { ModelId = value }; TtsSettings = TtsSettings with { Providers = p }; NotifySettingsChanged(); }
    }

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
        File.WriteAllText(path, JsonSerializer.Serialize(data));
    }
}
