using QuantumZ.UI.ViewModels;

namespace QuantumZ.UI.Pages;

public partial class MainAssistantPage : ContentPage
{
    private enum HudPanelMode
    {
        None,
        Detail,
        Memory,
        Config
    }

    private CancellationTokenSource? _pulseCancellation;
    private CancellationTokenSource? _waveformCancellation;
    private bool _visualizerEventsAttached;
    private readonly SemaphoreSlim _panelTransitionSemaphore = new(1, 1);
    private HudPanelMode _currentPanelMode = HudPanelMode.None;

    public MainAssistantPage(MainAssistantViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;

        viewModel.PropertyChanged += (s, e) =>
        {
            var vm = (MainAssistantViewModel)s!;
            if (e.PropertyName == nameof(MainAssistantViewModel.IsDetailViewOpen) && vm.IsDetailViewOpen)
            {
                _ = TransitionToPanelAsync(HudPanelMode.Detail);
            }
            else if (e.PropertyName == nameof(MainAssistantViewModel.IsMemoryViewOpen) && vm.IsMemoryViewOpen)
            {
                _ = TransitionToPanelAsync(HudPanelMode.Memory);
            }
            else if (e.PropertyName == nameof(MainAssistantViewModel.IsConfigViewOpen) && vm.IsConfigViewOpen)
            {
                _ = TransitionToPanelAsync(HudPanelMode.Config);
            }
            else if ((e.PropertyName == nameof(MainAssistantViewModel.IsDetailViewOpen)
                    || e.PropertyName == nameof(MainAssistantViewModel.IsMemoryViewOpen)
                    || e.PropertyName == nameof(MainAssistantViewModel.IsConfigViewOpen))
                    && !vm.IsDetailViewOpen
                    && !vm.IsMemoryViewOpen
                    && !vm.IsConfigViewOpen)
            {
                _ = TransitionToPanelAsync(HudPanelMode.None);
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MainAssistantViewModel vm)
        {
            if (!_visualizerEventsAttached)
            {
                vm.AttachVisualizerEvents();
                _visualizerEventsAttached = true;
            }

            _ = vm.InitializeAssetsAsync();
        }
        StartPulseAnimation();
        StartWaveformAnimation();
    }

    protected override void OnDisappearing()
    {
        _pulseCancellation?.Cancel();
        _pulseCancellation?.Dispose();
        _pulseCancellation = null;
        _waveformCancellation?.Cancel();
        _waveformCancellation?.Dispose();
        _waveformCancellation = null;
        if (_visualizerEventsAttached && BindingContext is MainAssistantViewModel vm)
        {
            vm.DetachVisualizerEvents();
            _visualizerEventsAttached = false;
        }

        base.OnDisappearing();
    }
    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is MainAssistantViewModel vm && vm.ClosePanels())
            return true;

        return base.OnBackButtonPressed();
    }

    private void ListeningSwitch_Toggled(object? sender, ToggledEventArgs e)
    {
        if (BindingContext is not MainAssistantViewModel vm || vm.IsListening == e.Value)
            return;

        _ = vm.SetListeningAsync(e.Value);
    }

    private void StartPulseAnimation()
    {
        _pulseCancellation?.Cancel();
        _pulseCancellation?.Dispose();
        _pulseCancellation = new CancellationTokenSource();
        var token = _pulseCancellation.Token;
        _ = PulseAsync(token);
    }

    private async Task PulseAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await AssistantRing.ScaleTo(1.035, 900, Easing.CubicInOut);
                await AssistantOrb.ScaleTo(1.08, 900, Easing.CubicInOut);
                if (token.IsCancellationRequested) return;
                await AssistantRing.ScaleTo(1.0, 900, Easing.CubicInOut);
                await AssistantOrb.ScaleTo(1.0, 900, Easing.CubicInOut);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private void StartWaveformAnimation()
    {
        _waveformCancellation?.Cancel();
        _waveformCancellation?.Dispose();
        _waveformCancellation = new CancellationTokenSource();
        _ = AnimateWaveformAsync(_waveformCancellation.Token);
    }

    private async Task AnimateWaveformAsync(CancellationToken token)
    {
        var bars = new[] { WaveBar1, WaveBar2, WaveBar3, WaveBar4, WaveBar5, WaveBar6, WaveBar7, WaveBar8, WaveBar9, WaveBar10 };
        var phase = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var level = BindingContext is MainAssistantViewModel vm ? Math.Clamp(vm.AudioLevel, 0.08, 1.0) : 0.08;
                var activeBoost = BindingContext is MainAssistantViewModel { IsDetectingActivity: true } ? 0.24 : 0.0;

                for (var index = 0; index < bars.Length; index++)
                {
                    var wave = 0.45 + (0.55 * Math.Abs(Math.Sin((phase + index) * 0.72)));
                    bars[index].ScaleY = Math.Clamp(0.18 + (level * wave) + activeBoost, 0.18, 1.55);
                    bars[index].Opacity = Math.Clamp(0.36 + level + activeBoost, 0.36, 1.0);
                }

                phase++;
                await Task.Delay(90, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }
    private async Task TransitionToPanelAsync(HudPanelMode mode)
    {
        if (!await _panelTransitionSemaphore.WaitAsync(0).ConfigureAwait(true))
            return;

        try
        {
            if (_currentPanelMode == mode)
                return;

            _currentPanelMode = mode;
            var target = mode switch
            {
                HudPanelMode.Detail => DetailPanel,
                HudPanelMode.Memory => MemoryPanel,
                HudPanelMode.Config => ConfigPanel,
                _ => null
            };

            var panels = new[] { DetailPanel, MemoryPanel, ConfigPanel };

            if (target is not null)
            {
                PanelOverlay.InputTransparent = false;
                await Task.WhenAll(
                    PanelOverlay.FadeTo(1.0, 220, Easing.CubicOut),
                    FocusContainer.FadeTo(0.18, 260, Easing.CubicOut),
                    FocusContainer.ScaleTo(0.96, 260, Easing.CubicOut),
                    FocusContainer.TranslateTo(0, -10, 260, Easing.CubicOut)
                );
                FocusContainer.InputTransparent = true;

                foreach (var panel in panels.Where(panel => panel != target))
                {
                    panel.Opacity = 0;
                    panel.TranslationY = 80;
                    panel.InputTransparent = true;
                }

                target.InputTransparent = false;
                await Task.WhenAll(
                    target.TranslateTo(0, 0, 500, Easing.CubicOut),
                    target.FadeTo(1, 500, Easing.CubicOut)
                );
            }
            else
            {
                await Task.WhenAll(panels.Select(panel => Task.WhenAll(
                    panel.TranslateTo(0, 80, 300, Easing.CubicIn),
                    panel.FadeTo(0, 360, Easing.CubicIn))));

                foreach (var panel in panels)
                {
                    panel.InputTransparent = true;
                }
                PanelOverlay.InputTransparent = true;

                FocusContainer.InputTransparent = false;

                await Task.WhenAll(
                    PanelOverlay.FadeTo(0, 220, Easing.CubicIn),
                    FocusContainer.FadeTo(1.0, 320, Easing.CubicOut),
                    FocusContainer.ScaleTo(1.0, 360, Easing.CubicOut),
                    FocusContainer.TranslateTo(0, 0, 360, Easing.CubicOut)
                );
            }
        }
        finally
        {
            _panelTransitionSemaphore.Release();
        }
    }
}
