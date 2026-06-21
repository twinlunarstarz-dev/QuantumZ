# QuantumZ — Full Implementation Plan (Make It a Working Assistant)

> Goal: Turn QuantumZ into a **fully functional voice AI assistant** that works in two modes:
> 1. **Remote mode** — talks to an OpenAI-compatible server for LLM, STT (speech-to-text), and TTS (text-to-speech).
> 2. **On-device mode** — uses bundled native libraries (llama for LLM, whisper for STT) and Android's built-in TTS.
>
> The **UI and navigation must be correct and usable** for a normal person.
>
> This plan is written so a **weak LLM can follow it step by step**. Do the phases **in order**. Do **not** skip ahead. After every phase, **build the app** and confirm there are **zero errors and zero warnings** before moving on.

---

## Phase 0 — Ground Rules (READ FIRST, DO NOT SKIP)

Follow these rules for the whole job:

1. **One file at a time.** Open a file, read it fully, make the change, save it. Do not edit many files blindly.
2. **Do not re-read the same file over and over.** Read it once, remember what you saw.
3. **Build often.** After each phase run the build command below. If it fails, fix it before moving on.
4. **Zero warnings rule.** The project has `TreatWarningsAsErrors=true`. A warning is treated as an error. Your code must be clean.
5. **Respect the architecture (from `AGENTS.md`):**
   - `Core` depends on nothing.
   - `Infrastructure` depends only on `Core`.
   - `Android` depends on `Core`.
   - `UI` depends on `Infrastructure` and `Core`.
   - **Never** create circular references.
6. **No hardcoded secrets or endpoints.** API keys and server URLs come from settings, never typed into source code.
7. **Use .NET 10 style** already used in the codebase: primary constructors, `[]` collection syntax, `required` members, `ValueTask` for hot paths.
8. **Keep files under 1,000 lines.** If a file gets too big, split it.
9. **Do not invent new files unless this plan tells you to.** Most fixes are edits to existing files.
10. **If a step says "decide", use the recommended option** unless the human told you otherwise.

### Build command (run this after every phase)

```powershell
dotnet build QuantumZ.csproj -c Debug -f net10.0-android36.0
```

- **Pass = zero errors AND zero warnings.**
- If you see native/CMake errors, that is Phase 1. Everything else is C#/XAML.

### Where to confirm prior decisions (per `AGENTS.md` Knowledge Protocol)

Before changing behavior you are unsure about:
1. Check the Obsidian vault (MCP) for prior notes/decisions.
2. If not found, check official .NET 10 / Android API 36 docs.
3. Only then implement, based on evidence.

---

## Phase 1 — Stop the CMake / VSCode Error Noise

**Problem:** When the project loads in VSCode, CMake Tools tries to build the `Native/` folder using the **Windows** compiler (`Visual Studio 17 2022`, `x64`). The file `Native/CMakeLists.txt` is **designed to fail** unless it is built with the **Android NDK** toolchain. So you see a scary error. This is **not** an app crash — it is just the editor configuring the wrong thing.

**Fix:** Tell VSCode CMake Tools to **stop auto-configuring** the `Native/` folder.

**Steps:**
1. Open or create the file `.vscode/settings.json`.
2. Add these keys (merge with anything already there — do not delete existing settings):

```jsonc
{
  "cmake.configureOnOpen": false,
  "cmake.automaticReconfigure": false,
  "cmake.sourceDirectory": "${workspaceFolder}/Native"
}
```

3. The native `.so` files are **already built and committed** in `Platforms/Android/NativeLibs/arm64-v8a/` (you can see `libllama.so`, `libwhisper.so`, `libquantumz_llama.so`, `libquantumz_whisper.so`, `libggml*.so`). So you do **not** need to rebuild native code to run the app. Building native code is only needed if a wrapper changes (Phase 11).

**Acceptance:**
- Reload VSCode. The CMake `FATAL_ERROR` about Android NDK no longer appears on startup.
- `dotnet build` still succeeds.

---

## Phase 2 — Pick ONE Source of Truth for Settings (Most Important Decision)

