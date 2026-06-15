using System.Windows.Input;

namespace QuantumZ.UI.ViewModels;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        execute();
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private int _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => Volatile.Read(ref _isExecuting) == 0 && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
            return;

        NotifyCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AsyncRelayCommand Error: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}