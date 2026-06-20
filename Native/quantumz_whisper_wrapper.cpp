#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <exception>
#include <string>
#include <vector>

#include "whisper.h"

namespace
{
thread_local std::string g_last_error;

struct QuantumZWhisperHandle
{
    whisper_context* context = nullptr;
};

void set_error(const std::string& message)
{
    g_last_error = message;
}

void clear_error()
{
    g_last_error.clear();
}

int copy_output(const std::string& text, char* output, int output_capacity)
{
    if (output == nullptr || output_capacity <= 0)
    {
        set_error("Output buffer is null or has non-positive capacity.");
        return -1;
    }

    const auto writable = static_cast<std::size_t>(output_capacity - 1);
    const auto bytes_to_copy = std::min(writable, text.size());
    if (bytes_to_copy > 0)
    {
        std::memcpy(output, text.data(), bytes_to_copy);
    }

    output[bytes_to_copy] = '\0';
    return static_cast<int>(bytes_to_copy);
}

std::vector<float> convert_pcm16_to_float(const short* pcm16, int sample_count)
{
    std::vector<float> samples(static_cast<std::size_t>(sample_count));
    for (int i = 0; i < sample_count; ++i)
    {
        samples[static_cast<std::size_t>(i)] = static_cast<float>(pcm16[i]) / 32768.0F;
    }

    return samples;
}
}

extern "C"
{
void* qz_whisper_create(const char* model_path, const char* params_json)
{
    (void)params_json;
    clear_error();

    if (model_path == nullptr || model_path[0] == '\0')
    {
        set_error("Model path is required.");
        return nullptr;
    }

    try
    {
        whisper_context_params context_params = whisper_context_default_params();
        whisper_context* context = whisper_init_from_file_with_params(model_path, context_params);
        if (context == nullptr)
        {
            set_error("whisper_init_from_file_with_params failed.");
            return nullptr;
        }

        auto* handle = new QuantumZWhisperHandle();
        handle->context = context;
        return handle;
    }
    catch (const std::exception& ex)
    {
        set_error(ex.what());
        return nullptr;
    }
    catch (...)
    {
        set_error("Unknown exception while creating Whisper runtime.");
        return nullptr;
    }
}

int qz_whisper_transcribe_pcm16(
    void* handle,
    const short* pcm16,
    int sample_count,
    int sample_rate,
    char* output,
    int output_capacity)
{
    clear_error();

    if (handle == nullptr)
    {
        set_error("Whisper handle is null.");
        return -1;
    }

    if (pcm16 == nullptr)
    {
        set_error("PCM16 input is null.");
        return -2;
    }
    if (sample_count <= 0)
    {
        set_error("sample_count must be positive.");
        return -3;
    }
    if (sample_rate <= 0)
    {
        set_error("sample_rate must be positive.");
        return -4;
    }

    try
    {
        auto* state = static_cast<QuantumZWhisperHandle*>(handle);
        std::vector<float> samples = convert_pcm16_to_float(pcm16, sample_count);

        if (sample_rate != WHISPER_SAMPLE_RATE)
        {
            set_error("Whisper wrapper expects 16 kHz PCM input. Resample before calling qz_whisper_transcribe_pcm16.");
            return -5;
        }

        whisper_full_params params = whisper_full_default_params(WHISPER_SAMPLING_GREEDY);
        params.print_realtime = false;
        params.print_progress = false;
        params.print_timestamps = false;
        params.print_special = false;
        params.translate = false;
        params.language = "en";
        params.n_threads = 4;

        const int result = whisper_full(state->context, params, samples.data(), sample_count);
        if (result != 0)
        {
            set_error("whisper_full failed.");
            return -6;
        }

        std::string transcript;
        const int segment_count = whisper_full_n_segments(state->context);
        for (int i = 0; i < segment_count; ++i)
        {
            const char* segment_text = whisper_full_get_segment_text(state->context, i);
            if (segment_text != nullptr)
            {
                transcript.append(segment_text);
            }
        }

        return copy_output(transcript, output, output_capacity);
    }
    catch (const std::exception& ex)
    {
        set_error(ex.what());
        return -7;
    }
    catch (...)
    {
        set_error("Unknown exception during Whisper transcription.");
        return -8;
    }
}

void qz_whisper_destroy(void* handle)
{
    if (handle == nullptr)
    {
        return;
    }

    auto* state = static_cast<QuantumZWhisperHandle*>(handle);
    if (state->context != nullptr)
    {
        whisper_free(state->context);
    }

    delete state;
}

const char* qz_whisper_last_error(void)
{
    return g_last_error.empty() ? "" : g_last_error.c_str();
}
}