**Problem:** There are **two** settings systems and they disagree:
- **New:** `PipelineSettings` (what the Settings screen mostly edits).
- **Legacy:** `LlmSettings`, `SttSettings`, `TtsSettings`, `VadSettings`, plus `UseOnDeviceStt`, `UseLocalTts`, `WhisperModelPath` (what the **runtime actually reads** through `settings.GetActiveProvider("LLM"/"STT"/"TTS"/"VAD")`).

Because the UI writes the **new** settings but the runtime reads the **legacy** settings, **changing settings in the app does nothing.** This must be fixed or nothing else will work end-to-end.

**Decision (recommended): Make the LEGACY provider settings the single source of truth that the runtime reads.** Keep `PipelineSettings` as the editable shape in the UI, but **every time you save**, also **write the legacy settings** so the runtime sees the change. This is the smallest, safest change because the runtime already reads legacy settings everywhere.

**Where the runtime reads settings (do not change these — just feed them correctly):**
- `Infrastructure/Services/LlamaAIClient.cs` → `settings.GetActiveProvider("LLM")`
- `Infrastructure/Services/RemoteSttEngine.cs` → `settings.GetActiveProvider("STT")`
- `Infrastructure/Services/TtsService.cs` → `settings.GetActiveProvider("TTS")`
- `Infrastructure/Services/SettingsService.cs` → `GetActiveProvider(...)` (lines ~183–190) maps the service name to the legacy `*Settings`.

**What `GetActiveProvider` must return for each mode:**
- **Remote mode:** a provider config pointing at the user's server URL, model id, and API key.
- **On-device mode:** for LLM → local (`http://localhost:8025/v1` or the native local manager); for STT → `UseOnDeviceStt = true` + a valid `WhisperModelPath`; for TTS → `UseLocalTts` (Piper) or Android TTS fallback.

**Acceptance for this phase (concept only — wiring happens in Phase 5):**
- You can clearly state, in one sentence, where the runtime reads each setting and what value it expects for remote vs on-device. Write that down as a comment block at the top of `SettingsService.cs` so future steps stay consistent.

---

## Phase 3 — Fix the Main Screen Controls and Navigation

**Problem A — No visible "Listen" control.** `Pages/MainAssistantPage.xaml` shows three header buttons: `DETAILS`, `MEMORY`, `CONFIG`. There is **no visible button or switch** to actually start/stop listening, even though `MainAssistantViewModel` already has a `ToggleListeningCommand`. The user cannot use the assistant.

**Problem B — CONFIG button is confusing.** The `CONFIG` header button is bound to `NavigateToSettingsCommand` (opens the **full Settings page**), but there is also an **in-page `ConfigPanel`** that never opens. Pick one behavior.

**Steps:**

1. Open `Pages/MainAssistantPage.xaml`.
2. Add a **clearly visible primary control** to start/stop listening. The view model exposes:
   - Command: `ToggleListeningCommand`
   - State: `IsListening` (bool)
   - There is a converter `UI/Converters/BoolToListenTextConverter.cs` to show text like "START LISTENING" / "STOP LISTENING".
   Add a large button bound like this (place it as the main action, centered, easy to tap):

```xml
<Button
    Text="{Binding IsListening, Converter={StaticResource BoolToListenText}}"
    Command="{Binding ToggleListeningCommand}"
    Style="{StaticResource PrimaryActionButtonStyle}"
    AutomationProperties.Name="Start or stop listening"
    HeightRequest="64" />
```

   - If `BoolToListenText` is not already declared in the page/app resources, declare it (look at how `HexToColor` or other converters are declared in this file or `App.xaml`).
   - If `PrimaryActionButtonStyle` does not exist, use an existing button style from `Resources/Styles/Styles.xaml`, or set explicit colors that match the "Cyber Red / Dark" theme.

3. **Decide CONFIG behavior. Recommended:** keep `CONFIG` opening the **full Settings page** (`NavigateToSettingsCommand`) because that page has all the real options. Then **remove the dead in-page `ConfigPanel`** (and its related `IsConfigViewOpen` / `ToggleConfig` usage) **only if** removing it does not break bindings. If removing is risky, instead **leave `ConfigPanel` but make sure it is never shown** and add a code comment saying it is unused. Do **not** leave two competing config UIs that both appear.

