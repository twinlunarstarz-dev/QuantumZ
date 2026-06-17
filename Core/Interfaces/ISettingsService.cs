using QuantumZ.Core.Models.Settings;

namespace QuantumZ.Core.Interfaces;

public interface ISettingsService
{
    // AI Core Settings
    ServiceProviderSettings LlmSettings { get; set; }
    ServiceProviderSettings VadSettings { get; set; }
    ServiceProviderSettings SttSettings { get; set; }
    ServiceProviderSettings TtsSettings { get; set; }

    // Compatibility Properties for existing services (Proxy to active providers)
    string LlmUrl { get; }
    string VadUrl { get; }
    string SttUrl { get; }
    string TtsUrl { get; }
    string LlamaModelId { get; set; }
    string SelectedModelName { get; set; }
    string SttModelId { get; set; }
    string TtsModelId { get; set; }
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

    // On-Device STT
    bool UseOnDeviceStt { get; set; }
    string WhisperModelPath { get; set; }

    // UI Settings
    bool ShowAdvancedNetworkSettings { get; set; }

    GlobalAssistantSettings GlobalSettings { get; set; }

    // Obsidian Memory
    string ObsidianVaultPath { get; set; }

    // Reactive Update Event
    event Action<ISettingsService>? SettingsChanged;
}
