using System;
using System.Threading;
using System.Threading.Tasks;

namespace QuantumZ.Core.Interfaces;

public interface IFluxAssetService
{
    /// <summary>
    /// Generates an image using the Flux model and saves it to local storage.
    /// </summary>
    /// <param name="prompt">The descriptive prompt for the asset.</param>
    /// <param name="fileName">The target filename (e.g., "bg_main.png").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The local path to the saved image.</returns>
    ValueTask<string> GenerateAssetAsync(string prompt, string fileName, CancellationToken ct = default);

    /// <summary>
    /// Returns a predefined set of prompts for consistent aesthetic assets.
    /// </summary>
    string GetAestheticPrompt(AssetType type);
}

public enum AssetType
{
    MainBackground,
    ButtonNeonRed,
    AssistantRingPulsing,
    SettingsPanelGlass
}