4. Make sure these three things are obvious to the user on the main screen:
   - A big **Listen / Stop** control (added above).
   - The current **status text** (`AiStatusText`) and **transcription** (`CurrentTranscription` / `LastTranscription`) are visible.
   - Navigation to **Settings**, **Memory**, **Details/Debug** works.

**Acceptance:**
- App launches to the main screen.
- There is one obvious button to start and stop listening; tapping it flips `IsListening` and the button text changes.
- Tapping `CONFIG` opens the full Settings page and Back returns to main.
- No second, half-broken config panel pops up.

---

## Phase 4 — Fix the Panel Crash (SemaphoreFullException)

**Problem:** In `Pages/MainAssistantPage.xaml.cs`, method `TransitionToPanelAsync` (lines ~186–261) does:

```csharp
if (!await _panelTransitionSemaphore.WaitAsync(0).ConfigureAwait(true))
    return;            // did NOT acquire the lock
...
finally
{
    _panelTransitionSemaphore.Release();   // BUG: releases even when not acquired
}
```

If two taps happen quickly, the second call returns early **without acquiring** the semaphore, but the `finally` **still releases** it. Releasing more times than you acquired throws `SemaphoreFullException` and crashes panel switching.

**Fix:** Only release if you actually acquired.

**Steps:**
1. Open `Pages/MainAssistantPage.xaml.cs`, go to `TransitionToPanelAsync`.
2. Change the structure so the `try/finally` is **inside** the success branch:

```csharp
private async Task TransitionToPanelAsync(HudPanelMode mode)
{
    if (!await _panelTransitionSemaphore.WaitAsync(0).ConfigureAwait(true))
        return; // could not get the lock; do nothing, do NOT release

    try
    {
        // ... existing transition/animation code ...
    }
    finally
    {
        _panelTransitionSemaphore.Release();
    }
}
```

**Acceptance:**
- Rapidly tap `DETAILS`, `MEMORY`, `CONFIG` back and forth many times.
- No crash, no `SemaphoreFullException`. Panels still open/close smoothly.

---

## Phase 5 — Make the Settings Screen Actually Change the Runtime

**Problem:** `ViewModels/SettingsViewModel.cs` `SaveSettingsAsync` (lines ~361–396) writes `VoiceAssistantSettings`, `PipelineSettings`, and `McpServers`. It does **not** write the legacy provider settings the runtime reads. So edits don't take effect.

**Fix:** In `SaveSettingsAsync`, after saving the new settings, **also write the legacy settings** based on what the user chose, using the source-of-truth rule from Phase 2.

**Steps:**
1. Open `ViewModels/SettingsViewModel.cs`. Look at the existing helpers `BuildStage(...)` (lines ~399–419) and `SetStageMode(...)`.
2. In `SaveSettingsAsync`, after the existing saves, set the legacy properties on `settingsService`:
   - For **LLM**:
     - If LLM mode = Remote: set `settingsService.LlmSettings` to a config with the remote **endpoint**, **model id**, and **API key** the user typed.
     - If LLM mode = Local/On-device: set `settingsService.LlmSettings` to the local endpoint (`http://localhost:8025/v1`) or the native local manager path.
   - For **STT**:
     - Remote: set `settingsService.SttSettings` (endpoint + model + API key) and set `settingsService.UseOnDeviceStt = false`.
     - On-device: set `settingsService.UseOnDeviceStt = true` and set `settingsService.WhisperModelPath` to the chosen whisper model.
   - For **TTS**:
     - Remote: set `settingsService.TtsSettings` (endpoint + model + API key) and `settingsService.UseLocalTts = false`.
     - On-device: set Android TTS fallback (and `UseLocalTts = true` **only if** Piper is actually installed — see Phase 11).
   - For **VAD**: set `settingsService.VadSettings` from the VAD UI fields (enabled, mode).
