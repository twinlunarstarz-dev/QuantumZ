using Microsoft.Extensions.DependencyInjection;
using QuantumZ.Android.Audio;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
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
        services.AddSingleton<INativeRuntimeService, NativeRuntimeService>();
        services.AddSingleton<ModelCatalogService>();
        services.AddSingleton<ILocalSetupService, LocalSetupService>();
        services.AddHttpClient<IModelRegistry, ModelRegistry>();
        services.AddHttpClient<LlamaAIClient>();
        services.AddTransient<IAIClient>(sp => sp.GetRequiredService<LlamaAIClient>());
        services.AddTransient<ILlmProvider>(sp => sp.GetRequiredService<LlamaAIClient>());
        services.AddHttpClient<IMcpOrchestrator, McpOrchestrator>();

        // Audio engines - STT
        services.AddHttpClient<RemoteSttEngine>();
        services.AddTransient<ISttProvider>(sp => sp.GetRequiredService<RemoteSttEngine>());
        services.AddSingleton<ISttEngine>(sp => sp.GetRequiredService<RemoteSttEngine>());

        services.AddSingleton<WhisperLocalSttProvider>();
        services.AddTransient<ISttProvider>(sp => sp.GetRequiredService<WhisperLocalSttProvider>());

        // Audio engines - TTS
        services.AddHttpClient<TtsService>();
        services.AddTransient<ITtsProvider>(sp => sp.GetRequiredService<TtsService>());

        services.AddSingleton<LocalAiTtsProvider>();
        services.AddTransient<ITtsProvider>(sp => sp.GetRequiredService<LocalAiTtsProvider>());

        services.AddSingleton<AndroidTtsEngine>();
        services.AddSingleton<ITtsEngine>(sp => sp.GetRequiredService<AndroidTtsEngine>());
        services.AddTransient<ITtsProvider>(sp => sp.GetRequiredService<AndroidTtsEngine>());
        services.AddSingleton<AudioRoutingManager>(sp =>
            new AudioRoutingManager(global::Android.App.Application.Context, sp.GetRequiredService<ISettingsService>()));
        services.AddSingleton<IThermalMonitor>(sp =>
            new AndroidThermalMonitorService(global::Android.App.Application.Context, sp.GetService<IDebugLogger>()));
        services.AddSingleton<IWakeWordProvider>(sp =>
            WakeWordProviderFactory.Create(
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<IDebugLogger>()));
        services.AddSingleton<IVadProvider, RmsVadProvider>();
        services.AddSingleton<IProviderRouter, ProviderRouter>();

        // AI Integration Core
        services.AddSingleton<IAIIntegrationService, AIIntegrationService>();
        services.AddSingleton<ActivityAnalyzerService>();

        // Visualizer & Speech State
        services.AddSingleton<IAudioVisualizer, AudioVisualizerService>();
        services.AddSingleton<ISpeechStateService, SpeechStateService>();
        services.AddSingleton<IPipelineStateService, PipelineStateService>();
        services.AddSingleton<IAudioRingBuffer>(_ => new AudioRingBuffer(capacitySeconds: 10f, sampleRate: 16000));

        // Logging & Memory
        services.AddSingleton<IDebugLogger, DebugLoggerService>();
        services.AddSingleton<SqliteLogBufferService>();
        services.AddSingleton<IActivityLogger>(sp => sp.GetRequiredService<SqliteLogBufferService>());
        services.AddSingleton<ILogSummaryService, LogSummaryService>();
        services.AddSingleton<IMemoryService, ObsidianMemoryService>();

        // Whisper model downloader
        services.AddSingleton<WhisperModelDownloader>();

        // V2 Pipeline Controller — transient so each service start gets a clean instance
        services.AddTransient<MicrophonePipelineController>();

        return services;
    }
}
