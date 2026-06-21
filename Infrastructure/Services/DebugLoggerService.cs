using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Infrastructure.Services;

public sealed class DebugLoggerService(IAudioVisualizer audioVisualizer, ISpeechStateService speechState) : IDebugLogger, IObservable<DebugEvent>, IDisposable
{
    private const int MaxEvents = 1000;
    private static readonly Regex SecretAssignmentPattern = new(
        "(?i)(api[_-]?key|authorization|bearer|token|secret|password)(\\s*[=:]\\s*)([^\\s,;\"'}]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerPattern = new(
        "(?i)bearer\\s+([A-Za-z0-9._~+/=-]{8,})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OpenAiStyleKeyPattern = new(
        "(?i)\\b(sk-[A-Za-z0-9._-]{6,})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] SensitiveMemberNames = ["apikey", "api_key", "authorization", "bearer", "token", "secret", "password"];

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

        var sanitizedEvent = SanitizeEvent(@event);

        System.Diagnostics.Debug.WriteLine($"QuantumZ: {sanitizedEvent.Component}: {sanitizedEvent.Level}: {sanitizedEvent.Message}");
#if ANDROID
        var androidMessage = $"{sanitizedEvent.Component}: {sanitizedEvent.Level}: {sanitizedEvent.Message}";
        switch (sanitizedEvent.Level)
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
        _pendingEvents.Enqueue(sanitizedEvent);

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

    private static DebugEvent SanitizeEvent(DebugEvent debugEvent) =>
        debugEvent with
        {
            Component = SanitizeText(debugEvent.Component),
            Message = SanitizeText(debugEvent.Message),
            Payload = SanitizePayload(debugEvent.Payload)
        };

    private static string SanitizeText(string value)
    {
        var sanitized = BearerPattern.Replace(value, "Bearer ****");
        sanitized = SecretAssignmentPattern.Replace(sanitized, match => $"{match.Groups[1].Value}{match.Groups[2].Value}{MaskSecret(match.Groups[3].Value)}");
        return OpenAiStyleKeyPattern.Replace(sanitized, match => MaskSecret(match.Value));
    }

    private static object? SanitizePayload(object? payload)
    {
        if (payload is null)
            return null;

        if (payload is string text)
            return SanitizeText(text);

        if (payload is System.Collections.IDictionary dictionary)
        {
            var sanitizedDictionary = new Dictionary<string, object?>();
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                var key = entry.Key?.ToString() ?? string.Empty;
                sanitizedDictionary[SanitizeText(key)] = IsSensitiveMemberName(key)
                    ? MaskSecret(entry.Value?.ToString() ?? string.Empty)
                    : SanitizePayload(entry.Value);
            }

            return sanitizedDictionary;
        }

        if (payload is System.Collections.IEnumerable enumerable and not string)
        {
            var sanitizedItems = new List<object?>();
            foreach (var item in enumerable)
                sanitizedItems.Add(SanitizePayload(item));

            return sanitizedItems;
        }

        var type = payload.GetType();
        if (type.IsPrimitive || payload is decimal or DateTime or DateTimeOffset or Guid)
            return payload;

        var sanitizedProperties = new Dictionary<string, object?>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;

            var value = property.GetValue(payload);
            sanitizedProperties[property.Name] = IsSensitiveMemberName(property.Name)
                ? MaskSecret(value?.ToString() ?? string.Empty)
                : SanitizePayload(value);
        }

        return sanitizedProperties.Count == 0 ? SanitizeText(payload.ToString() ?? string.Empty) : sanitizedProperties;
    }

    private static bool IsSensitiveMemberName(string name) =>
        SensitiveMemberNames.Any(sensitive => name.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Contains(sensitive.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase));

    private static string MaskSecret(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        if (trimmed.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
            return trimmed.Length <= 7 ? "sk-****" : $"{trimmed[..3]}****{trimmed[^4..]}";

        return trimmed.Length <= 4 ? "****" : $"****{trimmed[^4..]}";
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
