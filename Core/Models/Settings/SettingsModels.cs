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
