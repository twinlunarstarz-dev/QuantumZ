cmake_minimum_required(VERSION 3.22)

project(QuantumZNativeWrappers LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT ANDROID)
    message(FATAL_ERROR "QuantumZ native wrappers must be configured with the Android NDK toolchain. Provide -DCMAKE_TOOLCHAIN_FILE=<ndk>/build/cmake/android.toolchain.cmake.")
endif()

if(NOT ANDROID_ABI STREQUAL "arm64-v8a")
    message(FATAL_ERROR "QuantumZ currently packages only Android arm64-v8a native wrappers. Reconfigure with -DANDROID_ABI=arm64-v8a.")
endif()

# Android 16 requires 16 KB-compatible ELF page alignment for packaged shared
# libraries. Apply this before adding upstream subdirectories so wrapper and
# dependency libraries are linked consistently.
add_link_options("LINKER:-z,max-page-size=16384")

set(QZ_REPO_ROOT "${CMAKE_CURRENT_LIST_DIR}/.." CACHE PATH "QuantumZ repository root")
set(QZ_OUTPUT_DIR "${QZ_REPO_ROOT}/Platforms/Android/NativeLibs/arm64-v8a" CACHE PATH "Directory for packaged QuantumZ Android native libraries")
set(QZ_LLAMA_CPP_DIR "${CMAKE_CURRENT_LIST_DIR}/third_party/llama.cpp" CACHE PATH "llama.cpp source checkout")
set(QZ_WHISPER_CPP_DIR "${CMAKE_CURRENT_LIST_DIR}/third_party/whisper.cpp" CACHE PATH "whisper.cpp source checkout")
set(QZ_PREBUILT_ROOT "${CMAKE_CURRENT_LIST_DIR}/third_party/prebuilt" CACHE PATH "Optional prebuilt upstream root")

file(MAKE_DIRECTORY "${QZ_OUTPUT_DIR}")

set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${QZ_OUTPUT_DIR}")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${QZ_OUTPUT_DIR}")

function(qz_require_source_or_prebuilt engine source_dir prebuilt_lib source_header_path prebuilt_header_path)
    if(EXISTS "${source_dir}/CMakeLists.txt" AND EXISTS "${source_header_path}")
        set("QZ_${engine}_MODE" "source" PARENT_SCOPE)
        return()
    endif()

    if(EXISTS "${prebuilt_lib}" AND EXISTS "${prebuilt_header_path}")
        set("QZ_${engine}_MODE" "prebuilt" PARENT_SCOPE)
        return()
    endif()

    message(FATAL_ERROR
        "Missing upstream ${engine} sources or prebuilts. Expected source checkout at '${source_dir}' "
        "or compatible prebuilt library '${prebuilt_lib}' with headers under '${QZ_PREBUILT_ROOT}/include'. "
        "See Native/README.md for clone and build instructions.")
endfunction()

set(QZ_LLAMA_PREBUILT_LIB "${QZ_PREBUILT_ROOT}/android/arm64-v8a/libllama.so")
set(QZ_WHISPER_PREBUILT_LIB "${QZ_PREBUILT_ROOT}/android/arm64-v8a/libwhisper.so")

qz_require_source_or_prebuilt(
    "LLAMA"
    "${QZ_LLAMA_CPP_DIR}"
    "${QZ_LLAMA_PREBUILT_LIB}"
    "${QZ_LLAMA_CPP_DIR}/include/llama.h"
    "${QZ_PREBUILT_ROOT}/include/llama/llama.h")

qz_require_source_or_prebuilt(
    "WHISPER"
    "${QZ_WHISPER_CPP_DIR}"
    "${QZ_WHISPER_PREBUILT_LIB}"
    "${QZ_WHISPER_CPP_DIR}/include/whisper.h"
    "${QZ_PREBUILT_ROOT}/include/whisper/whisper.h")