3. Make sure the property setters on `SettingsService` actually **persist** (they call the save-to-JSON path). Verify by re-opening Settings after a save and confirming values stick.
4. Confirm `ISettingsService` exposes setters for `LlmSettings`, `SttSettings`, `TtsSettings`, `VadSettings`, `UseOnDeviceStt`, `UseLocalTts`, `WhisperModelPath`. If a setter is missing, add it to `Core/Interfaces/ISettingsService.cs` and implement it in `SettingsService.cs`.

**Acceptance:**
- Change the LLM endpoint in Settings, save, restart the app.
- The runtime (e.g. `LlamaAIClient`) now uses the **new** endpoint (confirm via debug logs / the Details panel health check).
- Same proof for STT and TTS endpoints.

---

## Phase 6 — Make REMOTE Mode Work End to End

**Goal:** With a valid OpenAI-compatible server URL + model + API key in Settings, the assistant should: hear you → transcribe → get an answer → speak it.

**Problem found:** `RemoteSttEngine.cs` and `TtsService.cs` build OpenAI-style requests but **do not attach the API key** stored in `ProviderConfig.Parameters`. So secured servers reject the calls.

**Steps:**

1. **API keys for STT/TTS:**
   - Open `Infrastructure/Services/RemoteSttEngine.cs`. In `TranscribeAsync` (and `IsAvailableAsync` if it calls the server), read the API key from the active provider config (`settings.GetActiveProvider("STT")`, look in its `Parameters` for the key, e.g. `"ApiKey"`) and add header:
     ```csharp
     request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
     ```
     Only add the header if the key is non-empty.
   - Do the same in `Infrastructure/Services/TtsService.cs` `SynthesizeAsync`.
   - `LlamaAIClient.cs` already uses `GetActiveProvider("LLM")` — confirm it also attaches the API key the same way; if not, add it.

2. **Timeouts and error handling (no crashes, no leaking internals):**
   - Make every network call use a sensible timeout (e.g. an `HttpClient.Timeout` or a `CancellationToken` with a timeout). Long hangs make the UI feel frozen.
   - Wrap network I/O in try/catch. On failure, log a clear message via `IDebugLogger` and set a friendly status (e.g. "Server unreachable"). **Do not** show a raw exception to the user.

3. **Remove background dialogs (also see Phase 12):**
   - In `LlamaAIClient.cs`, the call `dialogService.ShowAlertAsync(...)` on an unreachable server can pop a dialog from a **background health check**. Replace it with a **log + status update**, not a popup. Popups should only come from a user action.

4. **Confirm routing picks the remote provider in remote mode:**
   - `ProviderRouter.cs` sorts providers by preferred location. Make sure when settings say "Remote", the remote STT/TTS/LLM providers rank first.

**Acceptance (remote):**
- Enter a working OpenAI-compatible endpoint + key in Settings (use a test server).
- Press Listen, speak a sentence.
- You see a transcription, then an AI reply, then you hear the reply spoken.
- Turning off Wi-Fi shows a friendly "server unreachable" status, **not** a crash or a raw error popup.

---

## Phase 7 — Make ON-DEVICE Mode Work End to End

**Goal:** With on-device selected and models installed, the assistant works **without internet** using native llama (LLM) + whisper (STT) + Android TTS.

**What already exists:**
- Native libs are committed: `libquantumz_llama.so`, `libquantumz_whisper.so` (plus llama/whisper/ggml).
- `WhisperLocalSttProvider.cs` uses native whisper when `UseOnDeviceStt` is true and a model + runtime are available.
- `LlamaLocalManager.cs` runs local llama and resolves a `.gguf` model from app data or a configured path.
- `AndroidTtsEngine.cs` provides built-in TTS.
- `NativeRuntimeService.cs` checks which native libs are packaged.

**Steps:**

1. **STT on device:**
   - Confirm Setup/Settings can download a whisper model (see `ModelCatalogService.cs`, `WhisperModelDownloader.cs`, `LocalSetupService.cs`).
   - After Phase 5, when user picks on-device STT, `UseOnDeviceStt = true` and `WhisperModelPath` points at the downloaded model.
   - Verify `WhisperLocalSttProvider.IsAvailableAsync` returns true only when both the **runtime** (`NativeRuntimeKind` for whisper) and the **model file** exist.

