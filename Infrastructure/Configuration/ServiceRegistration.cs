using Microsoft.Extensions.DependencyInjection;
using QuantumZ.Core.Interfaces;
using QuantumZ.Infrastructure.Services;
using QuantumZ.Android.Services;

namespace QuantumZ.Infrastructure.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddHttpClient<IFluxAssetService, FluxAssetService>();
        services.AddSingleton<ILocalBinaryManager, LocalBinaryManager>();
        services.AddSingleton<ILlamaLocalManager, LlamaLocalManager>();
        services.AddHttpClient<IModelRegistry, ModelRegistry>();
        services.AddHttpClient<LlamaAIClient>();
        services.AddTransient<IAIClient>(sp => sp.GetRequiredService<LlamaAIClient>());
        services.AddTransient<ILlmProvider>(sp => sp.GetRequiredService<LlamaAIClient>());
        services.AddHttpClient<IMcpOrchestrator, McpOrchestrator>();

        // Audio engines
        services.AddHttpClient<RemoteSttEngine>();
        services.AddSingleton<ISttEngine>(sp => sp.GetRequiredService<RemoteSttEngine>());
        services.AddTransient<ISttProvider, WhisperLocalSttProvider>();
        services.AddTransient<ISttProvider>(sp => (ISttProvider)sp.GetRequiredService<ISttEngine>());
        services.AddHttpClient<TtsService>();
        services.AddTransient<ITtsProvider, LocalAiTtsProvider>();
        services.AddTransient<ITtsProvider>(sp => sp.GetRequiredService<TtsService>());
        services.AddSingleton<AndroidTtsEngine>();
        services.AddSingleton<ITtsEngine>(sp => sp.GetRequiredService<AndroidTtsEngine>());
        services.AddSingleton<ITtsProvider>(sp => sp.GetRequiredService<AndroidTtsEngine>());
        services.AddSingleton<IThermalMonitor>(sp =>
            new AndroidThermalMonitorService(global::Android.App.Application.Context, sp.GetService<IDebugLogger>()));
        services.AddSingleton<IVadProvider, RmsVadProvider>();
        services.AddSingleton<IProviderRouter, ProviderRouter>();

        // AI Integration Core
        services.AddSingleton<IAIIntegrationService, AIIntegrationService>();
        services.AddSingleton<ActivityAnalyzerService>();

        // Visualizer & Speech State
        services.AddSingleton<IAudioVisualizer, AudioVisualizerService>();
        services.AddSingleton<ISpeechStateService, SpeechStateService>();

        // Logging & Memory
        services.AddSingleton<IDebugLogger, DebugLoggerService>();
        services.AddSingleton<SqliteLogBufferService>();
        services.AddSingleton<IActivityLogger>(sp => sp.GetRequiredService<SqliteLogBufferService>());
        services.AddSingleton<ILogSummaryService, LogSummaryService>();
        services.AddSingleton<IMemoryService, ObsidianMemoryService>();

        // Whisper model downloader
        services.AddSingleton<WhisperModelDownloader>();

        return services;
    }
}
