using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

/// <summary>
/// Adaptive RMS-based VAD provider used as the local fallback foundation until a Silero provider is available.
/// </summary>
public sealed class RmsVadProvider : IVadProvider
{
    private const double MinStartRms = 0.009;
    private const double MinContinueRms = 0.006;
    private const double StartSignalToNoiseRatio = 3.2;
    private const double ContinueSignalToNoiseRatio = 1.9;
    private const double NoiseFloorAlpha = 0.035;
    private const int StartWindowCount = 2;
    private const int EndWindowCount = 8;

    private double _noiseFloor = 0.0035;
    private int _speechWindows;
    private int _silenceWindows;
    private bool _isSpeechActive;

    public ProviderDescriptor Descriptor { get; } = new(
        Id: "local.rms-vad",
        DisplayName: "Local RMS VAD",
        Capability: ProviderCapability.Vad,
        Location: ProviderLocation.Local);

    public bool IsReady => true;

    public event EventHandler<VadActivityEventArgs>? ActivityChanged;

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    public ValueTask<VadResult> DetectSpeechAsync(ReadOnlyMemory<byte> pcm16Audio, int sampleRate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var rms = ComputeRms(pcm16Audio.Span);
        var startThreshold = Math.Max(MinStartRms, _noiseFloor * StartSignalToNoiseRatio);
        var continueThreshold = Math.Max(MinContinueRms, _noiseFloor * ContinueSignalToNoiseRatio);
        var activeThreshold = _isSpeechActive ? continueThreshold : startThreshold;
        var confidence = Math.Clamp(rms / startThreshold, 0.0, 1.0);
        var speechCandidate = rms >= activeThreshold;

        if (!_isSpeechActive && !speechCandidate)
        {
            _noiseFloor = (_noiseFloor * (1.0 - NoiseFloorAlpha)) + (rms * NoiseFloorAlpha);
        }

        VadActivityState state;
        if (speechCandidate)
        {
            _speechWindows++;
            _silenceWindows = 0;

            if (!_isSpeechActive && _speechWindows >= StartWindowCount)
            {
                _isSpeechActive = true;
                state = VadActivityState.SpeechStarted;
            }
            else
            {
                state = _isSpeechActive ? VadActivityState.SpeechContinued : VadActivityState.Silence;
            }
        }
        else
        {
            _speechWindows = 0;
            _silenceWindows++;

            if (_isSpeechActive && _silenceWindows >= EndWindowCount)
            {
                _isSpeechActive = false;
                state = VadActivityState.SpeechEnded;
            }
            else
            {
                state = _isSpeechActive ? VadActivityState.SpeechContinued : VadActivityState.Silence;
            }
        }

        var result = new VadResult(_isSpeechActive, confidence, rms, state);
        if (state is VadActivityState.SpeechStarted or VadActivityState.SpeechEnded)
        {
            ActivityChanged?.Invoke(this, new VadActivityEventArgs(state, result, DateTimeOffset.UtcNow));
        }

        return ValueTask.FromResult(result);
    }

    private static double ComputeRms(ReadOnlySpan<byte> pcm16Audio)
    {
        if (pcm16Audio.Length < 2)
            return 0;

        double sumSquares = 0;
        var samples = pcm16Audio.Length / 2;

        for (var i = 0; i < pcm16Audio.Length - 1; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm16Audio[i..(i + 2)]) / 32768.0;
            sumSquares += sample * sample;
        }

        return Math.Sqrt(sumSquares / samples);
    }
}
