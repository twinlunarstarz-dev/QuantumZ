using QuantumZ.UI.ViewModels;

namespace QuantumZ.UI.Pages;

public partial class DebugOverlayPage : ContentPage
{
    public DebugOverlayPage(DebugViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
