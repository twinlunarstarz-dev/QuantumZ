using Microsoft.Extensions.DependencyInjection;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Selects the best available provider for each pipeline capability and fails over on errors.
/// </summary>
public sealed class ProviderRouter(
    IServiceProvider serviceProvider,
    IDebugLogger debugLogger,
    IModelRegistry modelRegistry,
    ISettingsService settings,
    IThermalMonitor thermalMonitor) : IProviderRouter
{
    private static readonly ProviderLocation[] PreferredLocations =
    [
        ProviderLocation.Remote,
        ProviderLocation.Hybrid,
        ProviderLocation.Local,
        ProviderLocation.BuiltIn
    ];

    private static readonly ProviderLocation[] TtsPreferredLocations =
    [
        ProviderLocation.BuiltIn,
        ProviderLocation.Local,
        ProviderLocation.Hybrid,
        ProviderLocation.Remote
    ];

    public ValueTask<IVadProvider> ResolveVadProviderAsync(CancellationToken ct = default) =>
        ResolveProviderAsync(serviceProvider.GetServices<IVadProvider>(), ProviderCapability.Vad, ct);

    public ValueTask<ISttProvider> ResolveSttProviderAsync(CancellationToken ct = default) =>
        ResolveProviderAsync(serviceProvider.GetServices<ISttProvider>(), ProviderCapability.Stt, ct);

    public ValueTask<ILlmProvider> ResolveLlmProviderAsync(CancellationToken ct = default) =>
        ResolveProviderAsync(serviceProvider.GetServices<ILlmProvider>(), ProviderCapability.Llm, ct);

    public ValueTask<ITtsProvider> ResolveTtsProviderAsync(CancellationToken ct = default) =>
        ResolveProviderAsync(serviceProvider.GetServices<ITtsProvider>(), ProviderCapability.Tts, ct);

    public ValueTask<VadResult> DetectSpeechAsync(ReadOnlyMemory<byte> pcm16Audio, int sampleRate, CancellationToken ct = default) =>
        ExecuteWithFallbackAsync(
            serviceProvider.GetServices<IVadProvider>(),
            ProviderCapability.Vad,
            provider => provider.DetectSpeechAsync(pcm16Audio, sampleRate, ct),
            ct);

    public ValueTask<string> TranscribeAsync(byte[] pcm16Audio, CancellationToken ct = default) =>
        TranscribeAsync(pcm16Audio, textProgress: null, ct);

    public ValueTask<string> TranscribeAsync(byte[] pcm16Audio, IProgress<string>? textProgress, CancellationToken ct = default) =>
        ExecuteWithFallbackAsync(
            serviceProvider.GetServices<ISttProvider>(),
            ProviderCapability.Stt,
            async provider =>
            {
                await provider.InitializeAsync(ct);
                return await provider.TranscribeAsync(pcm16Audio, textProgress, ct);
            },
            ct);

    public ValueTask<AiResponse> SendPromptAsync(AiRequest request, CancellationToken ct = default) =>
        ExecuteWithFallbackAsync(
            serviceProvider.GetServices<ILlmProvider>(),
            ProviderCapability.Llm,
            provider => provider.SendPromptAsync(request, ct),
            ct);

    public async IAsyncEnumerable<string> StreamPromptAsync(AiRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var provider = await ResolveLlmProviderAsync(ct);
        debugLogger.Log("ProviderRouter", $"Streaming LLM request through {provider.Descriptor.DisplayName}.", LogLevel.Trace);

        await foreach (var chunk in provider.StreamPromptAsync(request, ct))
        {
            yield return chunk;
        }
    }

    public ValueTask<byte[]> SynthesizeAsync(string text, CancellationToken ct = default) =>
        ExecuteWithFallbackAsync(
            serviceProvider.GetServices<ITtsProvider>(),
            ProviderCapability.Tts,
            provider => provider.SynthesizeAsync(text, ct),
            ct);

    private async ValueTask<TProvider> ResolveProviderAsync<TProvider>(IEnumerable<TProvider> providers, ProviderCapability capability, CancellationToken ct)
        where TProvider : IProvider
    {
        var filtered = FilterByThermalState(providers);
        foreach (var provider in await SortProvidersAsync(filtered, capability, ct))
        {
            ct.ThrowIfCancellationRequested();
            debugLogger.Log("ProviderRouter", $"Checking {capability} provider {provider.Descriptor.DisplayName} ({provider.Descriptor.Location}).", LogLevel.Trace);

            if (!provider.IsReady)
            {
                debugLogger.Log("ProviderRouter", $"Skipping {provider.Descriptor.DisplayName}; provider is not ready.", LogLevel.Warning);
                continue;
            }

            try
            {
                if (await provider.IsAvailableAsync(ct))
                {
                    debugLogger.Log("ProviderRouter", $"Selected {capability} provider {provider.Descriptor.DisplayName}.");
                    return provider;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                debugLogger.Log("ProviderRouter", $"Availability check failed for {provider.Descriptor.DisplayName}: {ex.Message}", LogLevel.Warning);
            }
        }

        var activeConfig = settings.GetActiveProvider(capability.ToString());
        if (activeConfig == null)
        {
            debugLogger.Log("ProviderRouter", $"Critical: No active configuration for {capability}. Check Settings page.", LogLevel.Error);
            throw new InvalidOperationException($"No available {capability} provider was found because no active configuration is set.");
        }

        throw new InvalidOperationException($"No available {capability} provider was found, although '{activeConfig.Name}' is configured as active.");
    }

    private async ValueTask<TResult> ExecuteWithFallbackAsync<TProvider, TResult>(
        IEnumerable<TProvider> providers,
        ProviderCapability capability,
        Func<TProvider, ValueTask<TResult>> operation,
        CancellationToken ct)
        where TProvider : IProvider
    {
        Exception? lastError = null;

        var filtered = FilterByThermalState(providers);
        foreach (var provider in await SortProvidersAsync(filtered, capability, ct))
        {
            ct.ThrowIfCancellationRequested();

            if (!provider.IsReady)
                continue;

            try
            {
                if (!await provider.IsAvailableAsync(ct))
                    continue;

                debugLogger.Log("ProviderRouter", $"Routing {capability} through {provider.Descriptor.DisplayName}.", LogLevel.Trace);
                return await operation(provider);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                debugLogger.Log("ProviderRouter", $"{capability} provider {provider.Descriptor.DisplayName} failed; trying fallback. {ex.Message}", LogLevel.Warning);
            }
        }

        var activeConfig = settings.GetActiveProvider(capability.ToString());
        if (activeConfig == null)
        {
            debugLogger.Log("ProviderRouter", $"Critical: No active configuration for {capability}. Check Settings page.", LogLevel.Error);
            throw new InvalidOperationException($"All {capability} providers failed or were unavailable, and no active provider is configured.", lastError);
        }

        throw new InvalidOperationException($"All {capability} providers failed or were unavailable. Active provider '{activeConfig.Name}' also failed.", lastError);
    }

    private static List<TProvider> SortProviders<TProvider>(IEnumerable<TProvider> providers)
        where TProvider : IProvider =>
        [.. providers.OrderBy(provider => GetLocationRank(provider.Descriptor.Capability, provider.Descriptor.Location))
            .ThenBy(provider => provider.Descriptor.Priority)
            .ThenBy(provider => provider.Descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)];

    private async ValueTask<List<TProvider>> SortProvidersAsync<TProvider>(IEnumerable<TProvider> providers, ProviderCapability capability, CancellationToken ct)
        where TProvider : IProvider
    {
        var providerList = providers.ToList();
        if (capability == ProviderCapability.Stt)
        {
            return [.. providerList
                .OrderBy(provider => IsSelectedSttLocation(provider) ? -1 : GetLocationRank(provider.Descriptor.Capability, provider.Descriptor.Location))
                .ThenBy(provider => provider.Descriptor.Priority)
                .ThenBy(provider => provider.Descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)];
        }

        if (capability == ProviderCapability.Llm)
        {
            return [.. providerList
                .OrderBy(provider => IsSelectedLlmLocation(provider) ? -1 : GetLocationRank(provider.Descriptor.Capability, provider.Descriptor.Location))
                .ThenBy(provider => provider.Descriptor.Priority)
                .ThenBy(provider => provider.Descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)];
        }

        if (capability != ProviderCapability.Tts)
            return SortProviders(providerList);

        var selectedModel = await modelRegistry.ResolvePreferredModelAsync(ProviderCapability.Tts, settings.GetActiveProvider("TTS")?.ModelId ?? "", ct);
        return [.. providerList
            .OrderBy(provider => IsSelectedTtsLocation(provider, selectedModel) ? -1 : GetLocationRank(provider.Descriptor.Capability, provider.Descriptor.Location))
            .ThenBy(provider => provider.Descriptor.Priority)
            .ThenBy(provider => provider.Descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    private bool IsSelectedTtsLocation<TProvider>(TProvider provider, ModelProfile? selectedModel)
        where TProvider : IProvider
    {
        if (provider.Descriptor.Capability != ProviderCapability.Tts)
            return false;

        // If UseLocalTts is enabled, prefer BuiltIn or Local providers
        if (settings.UseLocalTts)
            return provider.Descriptor.Location is ProviderLocation.BuiltIn or ProviderLocation.Local;

        // Otherwise, use the model registry selection
        return selectedModel is not null && provider.Descriptor.Location == selectedModel.Location;
    }

    private bool IsSelectedSttLocation<TProvider>(TProvider provider)
        where TProvider : IProvider =>
        provider.Descriptor.Capability == ProviderCapability.Stt
        && ((settings.UseOnDeviceStt && provider.Descriptor.Location == ProviderLocation.Local)
            || (!settings.UseOnDeviceStt && provider.Descriptor.Location == ProviderLocation.Remote));

    private bool IsSelectedLlmLocation<TProvider>(TProvider provider)
        where TProvider : IProvider =>
        provider.Descriptor.Capability == ProviderCapability.Llm
        && ((settings.UseLocalLlm && provider.Descriptor.Location is ProviderLocation.Local or ProviderLocation.Hybrid)
            || (!settings.UseLocalLlm && provider.Descriptor.Location is ProviderLocation.Remote or ProviderLocation.Hybrid));

    private static int GetLocationRank(ProviderCapability capability, ProviderLocation location)
    {
        var locations = capability == ProviderCapability.Tts ? TtsPreferredLocations : PreferredLocations;
        var index = Array.IndexOf(locations, location);
        return index < 0 ? int.MaxValue : index;
    }

    private bool IsTierAllowed(ThermalModelTier? tier)
    {
        if (tier == null) return true; // Non-tiered providers are always allowed

        var level = thermalMonitor.CurrentState.Level;
        return level switch
        {
            ThermalLevel.Normal => true,
            ThermalLevel.Warning => tier != ThermalModelTier.HighCapacity,
            ThermalLevel.Critical => tier == ThermalModelTier.LocalSmallMini || tier == ThermalModelTier.LocalMedium,
            _ => true
        };
    }

    private List<TProvider> FilterByThermalState<TProvider>(IEnumerable<TProvider> providers)
        where TProvider : IProvider
    {
        return providers.Where(p => IsTierAllowed(p.Descriptor.Tier)).ToList();
    }
}
