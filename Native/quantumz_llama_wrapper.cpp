#include <algorithm>
#include <cstddef>
#include <cstring>
#include <exception>
#include <memory>
#include <string>
#include <vector>

#include "llama.h"

namespace
{
thread_local std::string g_last_error;

struct QuantumZLlamaHandle
{
    llama_model* model = nullptr;
    llama_context* context = nullptr;
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

int decode_token(const llama_vocab* vocab, llama_token token, char* buffer, int buffer_size)
{
    auto written = llama_token_to_piece(vocab, token, buffer, buffer_size, 0, true);
    if (written < 0)
    {
        const auto required = -written;
        if (required > buffer_size)
        {
            std::vector<char> dynamic_buffer(static_cast<std::size_t>(required));
            written = llama_token_to_piece(vocab, token, dynamic_buffer.data(), required, 0, true);
            if (written > 0)
            {
                std::memcpy(buffer, dynamic_buffer.data(), static_cast<std::size_t>(std::min(written, buffer_size)));
            }
        }
    }

    return written;
}
}

extern "C"
{
void* qz_llama_create(const char* model_path, const char* params_json)
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
        llama_backend_init();

        auto model_params = llama_model_default_params();
        llama_model* model = llama_model_load_from_file(model_path, model_params);
        if (model == nullptr)
        {
            set_error("llama_model_load_from_file failed.");
            return nullptr;
        }

        auto context_params = llama_context_default_params();
        context_params.n_ctx = 2048;
        context_params.n_threads = 4;
        context_params.n_threads_batch = 4;

        llama_context* context = llama_init_from_model(model, context_params);
        if (context == nullptr)
        {
            llama_model_free(model);
            set_error("llama_init_from_model failed.");
            return nullptr;
        }

        auto* handle = new QuantumZLlamaHandle();
        handle->model = model;
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
        set_error("Unknown exception while creating llama runtime.");
        return nullptr;
    }
}

int qz_llama_infer(void* handle, const char* prompt, int max_tokens, char* output, int output_capacity)
{
    clear_error();

    if (handle == nullptr)
    {
        set_error("Llama handle is null.");
        return -1;
    }

    if (prompt == nullptr)
    {
        set_error("Prompt is null.");
        return -2;
    }

    if (max_tokens <= 0)
    {
        set_error("max_tokens must be positive.");
        return -3;
    }

    try
    {
        auto* state = static_cast<QuantumZLlamaHandle*>(handle);
        const std::string prompt_text(prompt);
        const llama_vocab* vocab = llama_model_get_vocab(state->model);
        const int prompt_capacity = static_cast<int>(prompt_text.size()) + 8;
        std::vector<llama_token> prompt_tokens(static_cast<std::size_t>(prompt_capacity));

        int token_count = llama_tokenize(
            vocab,
            prompt_text.c_str(),
            static_cast<int32_t>(prompt_text.size()),
            prompt_tokens.data(),
            static_cast<int32_t>(prompt_tokens.size()),
            true,
            true);

        if (token_count < 0)
        {
            prompt_tokens.resize(static_cast<std::size_t>(-token_count));
            token_count = llama_tokenize(
                vocab,
                prompt_text.c_str(),
                static_cast<int32_t>(prompt_text.size()),
                prompt_tokens.data(),
                static_cast<int32_t>(prompt_tokens.size()),
                true,
                true);
        }

        if (token_count <= 0)
        {
            set_error("llama_tokenize failed.");
            return -4;
        }

        prompt_tokens.resize(static_cast<std::size_t>(token_count));

        llama_memory_clear(llama_get_memory(state->context), true);

        llama_batch batch = llama_batch_get_one(prompt_tokens.data(), token_count);
        if (llama_decode(state->context, batch) != 0)
        {
            set_error("llama_decode failed while evaluating prompt.");
            return -5;
        }

        std::string generated;
        generated.reserve(static_cast<std::size_t>(std::min(max_tokens, output_capacity)));

        llama_sampler* sampler = llama_sampler_init_greedy();
        if (sampler == nullptr)
        {
            set_error("llama_sampler_init_greedy failed.");
            return -6;
        }

        for (int generated_count = 0; generated_count < max_tokens; ++generated_count)
        {
            llama_token next_token = llama_sampler_sample(sampler, state->context, -1);
            if (llama_vocab_is_eog(vocab, next_token))
            {
                break;
            }

            char piece[256] = {};
            const int written = decode_token(vocab, next_token, piece, static_cast<int>(sizeof(piece)));
            if (written > 0)
            {
                generated.append(piece, static_cast<std::size_t>(std::min(written, static_cast<int>(sizeof(piece)))));
            }

            llama_sampler_accept(sampler, next_token);
            llama_batch next_batch = llama_batch_get_one(&next_token, 1);
            if (llama_decode(state->context, next_batch) != 0)
            {
                llama_sampler_free(sampler);
                set_error("llama_decode failed while generating output.");
                return -7;
            }

            if (generated.size() >= static_cast<std::size_t>(std::max(0, output_capacity - 1)))
            {
                break;
            }
        }

        llama_sampler_free(sampler);

        return copy_output(generated, output, output_capacity);
    }
    catch (const std::exception& ex)
    {
        set_error(ex.what());
        return -8;
    }
    catch (...)
    {
        set_error("Unknown exception during llama inference.");
        return -9;
    }
}

void qz_llama_destroy(void* handle)
{
    if (handle == nullptr)
    {
        return;
    }

    auto* state = static_cast<QuantumZLlamaHandle*>(handle);
    if (state->context != nullptr)
    {
        llama_free(state->context);
    }

    if (state->model != nullptr)
    {
        llama_model_free(state->model);
    }

    delete state;
}

const char* qz_llama_last_error(void)
{
    return g_last_error.empty() ? "" : g_last_error.c_str();
}
}
