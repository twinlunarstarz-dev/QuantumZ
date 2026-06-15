using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.OS;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.Android.Services;

/// <summary>
/// Android implementation of thermal monitoring using BatteryManager and system properties.
/// </summary>
public class AndroidThermalMonitorService : IThermalMonitor, IDisposable
{
    private readonly Context _context;
    private readonly IDebugLogger? _debugLogger;
    private ThermalState _currentState = new(ThermalLevel.Normal, 0f, null, null, DateTimeOffset.UtcNow);
    private Timer? _sampleTimer;

    public ThermalState CurrentState => _currentState;

    public event EventHandler<ThermalState>? StateChanged;

    public AndroidThermalMonitorService(Context context, IDebugLogger? debugLogger = null)
    {
        _context = context;
        _debugLogger = debugLogger;

        // Sample every 30 seconds to avoid battery drain while maintaining responsiveness.
        _sampleTimer = new Timer(async _ => await SampleAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    public async ValueTask<ThermalState> SampleAsync(CancellationToken ct = default)
    {
        float batteryTemp = GetBatteryTemperature();
        float? cpuTemp = GetCpuTemperature();
        float? gpuTemp = GetGpuTemperature();

        var level = DetermineLevel(batteryTemp, cpuTemp);
        var newState = new ThermalState(level, batteryTemp, cpuTemp, gpuTemp, DateTimeOffset.UtcNow);

        if (newState.Level != _currentState.Level)
        {
            _debugLogger?.Log("Thermal", $"Thermal state changed: {_currentState.Level} -> {newState.Level} (Batt: {batteryTemp}°C)");
            StateChanged?.Invoke(this, newState);
        }

        _currentState = newState;
        return _currentState;
    }

    private ThermalLevel DetermineLevel(float batteryTemp, float? cpuTemp)
    {
        // Thresholds based on typical Android device thermal profiles.
        const float WarningThreshold = 40f;
        const float CriticalThreshold = 45f;

        float maxTemp = Math.Max(batteryTemp, cpuTemp ?? 0f);

        return maxTemp switch
        {
            >= CriticalThreshold => ThermalLevel.Critical,
            >= WarningThreshold => ThermalLevel.Warning,
            _ => ThermalLevel.Normal
        };
    }

    private float GetBatteryTemperature()
    {
        try
        {
            var intent = _context.RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
            if (intent == null) return 0f;

            // Battery temperature is reported in tenths of a degree Celsius.
            int tempTenths = intent.GetIntExtra(global::Android.OS.BatteryManager.ExtraTemperature, 0);
            return tempTenths / 10f;
        }
        catch (Exception ex)
        {
            _debugLogger?.Log("Thermal", $"Error reading battery temperature: {ex.Message}");
            return 0f;
        }
    }

    private float? GetCpuTemperature()
    {
        // CPU temp is highly device-specific on Android and often requires root or specific vendor APIs.
        // We attempt to read from common sysfs paths if available, otherwise return null.
        string[] possiblePaths = 
        [
            "/sys/class/thermal/thermal_zone0/temp",
            "/sys/class/thermal/thermal_zone1/temp",
            "/proc/cpuinfo" // Fallback for some older devices (though harder to parse)
        ];

        foreach (var path in possiblePaths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                string content = File.ReadAllText(path).Trim();
                if (int.TryParse(content, out int tempTenths))
                {
                    return tempTempC(tempTenths);
                }
            }
            catch { /* Ignore */ }
        }

        return null;
    }

    private float? GetGpuTemperature() => null; // GPU temperature is rarely exposed via standard sysfs without root.

    private float tempTempC(int tenths) 
    {
        // Some devices report in millidegrees, some in tenths.
        return tenths > 1000 ? tenths / 1000f : tenths / 10f;
    }

    public void Dispose()
    {
        _sampleTimer?.Dispose();
    }
}
