using QuantumZ.UI.ViewModels;

namespace QuantumZ.UI.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.Load();
    }
}
