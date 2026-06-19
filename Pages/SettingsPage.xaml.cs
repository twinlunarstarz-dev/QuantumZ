using QuantumZ.UI.ViewModels;

namespace QuantumZ.UI.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        vm.LoadSettings();
    }
}
