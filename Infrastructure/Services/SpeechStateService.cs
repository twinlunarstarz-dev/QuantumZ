using System;
using QuantumZ.Core.Interfaces;

namespace QuantumZ.Infrastructure.Services;

public class SpeechStateService : ISpeechStateService
{
    private string _currentTranscription = string.Empty;

    public string CurrentTranscription 
    { 
        get => _currentTranscription; 
        private set 
        {
            if (_currentTranscription != value)
            {
                _currentTranscription = value;
                TranscriptionChanged?.Invoke(this, _currentTranscription);
            }
        }
    }

    public event EventHandler<string>? TranscriptionChanged;

    public void UpdateTranscription(string text)
    {
        CurrentTranscription = text ?? string.Empty;
    }

    public void ClearTranscription()
    {
        CurrentTranscription = string.Empty;
    }
}
