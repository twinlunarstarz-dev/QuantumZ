using Android.Content;
using Android.OS;
using Android.Speech;

namespace QuantumZ.Android.Services;

/// <summary>
/// On-device STT using Android's built-in SpeechRecognizer with offline preference.
/// Provides continuous listening with automatic restart by recreating the recognizer
/// after each result or error (required for Samsung device compatibility).
/// </summary>
public sealed class OnDeviceSpeechRecognizer : Java.Lang.Object, IRecognitionListener
{
    private readonly Context _context;
    private readonly Action<string> _onResult;
    private SpeechRecognizer? _speechRecognizer;
    private bool _isListening;
    private bool _disposed;

    public OnDeviceSpeechRecognizer(Context context, Action<string> onResult)
    {
        _context = context;
        _onResult = onResult;

        if (!SpeechRecognizer.IsRecognitionAvailable(context))
            throw new InvalidOperationException("SpeechRecognizer is not available on this device.");

        CreateRecognizer();
    }

    private void CreateRecognizer()
    {
        if (_disposed) return;

        try
        {
            _speechRecognizer?.Destroy();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("QuantumZ", $"Recognizer destroy during recreate failed: {ex.Message}");
        }

        _speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(_context);
        _speechRecognizer.SetRecognitionListener(this);
        global::Android.Util.Log.Info("QuantumZ", "SpeechRecognizer instance created.");
    }

    public void StartListening()
    {
        if (_disposed) return;
        if (_isListening)
        {
            global::Android.Util.Log.Warn("QuantumZ", "StartListening called while already listening; ignoring.");
            return;
        }

        if (_speechRecognizer is null)
            CreateRecognizer();

        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
        intent.PutExtra(RecognizerIntent.ExtraPreferOffline, true);
        intent.PutExtra(RecognizerIntent.ExtraLanguage, "en-US");

        try
        {
            _speechRecognizer!.StartListening(intent);
            _isListening = true;
            global::Android.Util.Log.Info("QuantumZ", "OnDeviceSpeechRecognizer started listening.");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("QuantumZ", $"StartListening failed: {ex.Message}");
            _isListening = false;
            ScheduleRestart(500);
        }
    }

    public void StopListening()
    {
        if (_disposed || !_isListening || _speechRecognizer is null) return;

        try
        {
            _speechRecognizer.StopListening();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("QuantumZ", $"StopListening failed: {ex.Message}");
        }
        _isListening = false;
    }

    public void Destroy()
    {
        if (_disposed) return;
        _disposed = true;
        _isListening = false;

        try
        {
            _speechRecognizer?.Destroy();
            global::Android.Util.Log.Info("QuantumZ", "OnDeviceSpeechRecognizer destroyed.");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("QuantumZ", $"Destroy failed: {ex.Message}");
        }
        _speechRecognizer = null;
    }

    private void ScheduleRestart(int delayMs)
    {
        if (_disposed) return;
        Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            if (!_disposed)
            {
                global::Android.Util.Log.Info("QuantumZ", $"Restarting recognizer after {delayMs}ms delay.");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_disposed) return;
                    CreateRecognizer();
                    StartListening();
                });
            }
        });
    }

    public void OnReadyForSpeech(Bundle? @params)
    {
        global::Android.Util.Log.Info("QuantumZ", "OnReadyForSpeech called.");
    }

    public void OnBeginningOfSpeech()
    {
        global::Android.Util.Log.Info("QuantumZ", "OnBeginningOfSpeech called.");
    }

    public void OnRmsChanged(float rmsdB)
    {
        // Called frequently; avoid logging to prevent log spam.
    }

    public void OnBufferReceived(byte[]? buffer)
    {
        global::Android.Util.Log.Debug("QuantumZ", $"OnBufferReceived: {buffer?.Length ?? 0} bytes.");
    }

    public void OnEndOfSpeech()
    {
        _isListening = false;
        global::Android.Util.Log.Info("QuantumZ", "OnEndOfSpeech called.");
    }

    public void OnError(SpeechRecognizerError error)
    {
        _isListening = false;
        if (_disposed) return;

        global::Android.Util.Log.Error("QuantumZ", $"SpeechRecognizer error: {error}");

        var delay = error is SpeechRecognizerError.NoMatch or SpeechRecognizerError.Client ? 500 : 100;
        ScheduleRestart(delay);
    }

    public void OnResults(Bundle? results)
    {
        _isListening = false;
        if (_disposed) return;

        var matches = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
        global::Android.Util.Log.Info("QuantumZ", $"OnResults called with {matches?.Count ?? 0} match(es).");

        if (matches?.Count > 0)
        {
            global::Android.Util.Log.Info("QuantumZ", $"Recognized text: '{matches[0]}'");
            _onResult(matches[0]);
        }

        ScheduleRestart(200);
    }

    public void OnPartialResults(Bundle? partialResults)
    {
        var partial = partialResults?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
        if (partial?.Count > 0)
        {
            global::Android.Util.Log.Debug("QuantumZ", $"Partial result: '{partial[0]}'");
        }
    }

    public void OnEvent(int eventType, Bundle? @params)
    {
        global::Android.Util.Log.Debug("QuantumZ", $"OnEvent: type={eventType}");
    }
}
