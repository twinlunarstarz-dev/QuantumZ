using System.Windows.Input;

namespace QuantumZ.UI.ViewModels;

public class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

public class AsyncRelayCommand(Func<Task> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter)
    {
        try
        {
            await execute();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AsyncRelayCommand Error: {ex}");
        }
    }
}