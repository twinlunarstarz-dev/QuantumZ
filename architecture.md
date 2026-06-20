# QuantumZ Architecture (V2)

## Pipeline Flow

Mic (16kHz PCM) → RingBuffer → IWakeWordProvider (80ms chunks)
→ [Trigger Detected] → Snapshot preroll + accumulate query
→ IVadProvider detects end-of-speech
→ IProviderRouter.TranscribeAsync (STT)
→ IAIIntegrationService.ExecutePromptAsync (LLM + MCP tools)
→ IProviderRouter.SynthesizeAsync (TTS)
→ AudioRoutingManager → AudioOutput

## Settings V2

PipelineSettings: per-stage (WakeWord/VAD/STT/LLM/TTS) with Remote|Local|BuiltIn modes.

VoiceAssistantSettings: TriggerPhrase, SystemPrompt, PreRollSeconds, PostSilenceSeconds, WakeWordThreshold, AudioOutput, and MaxToolCallIterations.

## Pipeline States

Idle → ListeningForTrigger → TriggerDetected → RecordingQuery
→ ProcessingSTT → ProcessingLLM → Speaking → ListeningForTrigger

Recoverable failures transition to Error and then auto-recover to ListeningForTrigger when possible.

## Key New Files (V2)

- Core/Models/PipelineState.cs — 8-state enum
- Core/Models/AudioRingBuffer.cs — ring buffer
- Core/Interfaces/IWakeWordProvider.cs — wake word interface
- Core/Interfaces/IPipelineStateService.cs — state machine interface
- Infrastructure/Services/PipelineStateService.cs — state machine
- Infrastructure/Services/OnnxWakeWordProvider.cs — ONNX wake word
- Infrastructure/Services/RmsWakeWordProvider.cs — RMS fallback
- Platforms/Android/Services/MicrophonePipelineController.cs — pipeline logic
- Platforms/Android/Audio/AudioRoutingManager.cs — Android output routing for TTS
- ViewModels/AssistantAnimationViewModel.cs — 8-state orb animations

## Android Audio Capture and Routing

The Android foreground service owns only platform service lifecycle concerns and delegates pipeline behavior to MicrophonePipelineController.

MicrophonePipelineController initializes IWakeWordProvider before opening the continuous audio loop, records 16kHz PCM16 mono chunks through AudioRecord, writes every chunk to AudioRingBuffer, evaluates wake-word frames locally, and only invokes STT after a trigger and VAD-confirmed end-of-speech.

TTS output routing is applied immediately before synthesis/playback using VoiceAssistantSettings.AudioOutput:

- Auto: prefer Bluetooth, then wired headset, then speaker.
- Speaker: force built-in speaker where available.
- Bluetooth: prefer Bluetooth output and use assistant/media audio attributes for WAV playback.
- Headset: prefer Bluetooth or wired headset and fall back to speaker.

After TTS playback completes or fails, routing is restored to the default microphone/listening mode.

## TTS Playback

IProviderRouter.SynthesizeAsync returns provider-specific audio bytes or an empty byte array for built-in Android TTS.

- AndroidTtsEngine self-plays through Android TextToSpeech and returns an empty byte array after utterance completion.
- Remote TtsService returns audio bytes from an OpenAI-compatible audio/speech endpoint.
- LocalAiTtsProvider returns WAV bytes produced by local Piper/Kokoro-style synthesis.

Non-empty TTS bytes are written to a temporary WAV file in the MAUI cache directory and played with Android MediaPlayer using speech audio attributes. Empty bytes are treated as the built-in TTS self-play path.

## Native On-Device Runtime Packaging

Android 10+ does not provide a release-safe path for executing downloaded CLI binaries from app-writable storage. QuantumZ local on-device AI therefore requires packaged Android shared libraries in the APK/AAB rather than downloaded executables such as llama-server, whisper command-line tools, or Piper CLI binaries.

QuantumZ calls stable app-owned wrapper libraries instead of raw upstream shared-library names so managed code can depend on a small flat C ABI while upstream projects evolve:

- libquantumz_llama.so, loaded as quantumz_llama, for local LLM init/load/infer/free wrapper operations.
- libquantumz_whisper.so, loaded as quantumz_whisper, for local STT init/load/transcribe/free wrapper operations.
- libquantumz_piper.so, loaded as quantumz_piper, for optional local TTS init/load/synthesize/free wrapper operations.

Local setup blocks completion when required LLM or STT model files exist without their corresponding packaged QuantumZ wrapper runtime. VAD can use the built-in RMS fallback or ONNX Runtime path, and TTS can use Android built-in TextToSpeech, so Piper remains optional unless local Piper is selected.

## AI and MCP Integration

AIIntegrationService resolves the system prompt from the incoming AiRequest first, then VoiceAssistantSettings.SystemPrompt. It discovers MCP tools once per assistant request and carries the resolved system prompt and available tool list through the LLM tool-call loop.

LlamaAIClient sends OpenAI-compatible chat completions to configured providers, builds a system/user/history message array, and avoids hardcoded LAN endpoints. Runtime endpoints come from settings/model registry/local llama manager configuration.

## Dependency Direction

Dependencies continue to flow inward:

- UI depends on Core abstractions and Infrastructure services.
- Infrastructure implements Core interfaces and does not depend on UI.
- Android platform services depend on Core abstractions plus Android APIs.
- Core contains models and interfaces only.

No circular references are allowed.
