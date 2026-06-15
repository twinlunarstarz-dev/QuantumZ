namespace QuantumZ.Core.Interfaces;

/// <summary>
/// Cross-platform abstraction for showing alert dialogs to the user.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a simple alert with an OK button.
    /// </summary>
    ValueTask ShowAlertAsync(string title, string message, string cancel = "OK");

    /// <summary>
    /// Shows a confirmation dialog and returns the user's choice.
    /// </summary>
    ValueTask<bool> ShowConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No");
}