2. **LLM on device:**
   - Confirm a local `.gguf` model can be downloaded/placed (catalog has a provisional Gemma entry but **no verified download URL** — see Phase 11 note).
   - `LlamaLocalManager.ResolveModelPath()` must find the model in `AppData/models/llm` or the configured path.
   - `LlamaAIClient` should fall back to the local manager when remote is not selected/available.

3. **TTS on device:**
   - Use `AndroidTtsEngine` as the default on-device TTS. (Piper is optional — Phase 11.)
   - Note `AndroidTtsEngine.SynthesizeAsync` speaks directly and returns empty bytes; the pipeline in `MicrophonePipelineController.PlayTtsAudioAsync` must handle the "TTS already spoke, no audio bytes" case without trying to play empty audio.

4. **VAD/wake gate:**
   - `RmsVadProvider` (end-of-speech) and `RmsWakeWordProvider` (sustained-speech gate) are the built-in fallbacks. They are **not** real wake-word phrase detection — that is acceptable for v1. Make sure they are selected by default when nothing else is configured.

**Acceptance (on-device):**
- Put device in airplane mode (no internet).
- With whisper + llama models installed and on-device selected, press Listen and speak.
- You get a transcription, a local AI reply, and hear it via Android TTS.
- No empty-audio crash.

---

## Phase 8 — Honor the Tool-Call Iteration Setting + MCP Robustness

**Problem:** `Infrastructure/Services/AIIntegrationService.cs` hardcodes `private const int MaxIterations = 6;` and ignores the user setting `VoiceAssistantSettings.MaxToolCallIterations` (which defaults to 6).

**Steps:**
1. Open `AIIntegrationService.cs`.
2. Replace the constant usage with the setting:
   ```csharp
   int maxIterations = settingsService.VoiceAssistantSettings.MaxToolCallIterations;
   if (maxIterations <= 0) maxIterations = 6; // safety floor
   ```
3. Use `maxIterations` in the tool loop instead of the constant.
4. **MCP safety:** `McpOrchestrator.cs` only does HTTP JSON-RPC (no stdio on Android — that is fine). Make sure tool calls have timeouts and that a failing/unreachable MCP server logs and continues instead of crashing the whole AI turn.

**Acceptance:**
- Set `MaxToolCallIterations` to 2 in Settings, save.
- Trigger a prompt that would use tools; confirm the loop runs at most 2 tool rounds (check debug logs).
- A dead MCP server does not crash the assistant; it just skips tools and still answers.

---

## Phase 9 — Fix (or Remove) the Test Pipeline Button

**Problem:** `MainAssistantViewModel.TestPipelineAsync` sends a `TEST_UTTERANCE`, but `MicrophoneForegroundService.cs` explicitly **does not support it** ("not supported in v2 pipeline") and just returns. The button **says it started** but nothing happens — misleading.

**Decision (recommended): Implement it** so it actually runs a fake utterance through the real pipeline (great for debugging without speaking). If that is too hard, **remove the button** so the UI does not lie.

**If implementing:**
1. In `MicrophonePipelineController.cs`, add an internal method like `RunTestUtteranceAsync(string text)` that **skips audio capture** and feeds `text` straight into the AI pipeline (`RunAiPipelineAsync` equivalent), then plays the TTS result.
2. In `MicrophoneForegroundService.cs`, handle the `TEST_UTTERANCE` intent by calling that method instead of logging "not supported".
3. The text can be a fixed sample like "What time is it?".

**If removing:**
1. Remove the Test button from the UI and `TestPipelineCommand` wiring.

**Acceptance:**
- If implemented: pressing Test produces a real AI reply + spoken output without speaking.
- If removed: there is no button that claims to test but does nothing.

---

## Phase 10 — Fix the Android Foreground Service Lifecycle

