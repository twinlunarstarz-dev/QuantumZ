using Android.App;
using Android.Content;
using Android.OS;
using QuantumZ.Android.Services;

namespace QuantumZ.Android.Receivers;

[BroadcastReceiver(Exported = false)]
[IntentFilter(new[] { "com.quantumz.assistant.START_LISTENING" })]
public class ListenBroadcastReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;

        var action = intent.Action;
        global::Android.Util.Log.Info("QuantumZ", $"BroadcastReceiver received intent: {action}");

        var serviceIntent = new Intent(context, typeof(MicrophoneForegroundService));
        serviceIntent.SetAction(action);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(serviceIntent);
        }
        else
        {
            context.StartService(serviceIntent);
        }
    }
}