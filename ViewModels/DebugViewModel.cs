using System.Collections.ObjectModel;
using System.Text;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.UI.ViewModels;

public class DebugViewModel : BaseViewModel
{
    private readonly IDebugLogger _logger;
    private string _filterText = string.Empty;
    private string _statusMessage = string.Empty;

    public DebugViewModel(IDebugLogger logger)
    {
        _logger = logger;
        ClearLogs = new RelayCommand(() => _logger.ClearLogs());
        CopyLogsCommand = new AsyncRelayCommand(CopyLogsAsync);
        ShareLogsCommand = new AsyncRelayCommand(ShareLogsAsync);
    }

    public ObservableCollection<DebugEvent> Events => _logger.Events;

    public string FilterText
    {
        get => _filterText;
        set 
        { 
            if (_filterText != value)
            {
                _filterText = value;
                OnPropertyChanged();
                // In a real app, we'd implement filtered views here.
                // For now, the UI can handle filtering or we could use an ICollectionView if available in MAUI.
            } 
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public RelayCommand ClearLogs { get; }
    public AsyncRelayCommand CopyLogsCommand { get; }
    public AsyncRelayCommand ShareLogsCommand { get; }

    private async Task CopyLogsAsync()
    {
        await Clipboard.Default.SetTextAsync(FormatLogs());
        StatusMessage = $"Copied {Events.Count} debug event(s) to clipboard.";
    }

    private async Task ShareLogsAsync()
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"quantumz-debug-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        await File.WriteAllTextAsync(path, FormatLogs());
        await Share.Default.RequestAsync(new ShareFileRequest("Export QuantumZ debug log", new ShareFile(path)));
        StatusMessage = $"Prepared debug log export: {Path.GetFileName(path)}";
    }

    private string FormatLogs()
    {
        var builder = new StringBuilder();
        foreach (var debugEvent in Events)
        {
            builder.Append(debugEvent.Timestamp.ToString("O"))
                .Append(" [")
                .Append(debugEvent.Level)
                .Append("] ")
                .Append(debugEvent.Component)
                .Append(": ")
                .Append(debugEvent.Message);

            if (debugEvent.Payload is not null)
                builder.Append(" | ").Append(debugEvent.Payload);

            builder.AppendLine();
        }

        return builder.ToString();
    }
}
