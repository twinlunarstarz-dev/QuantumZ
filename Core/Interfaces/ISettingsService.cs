using QuantumZ.Core.Models.Settings;

namespace QuantumZ.Core.Interfaces;

public interface ISettingsService
{
    // AI Core Settings
    ServiceProviderSettings LlmSettings { get; set; }
    ServiceProviderSettings VadSettings { get; set; }
    ServiceProviderSettings SttSettings { get; set; }
    ServiceProviderSettings TtsSettings { get; set; }

    // Provider Accessor
    ProviderConfig? GetActiveProvider(string service);

    // Compatibility Properties for existing services (Proxy to active providers)
    List<string> WakeWords { get; set; }

    // MCP Settings
    List<McpServerConfig> McpServers { get; set; }

    // Audio Engine Settings
    AudioRoutingPreference AudioRouting { get; set; }

    // Activity Logger Settings
    TimeSpan LoggingInterval { get; set; }
    List<string> SummarizationTriggers { get; set; }
    bool EnableActivityLogging { get; set; }
    bool EnableActivityAnalysis { get; set; }

    // On-Device Providers
    bool UseOnDeviceStt { get; set; }
    bool UseLocalLlm { get; set; }
    bool UseLocalTts { get; set; }
    string WhisperModelPath { get; set; }

    // UI Settings
    bool ShowAdvancedNetworkSettings { get; set; }

    GlobalAssistantSettings GlobalSettings { get; set; }

    // Obsidian Memory
    string ObsidianVaultPath { get; set; }

    // V2 Pipeline Settings

    /// <summary>Gets or sets the voice assistant behavioral settings.</summary>
    VoiceAssistantSettings VoiceAssistantSettings { get; set; }

    /// <summary>Gets or sets the persistent first-run setup state.</summary>
    SetupSettings SetupSettings { get; set; }

    // Reactive Update Event
    event Action<ISettingsService>? SettingsChanged;
}
