using Microsoft.Extensions.DependencyInjection;
using QuantumZ.Core.Interfaces;
using QuantumZ.UI.Pages;

namespace QuantumZ.UI;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        Page rootPage = settingsService.SetupSettings.IsCompleted
            ? _serviceProvider.GetRequiredService<MainAssistantPage>()
            : _serviceProvider.GetRequiredService<SetupPage>();

        return new Window(new NavigationPage(rootPage));
    }
}
