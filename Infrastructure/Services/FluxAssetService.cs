using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Storage;
using QuantumZ.Core.Interfaces;

namespace QuantumZ.Infrastructure.Services;

public class FluxAssetService(HttpClient httpClient, ISettingsService settings) : IFluxAssetService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async ValueTask<string> GenerateAssetAsync(string prompt, string fileName, CancellationToken ct = default)
    {
        var endpoint = $"{settings.LlmUrl}/images/generations";
        
        var requestBody = new FluxImageRequest(
            prompt: prompt,
            model: "flux-2-klein-9b",
            n: 1,
            size: "1024x1024"
        );

        var response = await httpClient.PostAsJsonAsync(endpoint, requestBody, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<FluxImageResponse>(_jsonOptions, ct) 
                    ?? throw new InvalidOperationException("Failed to deserialize Flux image response.");

        var imageUrl = result.Data[0].Url;
        if (string.IsNullOrEmpty(imageUrl))
            throw new InvalidOperationException("No image URL returned from Flux server.");

        // Download the actual bytes and save locally
        var imageBytes = await httpClient.GetByteArrayAsync(imageUrl, ct);
        var localPath = Path.Combine(FileSystem.AppDataDirectory, fileName);
        await File.WriteAllBytesAsync(localPath, imageBytes, ct);

        return localPath;
    }

    public string GetAestheticPrompt(AssetType type) => type switch
    {
        AssetType.MainBackground => 
            "Cyberpunk futuristic dark city background, deep blacks, neon red accents, cinematic lighting, high contrast, digital art, flux style",
        AssetType.ButtonNeonRed => 
            "Futuristic cyberpunk UI button element, glowing neon red border, translucent glass center, isolated on black background, hyper-detailed tech interface",
        AssetType.AssistantRingPulsing => 
            "Symmetric futuristic AI core ring, pulsing neon red energy, dark metallic textures, holographic effects, centered composition, deep blacks, flux style",
        AssetType.SettingsPanelGlass => 
            "Futuristic cyberpunk frosted glass panel texture, subtle red glow edges, translucent deep gray, high tech surface, minimal design, 8k resolution",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private record FluxImageRequest(
        string prompt,
        string model,
        int n,
        string size
    );

    private record FluxImageResponse(
        List<FluxImageData> Data
    );

    private record FluxImageData(
        string Url
    );
}
