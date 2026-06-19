namespace QuantumZ.Core.Models.Settings;

public enum McpTransportType
{
    Http,
    Stdio
}

public record McpServerConfig(
    string Name,
    string Endpoint,
    string? ApiKey = null,
    List<string>? Capabilities = null,
    McpTransportType Transport = McpTransportType.Http,
    string? Command = null,
    List<string>? Args = null,
    Dictionary<string, string>? Env = null,
    bool Disabled = false,
    List<string>? AlwaysAllow = null
)
{
    public List<string> Capabilities { get; init; } = Capabilities ?? [];
    public List<string> Args { get; init; } = Args ?? [];
    public Dictionary<string, string> Env { get; init; } = Env ?? [];
    public List<string> AlwaysAllow { get; init; } = AlwaysAllow ?? [];
}

public enum AudioRoutingPreference
{
    Default,
    AlwaysSpeaker,
    AlwaysHeadset,
    Dynamic
}

public record ActivityLoggingSettings(
    TimeSpan Interval,
    List<string>? SummarizationTriggers = null
)
{
    public List<string> SummarizationTriggers { get; init; } = SummarizationTriggers ?? [];
}

public record ProviderConfig(
    string Name,
    string Url,
    string? ModelId = null,
    Dictionary<string, string>? Parameters = null
)
{
    public Dictionary<string, string> Parameters { get; init; } = Parameters ?? [];
}

public record ServiceProviderSettings(
    string ActiveProviderName,
    List<ProviderConfig> Providers
)
{
    public List<ProviderConfig> Providers { get; init; } = Providers ?? [];
}

public record GlobalAssistantSettings(
    double PreRollSeconds = 1.0,
    string? CustomSystemMessage = null,
    bool UseLocalLlm = false,
    bool UseOnDeviceStt = false,
    bool UseLocalTts = true,
    List<string>? WakeWords = null
)
{
    public List<string> WakeWords { get; init; } = WakeWords ?? ["QuantumZ", "Hey Quantum"];
}

// ── V2 Pipeline Settings ───────────────────────────────────────────────────

/// <summary>Determines whether a pipeline stage uses a remote API, local on-device model, or built-in Android API.</summary>
public enum ModelMode { Remote, Local, BuiltIn }

/// <summary>Configuration for a remote AI endpoint (OpenAI-compatible).</summary>
public sealed record RemoteEndpointConfig
{
    public required string Url { get; init; }
    public string? ApiKey { get; init; }
    public string? ModelId { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>Configuration for a locally-hosted or on-device model.</summary>
public sealed record LocalModelConfig
{
    public required string ModelPath { get; init; }
    public int ServerPort { get; init; } = 8025;
    public string? AdditionalParameters { get; init; }
}

/// <summary>Per-stage configuration controlling which provider handles a single pipeline capability.</summary>
public sealed record StageSettings
{
    public bool Enabled { get; init; } = true;
    public ModelMode Mode { get; init; } = ModelMode.Remote;
    public RemoteEndpointConfig? Remote { get; init; }
    public LocalModelConfig? Local { get; init; }
}

/// <summary>Complete pipeline configuration with one StageSettings per capability.</summary>
public sealed record PipelineSettings
{
    public StageSettings WakeWord { get; init; } = new();
    public StageSettings Vad { get; init; } = new();
    public StageSettings Stt { get; init; } = new();
    public StageSettings Llm { get; init; } = new();
    public StageSettings Tts { get; init; } = new();
}

/// <summary>Preferred audio output routing for TTS playback.</summary>
public enum AudioOutputMode { Auto, Speaker, Bluetooth, Headset }

/// <summary>Voice assistant behavioral settings that persist across restarts.</summary>
public sealed record VoiceAssistantSettings
{
    public string TriggerPhrase { get; init; } = "hey quantum";
    public string SystemPrompt { get; init; } = "You are QuantumZ, a helpful AI voice assistant running on Android. Be concise and conversational. Your responses will be read aloud.";
    public float PreRollSeconds { get; init; } = 5f;
    public float PostSilenceSeconds { get; init; } = 1.2f;
    public float WakeWordThreshold { get; init; } = 0.85f;
    public AudioOutputMode AudioOutput { get; init; } = AudioOutputMode.Auto;
    public int MaxToolCallIterations { get; init; } = 6;
}
