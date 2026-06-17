using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

public sealed class DebugLoggerService(IAudioVisualizer audioVisualizer, ISpeechStateService speechState) : IDebugLogger, IObservable<DebugEvent>, IDisposable
{
    private const int MaxEvents = 1000;

    private readonly ObservableCollection<DebugEvent> _events = [];
    private readonly ConcurrentQueue<DebugEvent> _pendingEvents = new();
    private readonly object _subscriberLock = new();
    private readonly List<IObserver<DebugEvent>> _subscribers = [];
    private int _isBatching;
    private int _pipelineSubscriptionsInitialized;
    private bool _disposed;

    public ObservableCollection<DebugEvent> Events
    {
        get
        {
            EnsurePipelineStateSubscriptions();
            return _events;
        }
    }

    public IObservable<DebugEvent> EventStream
    {
        get
        {
            EnsurePipelineStateSubscriptions();
            return this;
        }
    }

    public IDisposable Subscribe(IObserver<DebugEvent> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        EnsurePipelineStateSubscriptions();

        lock (_subscriberLock)
        {
            _subscribers.Add(observer);
        }

        return new Subscription(this, observer);
    }

    public void LogEvent(DebugEvent @event)
    {
        if (_disposed)
            return;

        System.Diagnostics.Debug.WriteLine($"QuantumZ: {@event.Component}: {@event.Level}: {@event.Message}");
#if ANDROID
        var androidMessage = $"{@event.Component}: {@event.Level}: {@event.Message}";
        switch (@event.Level)
        {
            case LogLevel.Error:
                global::Android.Util.Log.Error("QuantumZ", androidMessage);
                break;
            case LogLevel.Warning:
                global::Android.Util.Log.Warn("QuantumZ", androidMessage);
                break;
            case LogLevel.Trace:
                global::Android.Util.Log.Debug("QuantumZ", androidMessage);
                break;
            default:
                global::Android.Util.Log.Info("QuantumZ", androidMessage);
                break;
        }
#endif

        EnsurePipelineStateSubscriptions();
        _pendingEvents.Enqueue(@event);

        if (Interlocked.Exchange(ref _isBatching, 1) == 0)
        {
            if (MainThread.IsMainThread)
                ProcessPendingEvents();
            else
                MainThread.BeginInvokeOnMainThread(ProcessPendingEvents);
        }
    }

    public void Log(string component, string message, LogLevel level = LogLevel.Info)
    {
        LogEvent(new DebugEvent(DateTime.Now, component, level, message));
    }

    public void LogStateChange(string component, string state, object? payload = null)
    {
        LogEvent(new DebugEvent(DateTime.Now, component, LogLevel.Trace, $"State changed: {state}", payload));
    }

    private void ProcessPendingEvents()
    {
        try
        {
            while (_pendingEvents.TryDequeue(out var debugEvent))
            {
                _events.Add(debugEvent);
                NotifySubscribers(debugEvent);

                if (_events.Count > MaxEvents)
                {
                    _events.RemoveAt(0);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isBatching, 0);

            if (!_pendingEvents.IsEmpty && Interlocked.Exchange(ref _isBatching, 1) == 0)
                MainThread.BeginInvokeOnMainThread(ProcessPendingEvents);
        }
    }

    public void ClearLogs()
    {
        void ClearEvents()
        {
            while (_pendingEvents.TryDequeue(out _)) { }
            _events.Clear();
        }

        if (MainThread.IsMainThread)
            ClearEvents();
        else
            MainThread.BeginInvokeOnMainThread(ClearEvents);
    }

    public void Clear() => ClearLogs();

    public void Dispose()
    {
        _disposed = true;

        lock (_subscriberLock)
        {
            _subscribers.Clear();
        }
    }

    private void EnsurePipelineStateSubscriptions()
    {
        if (Interlocked.Exchange(ref _pipelineSubscriptionsInitialized, 1) == 1)
            return;

        audioVisualizer.StateChanged += (_, state) => LogStateChange("Audio", state.ToString());
        audioVisualizer.ActivityDetectedChanged += (_, detected) => LogStateChange("VAD", detected ? "SpeechDetected" : "SilenceDetected");
        speechState.TranscriptionChanged += (_, text) => LogStateChange("STT", string.IsNullOrWhiteSpace(text) ? "TranscriptionCleared" : "TranscriptionUpdated", text);
    }

    private void NotifySubscribers(DebugEvent debugEvent)
    {
        IObserver<DebugEvent>[] subscribers;
        lock (_subscriberLock)
        {
            subscribers = [.. _subscribers];
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                subscriber.OnNext(debugEvent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"QuantumZ: Debug event subscriber failed: {ex.Message}");
            }
        }
    }

    private void Unsubscribe(IObserver<DebugEvent> observer)
    {
        lock (_subscriberLock)
        {
            _subscribers.Remove(observer);
        }
    }

    private sealed class Subscription(DebugLoggerService logger, IObserver<DebugEvent> observer) : IDisposable
    {
        private int _isDisposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
                logger.Unsubscribe(observer);
        }
    }
}
