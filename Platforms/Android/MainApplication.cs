using Android.App;
using Android.Runtime;

namespace QuantumZ.UI;

using Microsoft.Extensions.DependencyInjection;

[Application]
public class MainApplication(nint handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    public static IServiceProvider? Services { get; private set; }

    protected override MauiApp CreateMauiApp()
    {
        var app = MauiProgram.CreateMauiApp();
        Services = app.Services;
        return app;
    }
}
