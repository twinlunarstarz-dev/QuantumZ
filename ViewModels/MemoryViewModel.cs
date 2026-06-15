using System.Collections.ObjectModel;
using System.Windows.Input;
using QuantumZ.Core.Interfaces;
using QuantumZ.Core.Models;

namespace QuantumZ.UI.ViewModels;

public class MemoryViewModel(IMemoryService memoryService) : BaseViewModel
{
    private readonly IMemoryService _memoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));

    private DateTime _startDate = DateTime.UtcNow.AddDays(-7);
    public DateTime StartDate
    {
        get => _startDate;
        set
        {
            if (_startDate != value)
            {
                _startDate = value;
                OnPropertyChanged();
                _ = LoadSummariesAsync();
            }
        }
    }

    private DateTime _endDate = DateTime.UtcNow;
    public DateTime EndDate
    {
        get => _endDate;
        set
        {
            if (_endDate != value)
            {
                _endDate = value;
                OnPropertyChanged();
                _ = LoadSummariesAsync();
            }
        }
    }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public ObservableCollection<DailySummary> Summaries { get; } = [];
    public ObservableCollection<MemorySearchResult> SearchResults { get; } = [];

    private ICommand? _searchCommand;
    public ICommand SearchCommand => _searchCommand ??= new AsyncRelayCommand(SearchAsync);

    private ICommand? _refreshCommand;
    public ICommand RefreshCommand => _refreshCommand ??= new AsyncRelayCommand(LoadSummariesAsync);

    public async Task LoadSummariesAsync()
    {
        try
        {
            Summaries.Clear();
            var items = await _memoryService.GetSummariesAsync(StartDate, EndDate);
            foreach (var item in items)
            {
                Summaries.Add(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadSummaries failed: {ex}");
        }
    }

    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        try
        {
            SearchResults.Clear();
            var items = await _memoryService.SearchMemoryAsync(SearchQuery.Trim(), 20);
            foreach (var item in items)
            {
                SearchResults.Add(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SearchMemory failed: {ex}");
        }
    }
}
