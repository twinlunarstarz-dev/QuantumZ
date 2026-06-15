using QuantumZ.Core.Interfaces;

namespace QuantumZ.Android.Services;

/// <summary>
/// Android implementation of IDialogService that marshals calls to the main UI thread.
/// </summary>
public sealed class DialogService : IDialogService
{
    public ValueTask ShowAlertAsync(string title, string message, string cancel = "OK")
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var page = Application.Current?.MainPage;
                if (page != null)
                {
                    await page.DisplayAlert(title, message, cancel);
                }
                else
                {
                    global::Android.Util.Log.Warn("QuantumZ", $"Alert (no page): {title} - {message}");
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("QuantumZ", $"ShowAlert failed: {ex}");
            }
            finally
            {
                tcs.TrySetResult();
            }
        });
        return new ValueTask(tcs.Task);
    }

    public ValueTask<bool> ShowConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No")
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var page = Application.Current?.MainPage;
                if (page != null)
                {
                    var result = await page.DisplayAlert(title, message, accept, cancel);
                    tcs.TrySetResult(result);
                }
                else
                {
                    global::Android.Util.Log.Warn("QuantumZ", $"Confirm (no page): {title} - {message}");
                    tcs.TrySetResult(false);
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("QuantumZ", $"ShowConfirm failed: {ex}");
                tcs.TrySetResult(false);
            }
        });
        return new ValueTask<bool>(tcs.Task);
    }
}
