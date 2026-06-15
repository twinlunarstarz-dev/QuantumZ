using QuantumZ.Core.Interfaces;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Singleton service that bridges audio levels from the foreground service to the UI.
/// </summary>
public sealed class AudioVisualizerService : IAudioVisualizer
{
    public event EventHandler<double>? AudioLevelChanged;
    public event EventHandler<ListeningState>? StateChanged;
    public event EventHandler<bool>? ActivityDetectedChanged;

    public void ReportAudioLevel(double level) => AudioLevelChanged?.Invoke(this, level);
    public void ReportState(ListeningState state) => StateChanged?.Invoke(this, state);
    public void ReportActivityDetected(bool detected) => ActivityDetectedChanged?.Invoke(this, detected);
}
