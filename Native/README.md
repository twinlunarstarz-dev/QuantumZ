# QuantumZ Android native wrappers

QuantumZ uses app-owned wrapper libraries for Android on-device AI instead of executing downloaded command-line binaries. The managed bindings expect these Android `arm64-v8a` shared libraries:

- `Platforms/Android/NativeLibs/arm64-v8a/libquantumz_llama.so`
- `Platforms/Android/NativeLibs/arm64-v8a/libquantumz_whisper.so`

These wrappers expose the stable QuantumZ C ABI used by `Infrastructure/Native/LlamaNative.cs` and `Infrastructure/Native/WhisperNative.cs` while allowing upstream `llama.cpp` and `whisper.cpp` internals to evolve independently.

## Required upstream sources

This repository does not vendor upstream native inference engines by default. To build the wrappers from source, place shallow checkouts here:

```text
Native/third_party/llama.cpp/
Native/third_party/whisper.cpp/
```

Recommended source checkout commands from the repository root:

```powershell
git clone --depth 1 https://github.com/ggml-org/llama.cpp.git Native/third_party/llama.cpp
git clone --depth 1 https://github.com/ggml-org/whisper.cpp.git Native/third_party/whisper.cpp
```

Alternatively, provide compatible prebuilt upstream Android `arm64-v8a` libraries and headers:

```text
Native/third_party/prebuilt/android/arm64-v8a/libllama.so
Native/third_party/prebuilt/android/arm64-v8a/libwhisper.so
Native/third_party/prebuilt/include/llama/llama.h
Native/third_party/prebuilt/include/whisper/whisper.h
```

The CMake configuration prefers source checkouts when present. Prebuilt upstream libraries are accepted only when matching headers are also present.

## Android arm64-v8a build command

Install the Android SDK, Android NDK, and CMake. Then run from the repository root, replacing the NDK path with the installed version on the machine:

```powershell
cmake -S Native -B Native/build/android-arm64 -G Ninja `
  -DCMAKE_TOOLCHAIN_FILE="C:/Users/yelle/AppData/Local/Android/Sdk/ndk/<version>/build/cmake/android.toolchain.cmake" `
  -DANDROID_ABI=arm64-v8a `
  -DANDROID_PLATFORM=android-26 `
  -DCMAKE_BUILD_TYPE=Release `
  -DQZ_OUTPUT_DIR="$PWD/Platforms/Android/NativeLibs/arm64-v8a"

cmake --build Native/build/android-arm64 --config Release
```

If Ninja is unavailable, omit `-G Ninja` and let CMake select an installed generator.

## Output target

The wrapper build writes the QuantumZ shared libraries to:

```text
Platforms/Android/NativeLibs/arm64-v8a/
```

The MAUI project packages `*.so` files in that directory as Android native libraries when the directory exists. If the upstream sources or Android NDK are missing, the managed app can still build, but true on-device LLM/STT setup must remain blocked until these wrapper libraries are produced and packaged.
