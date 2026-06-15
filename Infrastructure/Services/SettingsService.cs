using System.Text.Json;
using Microsoft.Maui.Storage;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models.Settings;

namespace QuantumZ.Infrastructure.Services;

public sealed class SettingsService : ISettingsService
{
    private const string LlmUrlKey = "llm_server_url";
    private const string SttUrlKey = "stt_server_url";
    private const string VadUrlKey = "vad_server_url";
    private const string TtsUrlKey = "tts_server_url";
    private const string LegacyLlamaServerUrlKey = "llama_server_url";
    private const string LlamaModelIdKey = "llama_model_id";
    private const string SelectedModelNameKey = "selected_model_name";
    private const string SttModelIdKey = "stt_model_id";
    private const string TtsModelIdKey = "tts_model_id";
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
    private const string WakeWordsFile = "wake_words.json";
    private const string SummarizationTriggersFile = "summarization_triggers.json";

    public event Action<ISettingsService>? SettingsChanged;

    public string VadUrl
    {
        get => Preferences.Default.Get(VadUrlKey, ServerUrlDefault);
        set { Preferences.Default.Set(VadUrlKey, value); NotifySettingsChanged(); }
    }

    public string LlmUrl
    {
        get
        {
            if (!Preferences.Default.ContainsKey(LlmUrlKey))
            {
                var legacy = Preferences.Default.Get(LegacyLlamaServerUrlKey, ServerUrlDefault);
                Preferences.Default.Set(LlmUrlKey, legacy);
            }
            return Preferences.Default.Get(LlmUrlKey, ServerUrlDefault);
        }
        set { Preferences.Default.Set(LlmUrlKey, value); NotifySettingsChanged(); }
    }

    public string SttUrl
    {
        get
        {
            if (!Preferences.Default.ContainsKey(SttUrlKey))
            {
                var legacy = Preferences.Default.Get(LegacyLlamaServerUrlKey, ServerUrlDefault);
                Preferences.Default.Set(SttUrlKey, legacy);
            }
            return Preferences.Default.Get(SttUrlKey, ServerUrlDefault);
        }
        set { Preferences.Default.Set(SttUrlKey, value); NotifySettingsChanged(); }
    }

    public string TtsUrl
    {
        get
        {
            if (!Preferences.Default.ContainsKey(TtsUrlKey))
            {
                var legacy = Preferences.Default.Get(LegacyLlamaServerUrlKey, ServerUrlDefault);
                Preferences.Default.Set(TtsUrlKey, legacy);
            }
            return Preferences.Default.Get(TtsUrlKey, ServerUrlDefault);
        }
        set { Preferences.Default.Set(TtsUrlKey, value); NotifySettingsChanged(); }
    }

    public string LlamaModelId
    {
        get => Preferences.Default.Get(LlamaModelIdKey, LlamaModelIdDefault);
        set { Preferences.Default.Set(LlamaModelIdKey, value); NotifySettingsChanged(); }
    }

    public string SelectedModelName
    {
        get => Preferences.Default.Get(SelectedModelNameKey, SelectedModelNameDefault);
        set { Preferences.Default.Set(SelectedModelNameKey, value); NotifySettingsChanged(); }
    }

    public string SttModelId
    {
        get => Preferences.Default.Get(SttModelIdKey, SttModelIdDefault);
        set { Preferences.Default.Set(SttModelIdKey, value); NotifySettingsChanged(); }
    }

    public string TtsModelId
    {
        get => Preferences.Default.Get(TtsModelIdKey, TtsModelIdDefault);
        set { Preferences.Default.Set(TtsModelIdKey, value); NotifySettingsChanged(); }
    }

    public List<string> WakeWords
    {
        get => LoadListFromJson(WakeWordsFile) ?? ["hey quantum"];
        set { SaveListToJson(WakeWordsFile, value); NotifySettingsChanged(); }
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
        get => Preferences.Default.Get(UseOnDeviceSttKey, true);
        set { Preferences.Default.Set(UseOnDeviceSttKey, value); NotifySettingsChanged(); }
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
