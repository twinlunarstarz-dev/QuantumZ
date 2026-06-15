using QuantumZ.UI.ViewModels;

namespace QuantumZ.UI.Pages;

public partial class MemoryPage : ContentPage
{
    public MemoryPage(MemoryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MemoryViewModel vm)
        {
            _ = vm.LoadSummariesAsync();
        }
    }
}
