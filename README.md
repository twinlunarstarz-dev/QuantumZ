# QuantumZ

QuantumZ is a modular Android voice assistant application built with .NET MAUI.

## Architecture

QuantumZ follows a modular design to separate concerns and prevent monolithic growth:

- **`QuantumZ.Core`**: Domain models, interfaces, and core business logic. No dependencies on other modules.
- **`QuantumZ.Infrastructure`**: Implementation of external services (llama.cpp REST clients, MCP Client), persistence (SQLite), and low-level utilities.
- **`QuantumZ.Android`**: Platform-specific implementations, including the Android Foreground Service (`microphone` type) and `AudioManager` routing logic.
- **`QuantumZ.UI`**: MAUI Pages, ViewModels (MVVM), and styling/theming ("Cyber Red / Dark").

## On-Device Features: arm64-v8a Only

**Starting with v1, all on-device AI features (Whisper STT, Llama LLM) are locked to the `arm64-v8a` architecture.**

### Why arm64-v8a Only?

- Native shared libraries (`libquantumz_llama.so`, `libquantumz_whisper.so`) are packaged only for `arm64-v8a`.
- Other Android ABIs (armeabi-v7a, x86, x86_64) cannot load these libraries and will not have access to on-device LLM or STT.
- This decision reduces build complexity and ensures optimal performance on modern 64-bit Android devices.

### Guidance for Users of Other Architectures

If your device uses a different architecture (armeabi-v7a, x86, x86_64):

- **On-device LLM and STT will not be available.**
- You can still use remote providers for LLM and STT by configuring appropriate endpoints in settings.
- TTS uses Android's built-in TextToSpeech service, which works on all architectures.

## TTS: Android Built-in Fallback

**Piper TTS support has been dropped for v1.** The on-device voice output uses Android's built-in `TextToSpeech` service as the default fallback.

- No additional voice model downloads are required for basic TTS functionality.
- The `LocalAiTtsProvider` (Piper) is retained in the codebase but reports as unavailable in v1.
- Future versions may reintroduce local TTS with a different implementation.

## Native Libraries

The following native shared libraries are packaged for `arm64-v8a`:

| Library | Purpose |
|---------|---------|
| `libquantumz_llama.so` | Local LLM inference (llama.cpp wrapper) |
| `libquantumz_whisper.so` | Local STT transcription (whisper.cpp wrapper) |
| `libllama.so`, `libggml*.so` | llama.cpp dependencies |
| `libwhisper.so` | whisper.cpp core library |

**Note:** `libquantumz_piper.so` is **not** packaged. Piper TTS was removed for v1.

## Building

```bash
dotnet build
```

The project must compile with zero warnings and zero errors (`TreatWarningsAsErrors` is enabled).

## License

See individual component licenses in the `Native/third_party/` directory.
