using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace QuantumZ.UI;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int RequestPermissionsCode = 1001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestCriticalPermissions();
    }

    private void RequestCriticalPermissions()
    {
        var permissions = new List<string>();

        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.RecordAudio) != Permission.Granted)
            permissions.Add(Manifest.Permission.RecordAudio);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.PostNotifications) != Permission.Granted)
                permissions.Add(Manifest.Permission.PostNotifications);
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.BluetoothConnect) != Permission.Granted)
                permissions.Add(Manifest.Permission.BluetoothConnect);
        }

        if (permissions.Count > 0)
        {
            ActivityCompat.RequestPermissions(this, permissions.ToArray(), RequestPermissionsCode);
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode != RequestPermissionsCode) return;

        for (var i = 0; i < permissions.Length; i++)
        {
            var granted = grantResults[i] == Permission.Granted;
            global::Android.Util.Log.Info("QuantumZ", $"Permission {permissions[i]}: {(granted ? "GRANTED" : "DENIED")}");
        }
    }
}
