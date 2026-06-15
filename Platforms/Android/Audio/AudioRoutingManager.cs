using Android.Content;
using Android.Media;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models.Settings;

namespace QuantumZ.Android.Audio;

public sealed class AudioRoutingManager : Java.Lang.Object, AudioManager.IOnCommunicationDeviceChangedListener
{
    private readonly AudioManager _audioManager;
    private readonly ISettingsService _settings;
    private readonly Context _context;

    public AudioRoutingManager(Context context, ISettingsService settings)
    {
        _context = context;
        _settings = settings;
        _audioManager = (AudioManager)context.GetSystemService(Context.AudioService)!;
    }

    public void Initialize()
    {
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
        {
            _audioManager.AddOnCommunicationDeviceChangedListener(_context.MainExecutor!, this);
        }
        ApplyRoutingPreference();
    }

    public void ApplyRoutingPreference()
    {
        var preference = _settings.AudioRouting;

        switch (preference)
        {
            case AudioRoutingPreference.AlwaysSpeaker:
                ForceSpeaker();
                break;
            case AudioRoutingPreference.AlwaysHeadset:
                ForceHeadsetOrBluetooth();
                break;
            case AudioRoutingPreference.Dynamic:
                ApplyDynamicRouting();
                break;
            default:
                // Default: let system handle it but ensure communication mode
                _audioManager.Mode = Mode.InCommunication;
                break;
        }
    }

    public void OnCommunicationDeviceChanged(AudioDeviceInfo? device)
    {
        if (_settings.AudioRouting == AudioRoutingPreference.Dynamic)
        {
            ApplyDynamicRouting();
        }
    }

    private void ApplyDynamicRouting()
    {
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
        {
            var devices = _audioManager.AvailableCommunicationDevices;
            foreach (var d in devices)
            {
                if (d.Type == AudioDeviceType.BluetoothSco || d.Type == AudioDeviceType.BluetoothA2dp)
                {
                    _audioManager.SetCommunicationDevice(d);
                    _audioManager.Mode = Mode.InCommunication;
                    return;
                }
            }
            foreach (var d in devices)
            {
                if (d.Type == AudioDeviceType.WiredHeadset || d.Type == AudioDeviceType.WiredHeadphones)
                {
                    _audioManager.SetCommunicationDevice(d);
                    _audioManager.Mode = Mode.InCommunication;
                    return;
                }
            }
        }

        // Fallback to speakerphone for assistant responses
        _audioManager.Mode = Mode.InCommunication;
        _audioManager.SpeakerphoneOn = true;
    }

    private void ForceSpeaker()
    {
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
        {
            var devices = _audioManager.AvailableCommunicationDevices;
            foreach (var d in devices)
            {
                if (d.Type == AudioDeviceType.BuiltinSpeaker)
                {
                    _audioManager.SetCommunicationDevice(d);
                    break;
                }
            }
        }
        _audioManager.Mode = Mode.InCommunication;
        _audioManager.SpeakerphoneOn = true;
    }

    private void ForceHeadsetOrBluetooth()
    {
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
        {
            var devices = _audioManager.AvailableCommunicationDevices;
            foreach (var d in devices)
            {
                if (d.Type == AudioDeviceType.BluetoothSco || d.Type == AudioDeviceType.BluetoothA2dp)
                {
                    _audioManager.SetCommunicationDevice(d);
                    _audioManager.Mode = Mode.InCommunication;
                    return;
                }
            }
            foreach (var d in devices)
            {
                if (d.Type == AudioDeviceType.WiredHeadset || d.Type == AudioDeviceType.WiredHeadphones)
                {
                    _audioManager.SetCommunicationDevice(d);
                    _audioManager.Mode = Mode.InCommunication;
                    return;
                }
            }
        }

        // If no headset, fallback to speaker
        _audioManager.Mode = Mode.InCommunication;
        _audioManager.SpeakerphoneOn = true;
    }

    public void Dispose()
    {
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S)
        {
            _audioManager.RemoveOnCommunicationDeviceChangedListener(this);
        }
    }
}