**Problem:** In `Platforms/Android/Services/MicrophoneForegroundService.cs` `OnDestroy` (lines ~70–88):
- It calls `_controller?.StopAsync();` (this method is actually **synchronous** despite the name).
- It **fire-and-forgets** `_controller.DisposeAsync().AsTask();` (cleanup may never finish → leaked `AudioRecord`/threads).
- It cancels/disposes `_cts`.

Also `MicrophonePipelineController.cs` uses fire-and-forget tasks for capture and the AI pipeline; unobserved exceptions can crash silently.

**Steps:**
1. In `MicrophoneForegroundService.OnDestroy`, make shutdown deterministic and safe:
   - Cancel the token first (`_cts.Cancel()`).
   - Stop capture (`_controller.StopAsync()`), then **await** disposal properly. Since `OnDestroy` is `void`, block briefly and safely (e.g. `_controller.DisposeAsync().AsTask().GetAwaiter().GetResult()` inside a try/catch with a short timeout) OR move teardown to the `StopAsync` path that runs before the service ends. Do not just fire-and-forget.
   - Dispose `_cts` last, inside try/catch.
2. In `MicrophonePipelineController.cs`, for the fire-and-forget capture/AI tasks, attach a continuation or try/catch that **logs** any exception via `IDebugLogger` so failures are visible, not silent.

**Acceptance:**
- Start listening, then stop / kill the service repeatedly.
- No leaked microphone (mic indicator turns off), no app crash, and any pipeline error is logged.

---

## Phase 11 — Native Packaging, ABI, and the Piper Decision

**Problem A — Piper TTS mismatch.** `Infrastructure/Native/PiperNative.cs` and `LocalAiTtsProvider.cs` expect a native `libquantumz_piper.so`, but:
- `Native/CMakeLists.txt` has **no piper target** (only `quantumz_llama` and `quantumz_whisper`).
- The packaged folder `Platforms/Android/NativeLibs/arm64-v8a/` has **no** `libquantumz_piper.so`.
So local Piper TTS can never load.

**Decision (recommended): Drop Piper for v1.** Use **Android TTS** as the on-device voice. This removes a whole native build burden.
- Make `LocalAiTtsProvider.IsAvailableAsync` return false cleanly (it already requires the runtime; just ensure it never throws when the lib is missing).
- Ensure `AndroidTtsEngine` is the default on-device TTS.
- Leave `PiperNative.cs` in place but unused, with a comment "Piper not shipped in v1".
- (Optional, only if the human asks) Add a `quantumz_piper` target to `Native/CMakeLists.txt`, build it with the Android NDK, and drop the `.so` into the arm64-v8a folder.

**Problem B — Only arm64-v8a is shipped.** `QuantumZ.csproj` packages native libs only from `Platforms/Android/NativeLibs/arm64-v8a/*.so`. This means the app's on-device features only work on **arm64** devices. Most modern phones are arm64, so this is acceptable for v1.
- **Action:** Set `RuntimeIdentifiers`/`AndroidSupportedAbis` to `arm64-v8a` in `QuantumZ.csproj` so the app does not try to run native features on unsupported ABIs. Document this limitation in `README.md`.

**Problem C — Native wrappers ignore params.**
- `quantumz_llama_wrapper.cpp`: fixed `n_ctx=2048`, `n_threads=4`, greedy sampler, ignores `params_json`; output buffer (managed side 8192) can truncate long replies.
- `quantumz_whisper_wrapper.cpp`: ignores `params_json`, `n_threads=4`.
- **For v1:** leave the C++ as-is (it works) but on the managed side **increase the llama output buffer** beyond 8192 if you see truncated answers, and keep prompts within ~2048 context. Only edit C++ if the human asks (requires Android NDK build per Phase 1 note).

**Acceptance:**
- On an arm64 device, on-device LLM + STT load and run.
- The app does not crash looking for Piper; on-device voice uses Android TTS.
- README states arm64-only for on-device features.

---

## Phase 12 — Background Health Checks and Dialog Behavior

