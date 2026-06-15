using QuantumZ.UI.ViewModels;

namespace QuantumZ.UI.Pages;

public partial class MainAssistantPage : ContentPage
{
    private CancellationTokenSource? _pulseCancellation;
    private CancellationTokenSource? _waveformCancellation;
    private bool _visualizerEventsAttached;

    public MainAssistantPage(MainAssistantViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;

        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainAssistantViewModel.IsDetailViewOpen))
            {
                var vm = (MainAssistantViewModel)s!;
                _ = TransitionToDetailAsync(vm.IsDetailViewOpen);
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
    private async Task TransitionToDetailAsync(bool isOpen)
    {
        if (isOpen)
        {
            await Task.WhenAll(
                FocusContainer.ScaleTo(0.92, 400, Easing.CubicOut),
                FocusContainer.TranslateTo(0, -20, 400, Easing.CubicOut)
            );

            await Task.WhenAll(
                DetailPanel.TranslateTo(0, 0, 500, Easing.CubicOut),
                DetailPanel.FadeTo(1, 500, Easing.CubicOut)
            );
        }
        else
        {
            await Task.WhenAll(
                DetailPanel.TranslateTo(0, 500, 400, Easing.CubicIn),
                DetailPanel.FadeTo(0, 400, Easing.CubicIn)
            );

            await Task.WhenAll(
                FocusContainer.ScaleTo(1.0, 400, Easing.CubicOut),
                FocusContainer.TranslateTo(0, 0, 400, Easing.CubicOut)
            );
        }
    }
}