using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models.Settings;

namespace QuantumZ.Infrastructure.Services;

/// <summary>Creates the appropriate IWakeWordProvider based on current pipeline settings.</summary>
internal static class WakeWordProviderFactory
{
    internal static IWakeWordProvider Create(
        ISettingsService settingsService,
        IDebugLogger logger)
    {
        var stage = settingsService.PipelineSettings.WakeWord;
        return stage.Mode switch
        {
            ModelMode.Local or ModelMode.Remote => new OnnxWakeWordProvider(settingsService, logger),
            _ => new RmsWakeWordProvider(settingsService, logger)
        };
    }
}
