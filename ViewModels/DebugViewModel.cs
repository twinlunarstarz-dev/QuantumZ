using System.Collections.ObjectModel;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;
using QuantumZ.UI.ViewModels;

namespace QuantumZ.ViewModels;

public class DebugViewModel : BaseViewModel
{
    private readonly IDebugLogger _logger;
    private string _filterText = string.Empty;

    public DebugViewModel(IDebugLogger logger)
    {
        _logger = logger;
        ClearLogs = new RelayCommand(() => _logger.ClearLogs());
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

    public RelayCommand ClearLogs { get; }
}
