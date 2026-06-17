using Microsoft.Extensions.Logging;
using QuantumZ.Infrastructure.Configuration;
using QuantumZ.UI.Pages;
using QuantumZ.UI.ViewModels;

namespace QuantumZ.UI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddInfrastructure();

        builder.Services.AddSingleton<App>();
        builder.Services.AddTransient<MainAssistantPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<MemoryPage>();
        builder.Services.AddTransient<DebugOverlayPage>();

        builder.Services.AddSingleton<MainAssistantViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<MemoryViewModel>();
        builder.Services.AddSingleton<DebugViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
