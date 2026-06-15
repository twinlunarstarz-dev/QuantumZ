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
        var mainAssistantPage = _serviceProvider.GetRequiredService<Pages.MainAssistantPage>();

        return new Window(new NavigationPage(mainAssistantPage));
    }
}
