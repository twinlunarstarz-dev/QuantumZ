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
    private readonly Action<string>? _onPartialResult;
    private SpeechRecognizer? _speechRecognizer;
    private bool _isListening;
    private bool _disposed;
    private int _consecutiveFailures = 0;

    public OnDeviceSpeechRecognizer(Context context, Action<string> onResult, Action<string>? onPartialResult = null)
    {
        _context = context;
        _onResult = onResult;
        _onPartialResult = onPartialResult;

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

    private void ScheduleRestart(int baseDelayMs)
    {
        if (_disposed) return;

        // Implement exponential backoff for consecutive failures to prevent hammering the service.
        // Max delay capped at 10 seconds.
        int actualDelay = Math.Min(baseDelayMs * (int)Math.Pow(2, _consecutiveFailures), 10000);
        
        Task.Run(async () =>
        {
            await Task.Delay(actualDelay);
            if (!_disposed)
            {
                global::Android.Util.Log.Info("QuantumZ", $"Restarting recognizer after {actualDelay}ms delay (failure count: {_consecutiveFailures}).");
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

        // Treat ServerDisconnected as a transient warning rather than a critical error to reduce log noise.
        bool isTransient = error == SpeechRecognizerError.ServerDisconnected || error == SpeechRecognizerError.Network;
        if (isTransient)
            global::Android.Util.Log.Warn("QuantumZ", $"SpeechRecognizer transient error: {error}. Attempting silent restart...");
        else
            global::Android.Util.Log.Error("QuantumZ", $"SpeechRecognizer critical error: {error}");

        // Increment failure count for backoff unless it's a trivial "no match" or expected transient disconnect.
        if (error is not SpeechRecognizerError.NoMatch && !isTransient)
        {
            _consecutiveFailures++;
        }
        else if (isTransient)
        {
            // Reset failure count for transients to keep restart delays low and responsive.
            _consecutiveFailures = Math.Max(0, _consecutiveFailures - 1);
        }

        // Use a very aggressive restart delay for transient server disconnects to minimize the "gap" in listening.
        var delay = error switch
        {
            SpeechRecognizerError.NoMatch => 500,
            SpeechRecognizerError.ServerDisconnected or SpeechRecognizerError.Network => 100,
            SpeechRecognizerError.Client => 500,
            _ => 200
        };

        ScheduleRestart(delay);
    }

    public void OnResults(Bundle? results)
    {
        _isListening = false;
        if (_disposed) return;

        // Reset failure count on successful result.
        _consecutiveFailures = 0;

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
            _onPartialResult?.Invoke(partial[0]);
        }
    }

    public void OnEvent(int eventType, Bundle? @params)
    {
        global::Android.Util.Log.Debug("QuantumZ", $"OnEvent: type={eventType}");
    }
}
