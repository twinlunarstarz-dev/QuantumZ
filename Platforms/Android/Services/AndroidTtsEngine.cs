using Android.App;
using Android.OS;
using Android.Speech.Tts;
using Tts = Android.Speech.Tts.TextToSpeech;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Android.Services;

/// <summary>
/// On-device TTS engine using Android's built-in TextToSpeech.
/// </summary>
public sealed class AndroidTtsEngine : Java.Lang.Object, Tts.IOnInitListener, ITtsEngine, ITtsProvider, IDisposable
{
    private Tts? _tts;
    private readonly TaskCompletionSource<bool> _initTcs = new();
    private TaskCompletionSource<bool>? _utteranceTcs;
    private bool _disposed;

    public AndroidTtsEngine()
    {
        var context = global::Android.App.Application.Context;
        _tts = new Tts(context, this);
    }

    public ProviderDescriptor Descriptor { get; } = new(
        Id: "builtin.android-tts",
        DisplayName: "Android Built-In TTS",
        Capability: ProviderCapability.Tts,
        Location: ProviderLocation.BuiltIn);

    public bool IsReady => !_disposed;

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (_disposed)
            return false;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            return await _initTcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (Exception ex) when (ex is System.OperationCanceledException or InvalidOperationException)
        {
            global::Android.Util.Log.Warn("QuantumZ", $"AndroidTtsEngine availability check failed: {ex.Message}");
            return false;
        }
    }

    public void OnInit(OperationResult status)
    {
        global::Android.Util.Log.Info("QuantumZ", $"AndroidTtsEngine OnInit: {status}");
        if (status == OperationResult.Success)
            _initTcs.TrySetResult(true);
        else
            _initTcs.TrySetException(new InvalidOperationException($"TTS initialization failed: {status}"));
    }

    public async ValueTask<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AndroidTtsEngine));

        global::Android.Util.Log.Info("QuantumZ", "AndroidTtsEngine SynthesizeAsync: waiting for init...");
        await _initTcs.Task.WaitAsync(ct);
        global::Android.Util.Log.Info("QuantumZ", "AndroidTtsEngine SynthesizeAsync: init complete.");

        if (_tts == null)
            throw new InvalidOperationException("TTS engine not initialized.");

        _utteranceTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new TtsProgressListener(_utteranceTcs);
        _tts.SetOnUtteranceProgressListener(listener);

        var bundle = new Bundle();
        global::Android.Util.Log.Info("QuantumZ", $"AndroidTtsEngine Speak: '{text[..Math.Min(text.Length, 80)]}...'");
        var result = _tts.Speak(text, QueueMode.Flush, bundle, "quantumz_tts");
        global::Android.Util.Log.Info("QuantumZ", $"AndroidTtsEngine Speak returned: {result}");

        if (result == OperationResult.Error)
        {
            global::Android.Util.Log.Error("QuantumZ", "AndroidTtsEngine Speak returned ERROR.");
            throw new InvalidOperationException("TTS Speak failed immediately.");
        }

        try
        {
            // Samsung + Google TTS sometimes never fires OnDone; use a generous timeout.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await _utteranceTcs.Task.WaitAsync(linkedCts.Token);
            global::Android.Util.Log.Info("QuantumZ", "AndroidTtsEngine SynthesizeAsync: utterance completed.");
        }
        catch (System.OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout expired but cancellation token not directly cancelled -> assume TTS played through.
            global::Android.Util.Log.Warn("QuantumZ", "AndroidTtsEngine SynthesizeAsync: utterance timeout (assumed completed).");
        }

        return [];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tts?.Stop();
        _tts?.Shutdown();
        _tts = null;
    }

    private class TtsProgressListener(TaskCompletionSource<bool> tcs) : UtteranceProgressListener
    {
        public override void OnDone(string? utteranceId)
        {
            global::Android.Util.Log.Info("QuantumZ", $"TtsProgressListener OnDone: {utteranceId}");
            tcs.TrySetResult(true);
        }

        [Obsolete]
        public override void OnError(string? utteranceId)
        {
            global::Android.Util.Log.Error("QuantumZ", $"TtsProgressListener OnError: {utteranceId}");
            tcs.TrySetException(new InvalidOperationException("TTS playback error."));
        }

        public override void OnStart(string? utteranceId)
        {
            global::Android.Util.Log.Info("QuantumZ", $"TtsProgressListener OnStart: {utteranceId}");
        }
    }
}
