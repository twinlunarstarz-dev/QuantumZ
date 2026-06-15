using QuantumZ.ViewModels;

namespace QuantumZ.Pages;

public partial class DebugOverlayPage : ContentPage
{
    public DebugOverlayPage(DebugViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
