using QuantumZ.Core.Models.Settings;

namespace QuantumZ.Core.Interfaces;

public interface ISettingsService
{
    // AI Core Settings
    string LlmUrl { get; set; }
    string VadUrl { get; set; }
    string SttUrl { get; set; }
    string TtsUrl { get; set; }
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

    // Obsidian Memory
    string ObsidianVaultPath { get; set; }

    // Reactive Update Event
    event Action<ISettingsService>? SettingsChanged;
}
