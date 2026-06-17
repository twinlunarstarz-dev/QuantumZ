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

public sealed class RelayCommand<T>(Action<T?> execute, Predicate<T?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (canExecute is null)
            return true;

        var typed = parameter is null ? default : (T?)ConvertParameter(parameter);
        return canExecute(typed);
    }

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        var typed = parameter is null ? default : (T?)ConvertParameter(parameter);
        execute(typed);
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static object? ConvertParameter(object parameter)
    {
        if (parameter is T)
            return parameter;

        if (parameter is IConvertible convertible && typeof(IConvertible).IsAssignableFrom(typeof(T)))
            return Convert.ChangeType(convertible, typeof(T), null);

        return parameter;
    }
}

public sealed class AsyncRelayCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null,
    Action<Exception>? onException = null) : ICommand
{
    private int _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => Volatile.Read(ref _isExecuting) == 0 && (canExecute?.Invoke() ?? true);

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
            return;

        NotifyCanExecuteChanged();
        _ = ExecuteCoreAsync();
    }

    private async Task ExecuteCoreAsync()
    {
        try
        {
            await execute();
        }
        catch (Exception ex)
        {
            if (onException is not null)
            {
                onException(ex);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"AsyncRelayCommand Error: {ex}");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand<T>(
    Func<T?, Task> execute,
    Predicate<T?>? canExecute = null,
    Action<Exception>? onException = null) : ICommand
{
    private int _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (Volatile.Read(ref _isExecuting) != 0)
            return false;

        if (canExecute is null)
            return true;

        var typed = parameter is null ? default : (T?)ConvertParameter(parameter);
        return canExecute(typed);
    }

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
            return;

        var typed = parameter is null ? default : (T?)ConvertParameter(parameter);
        NotifyCanExecuteChanged();
        _ = ExecuteCoreAsync(typed);
    }

    private async Task ExecuteCoreAsync(T? parameter)
    {
        try
        {
            await execute(parameter);
        }
        catch (Exception ex)
        {
            if (onException is not null)
            {
                onException(ex);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"AsyncRelayCommand<T> Error: {ex}");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static object? ConvertParameter(object parameter)
    {
        if (parameter is T)
            return parameter;

        if (parameter is IConvertible convertible && typeof(IConvertible).IsAssignableFrom(typeof(T)))
            return Convert.ChangeType(convertible, typeof(T), null);

        return parameter;
    }
}
