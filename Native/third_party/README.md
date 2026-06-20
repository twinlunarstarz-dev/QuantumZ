# Third-party native sources

Place upstream native engines or compatible prebuilt artifacts under this directory before building the QuantumZ Android wrapper libraries.

Expected source checkout layout:

```text
Native/third_party/llama.cpp/
Native/third_party/whisper.cpp/
```

Expected prebuilt fallback layout:

```text
Native/third_party/prebuilt/android/arm64-v8a/libllama.so
Native/third_party/prebuilt/android/arm64-v8a/libwhisper.so
Native/third_party/prebuilt/include/llama/llama.h
Native/third_party/prebuilt/include/whisper/whisper.h
```

Do not commit large generated build outputs or unreviewed binary artifacts. Only build from trusted upstream source or reviewed local prebuilts.