if(QZ_LLAMA_MODE STREQUAL "source")
    set(LLAMA_BUILD_TESTS OFF CACHE BOOL "" FORCE)
    set(LLAMA_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
    set(LLAMA_BUILD_SERVER OFF CACHE BOOL "" FORCE)
    set(LLAMA_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
    add_subdirectory("${QZ_LLAMA_CPP_DIR}" "${CMAKE_BINARY_DIR}/llama.cpp" EXCLUDE_FROM_ALL)
elseif(QZ_LLAMA_MODE STREQUAL "prebuilt")
    add_library(llama SHARED IMPORTED GLOBAL)
    set_target_properties(llama PROPERTIES IMPORTED_LOCATION "${QZ_LLAMA_PREBUILT_LIB}")
endif()

if(QZ_WHISPER_MODE STREQUAL "source")
    set(WHISPER_BUILD_TESTS OFF CACHE BOOL "" FORCE)
    set(WHISPER_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
    set(WHISPER_BUILD_SERVER OFF CACHE BOOL "" FORCE)
    add_subdirectory("${QZ_WHISPER_CPP_DIR}" "${CMAKE_BINARY_DIR}/whisper.cpp" EXCLUDE_FROM_ALL)
elseif(QZ_WHISPER_MODE STREQUAL "prebuilt")
    add_library(whisper SHARED IMPORTED GLOBAL)
    set_target_properties(whisper PROPERTIES IMPORTED_LOCATION "${QZ_WHISPER_PREBUILT_LIB}")
endif()

add_library(quantumz_llama SHARED quantumz_llama_wrapper.cpp)
add_library(quantumz_whisper SHARED quantumz_whisper_wrapper.cpp)

target_compile_features(quantumz_llama PRIVATE cxx_std_17)
target_compile_features(quantumz_whisper PRIVATE cxx_std_17)

if(QZ_LLAMA_MODE STREQUAL "source")
    target_include_directories(quantumz_llama PRIVATE
        "${QZ_LLAMA_CPP_DIR}/include"
        "${QZ_LLAMA_CPP_DIR}/common")
else()
    target_include_directories(quantumz_llama PRIVATE "${QZ_PREBUILT_ROOT}/include/llama")
endif()

if(QZ_WHISPER_MODE STREQUAL "source")
    target_include_directories(quantumz_whisper PRIVATE
        "${QZ_WHISPER_CPP_DIR}/include")
else()
    target_include_directories(quantumz_whisper PRIVATE "${QZ_PREBUILT_ROOT}/include/whisper")
endif()

target_compile_options(quantumz_llama PRIVATE -Wall -Wextra -Wpedantic)
target_compile_options(quantumz_whisper PRIVATE -Wall -Wextra -Wpedantic)

function(qz_copy_packaged_runtime owner_target dependency_target)
    if(TARGET "${dependency_target}")
        add_custom_command(
            TARGET "${owner_target}"
            POST_BUILD
            COMMAND "${CMAKE_COMMAND}" -E copy_if_different
                "$<TARGET_FILE:${dependency_target}>"
                "${QZ_OUTPUT_DIR}/$<TARGET_FILE_NAME:${dependency_target}>"
            VERBATIM)
    endif()
endfunction()

if(TARGET llama)
    target_link_libraries(quantumz_llama PRIVATE llama)
    qz_copy_packaged_runtime(quantumz_llama llama)
elseif(TARGET llama_shared)
    target_link_libraries(quantumz_llama PRIVATE llama_shared)
    qz_copy_packaged_runtime(quantumz_llama llama_shared)
elseif(TARGET libllama)
    target_link_libraries(quantumz_llama PRIVATE libllama)
    qz_copy_packaged_runtime(quantumz_llama libllama)
else()
    message(FATAL_ERROR "Configured llama.cpp source did not expose a known CMake library target: llama, llama_shared, or libllama.")
endif()

qz_copy_packaged_runtime(quantumz_llama ggml)
qz_copy_packaged_runtime(quantumz_llama ggml-cpu)
qz_copy_packaged_runtime(quantumz_llama ggml-base)

if(TARGET whisper)
    target_link_libraries(quantumz_whisper PRIVATE whisper)
    qz_copy_packaged_runtime(quantumz_whisper whisper)
elseif(TARGET whisper_shared)
    target_link_libraries(quantumz_whisper PRIVATE whisper_shared)
    qz_copy_packaged_runtime(quantumz_whisper whisper_shared)
elseif(TARGET libwhisper)
    target_link_libraries(quantumz_whisper PRIVATE libwhisper)
    qz_copy_packaged_runtime(quantumz_whisper libwhisper)
else()
    message(FATAL_ERROR "Configured whisper.cpp source did not expose a known CMake library target: whisper, whisper_shared, or libwhisper.")
endif()

qz_copy_packaged_runtime(quantumz_whisper ggml)
qz_copy_packaged_runtime(quantumz_whisper ggml-cpu)
qz_copy_packaged_runtime(quantumz_whisper ggml-base)