**Problem A — Noisy startup.** `MainAssistantViewModel.AttachVisualizerEvents()` kicks off `RefreshHudTelemetryAsync`, `RefreshMemoryTelemetryAsync`, and `RefreshServiceHealthAsync` every time the page appears. This hits the network/services and can make the screen slow or jumpy.

**Problem B — Popups from background.** Health checks / `LlamaAIClient` can show dialogs (`ShowAlertAsync`) from non-user actions.

**Steps:**
1. In `AttachVisualizerEvents`, debounce/throttle the refreshes:
   - Only auto-refresh **once** on first appearance, or at most every N seconds. Do not re-run all three every single appearance.
   - Run them in the background and update the UI when done; never block page navigation.
2. Make all health checks **silent**: they update status text/indicators only. Replace any `ShowAlertAsync` triggered by background checks with logging + status text (already covered in Phase 6.3).
3. Dialogs (`DialogService`) are allowed **only** as a direct result of a user tapping something.

**Acceptance:**
- Opening the main page is fast and does not spam the network.
- No popup appears unless the user pressed a button.

---

## Phase 13 — Make Settings Saving Safe + Secure

**Problem A — Silent data loss.** `SettingsService.LoadComplexFromJson<T>` catches all errors and returns `null`, silently wiping config if JSON is slightly corrupt.

**Problem B — Not crash-safe write.** `SaveComplexToJson<T>` writes a temp file, **deletes the original**, then moves temp. A crash between delete and move loses settings.

**Problem C — Cleartext + broad permissions.** `AndroidManifest.xml` has `usesCleartextTraffic="true"` and broad storage permissions.

**Steps:**
1. In `LoadComplexFromJson<T>`: on parse error, **log** the error via `IDebugLogger` and, if possible, **back up** the bad file (rename to `*.corrupt`) before returning null, so the user/dev knows config was reset.
2. In `SaveComplexToJson<T>`: use an atomic replace. Write temp, then use `File.Replace(temp, target, backup)` (which is atomic on the platform) instead of delete-then-move. If `File.Replace` is unavailable, write temp then `File.Move(temp, target, overwrite: true)` without deleting first.
3. **Security:**
   - Keep `usesCleartextTraffic` only if local `http://localhost` endpoints need it (the local llama server is plain HTTP on localhost). If only localhost needs cleartext, prefer a network security config that allows cleartext **only for localhost**, not all traffic.
   - Trim storage permissions in `AndroidManifest.xml` to the minimum the app actually uses on API 36.
   - API keys live in settings storage only; never logged. Make sure `IDebugLogger` calls never print the API key.

**Acceptance:**
- Corrupting a settings file does not silently erase everything; it is logged and backed up.
- Killing the app mid-save never loses existing settings.
- Cleartext is restricted to localhost (or justified), and no API key appears in logs.

---

## Phase 14 — UI Usability and Responsiveness

**Problem:** Layouts use dense, fixed sizes that may truncate on small screens or with large accessibility fonts.

**Steps (apply to `Pages/*.xaml`):**
1. Wrap long/scrollable content in `ScrollView` so nothing is cut off.
2. Avoid hardcoded heights for text containers; let them grow. Use `*`/`Auto` grid rows.
3. Add `AutomationProperties.Name` to all interactive controls (buttons, switches) for accessibility.
4. Make sure tap targets are at least ~48px high.
5. Confirm the "Cyber Red / Dark" theme has enough contrast for readable text (check `Resources/Styles/Colors.xaml`).
6. Test the main screen, Settings, Setup, Memory, and Debug pages at a large system font size.

**Acceptance:**
- Every page scrolls when content is tall; nothing is clipped.
- Buttons are easy to tap and have accessibility names.
- Text is readable at large font sizes.

---

## Phase 15 — Final Acceptance Test Matrix (Do This Last)

Build a release-quality build and verify **all** of the following on a **real arm64 Android device** (per `AGENTS.md`, device UX verification is mandatory):

### A. First-run / Setup
- [ ] Fresh install opens `SetupPage` (because `SetupSettings.IsCompleted == false`).
- [ ] Choosing **Remote** saves endpoint/model/key and lands on the main screen.
- [ ] Choosing **On-device** shows the model checklist, can install whisper + llama, then lands on main screen.

