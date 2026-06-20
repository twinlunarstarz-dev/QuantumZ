using QuantumZ.Core.Interfaces;

namespace QuantumZ.Infrastructure.Services;

/// <summary>Creates the release trigger-gate provider.</summary>
internal static class WakeWordProviderFactory
{
    internal static IWakeWordProvider Create(
        ISettingsService settingsService,
        IDebugLogger logger)
    {
        // The user-configured wake phrase lives in VoiceAssistantSettings.TriggerPhrase.
        // Until a real custom acoustic phrase detector is implemented, the release-safe
        // provider gates on sustained voice activity and labels a detection with that
        // configured trigger phrase. Do not select OnnxWakeWordProvider from the hidden
        // legacy PipelineSettings.WakeWord stage.
        return new RmsWakeWordProvider(settingsService, logger);
    }
}