### B. Navigation
- [ ] Main → Settings (CONFIG) → Back works.
- [ ] Main → Memory → Back works.
- [ ] Main → Details/Debug → Back works.
- [ ] Rapidly toggling panels never crashes (Phase 4).

### C. Remote mode (internet on)
- [ ] Listen → speak → transcription → AI reply → spoken reply.
- [ ] Wrong/secured endpoint shows friendly status, no crash, no raw error popup.
- [ ] Changing the endpoint in Settings actually changes which server is used (Phase 5).

### D. On-device mode (airplane mode)
- [ ] Listen → speak → local transcription (whisper) → local reply (llama) → Android TTS speaks it.
- [ ] No crash when Piper is absent (Phase 11).

### E. Settings correctness
- [ ] Each setting you change is still there after restart.
- [ ] `MaxToolCallIterations` actually limits tool rounds (Phase 8).

### F. Stability
- [ ] Start/stop listening many times — mic indicator turns off after stop (Phase 10).
- [ ] No `SemaphoreFullException`, no unobserved task crashes in logs.

### G. Build quality (`AGENTS.md` quality bar)
- [ ] `dotnet build` = **zero warnings, zero errors**.
- [ ] No hardcoded secrets/endpoints in source.
- [ ] No file exceeds 1,000 lines.

---

## Quick File Reference (where each fix lives)

| Phase | Main files to edit |
|------|--------------------|
| 1 | `.vscode/settings.json` |
| 2 | `Infrastructure/Services/SettingsService.cs` (document source of truth) |
| 3 | `Pages/MainAssistantPage.xaml` (+ `UI/Converters/BoolToListenTextConverter.cs`, `Resources/Styles/Styles.xaml`) |
| 4 | `Pages/MainAssistantPage.xaml.cs` (`TransitionToPanelAsync`) |
| 5 | `ViewModels/SettingsViewModel.cs`, `Core/Interfaces/ISettingsService.cs`, `Infrastructure/Services/SettingsService.cs` |
| 6 | `Infrastructure/Services/RemoteSttEngine.cs`, `TtsService.cs`, `LlamaAIClient.cs`, `ProviderRouter.cs` |
| 7 | `WhisperLocalSttProvider.cs`, `LlamaLocalManager.cs`, `AndroidTtsEngine.cs`, `MicrophonePipelineController.cs` |
| 8 | `Infrastructure/Services/AIIntegrationService.cs`, `McpOrchestrator.cs` |
| 9 | `MicrophonePipelineController.cs`, `MicrophoneForegroundService.cs`, `MainAssistantViewModel.cs` |
| 10 | `Platforms/Android/Services/MicrophoneForegroundService.cs`, `MicrophonePipelineController.cs` |
| 11 | `QuantumZ.csproj`, `Native/CMakeLists.txt`, `LocalAiTtsProvider.cs`, `README.md` |
| 12 | `ViewModels/MainAssistantViewModel.cs`, `LlamaAIClient.cs` |
| 13 | `Infrastructure/Services/SettingsService.cs`, `Platforms/Android/AndroidManifest.xml` |
| 14 | `Pages/*.xaml`, `Resources/Styles/Colors.xaml` |
| 15 | (testing only) |

---

## Notes Carried From the Audit (so you don't redo work)

- The external audit claim that `SetupPage`, `SetupViewModel`, `NativeRuntimeService`, `LocalSetupService`, `ModelCatalogService` are **missing is WRONG** for this workspace — they all exist. Do not recreate them.
- The native `.so` files **are already committed**. You do not need the Android NDK unless you change C++ (Phases 1 & 11).
- The CMake error on startup is **editor noise**, not an app crash (Phase 1).
- `OnDeviceSpeechRecognizer.cs` exists but is **not wired into DI**. Ignore it for v1 (whisper is the on-device STT path) unless the human wants Android's built-in recognizer instead.

---

*Plan version 1.0 — execute phases in order, build after each, keep zero warnings.*
