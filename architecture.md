# QuantumZ

## Architecture.md

Version: 1.0
Platform: .NET 10 MAUI
Primary Target: Android
Secondary Targets: Windows, Linux, macOS

---

# Vision

TwinFinZ Assistant is a next-generation AI companion platform designed to operate seamlessly across fully offline and cloud-enhanced environments.

The system must:

* Work entirely offline
* Operate locally on Android devices
* Integrate with powerful remote AI servers
* Support dynamic model loading
* Support MCP tools
* Support voice-first interactions
* Remain provider agnostic
* Remain future-proof as models evolve

---

# Core Design Principles

## Offline First

Every core capability must function without internet access.

## Remote Enhanced

When connectivity exists, more capable providers may be used automatically.

## Provider Agnostic

No feature may depend on:

* Specific model names
* Specific model vendors
* Specific inference engines

All interactions occur through capability interfaces.

## Dynamic Discovery

Models and providers are discovered at runtime.

---

# Application Architecture

```text
User
 │
 ▼
Assistant Shell
 │
 ├── UI Engine
 ├── Audio Engine
 ├── Conversation Engine
 ├── Tool Engine
 ├── Memory Engine
 ├── Provider Router
 └── Model Registry

Provider Router
 │
 ├── Local Providers
 └── Remote Providers
```

---

# UI Design System

## Theme

Codename: Crimson Nexus

Inspired By:

* Aerospace HUDs
* Tactical displays
* Cyberpunk control systems
* Sci-fi command centers

---

## Color Palette

Background

#0A0A0A

Surface

#111111

Elevated Surface

#1A1A1A

Primary Accent

#FF003C

Secondary Accent

#FF335F

Success

#00FF88

Warning

#FFB300

Error

#FF3355

Text Primary

#FFFFFF

Text Secondary

#AAAAAA

---

## Visual Elements

* Glassmorphism
* Soft neon glow
* Streaming token animations
* Voice waveform visualizer
* Circular listening indicators
* Animated assistant orb
* Dynamic model status indicators
* GPU/server monitoring panels

---

# Audio Engine

Handles all voice functionality.

---

## Audio Capture

Supports:

* Internal microphone
* Bluetooth audio devices
* Wired headsets

---

## Voice Activity Detection

Interface:

```csharp
IVadProvider
```

Supported Providers:

* Silero VAD
* WebRTC VAD

Default:

Silero VAD

Purpose:

Detect speech start and stop events.

---

## Speech To Text

Interface:

```csharp
ISttProvider
```

Local Providers:

* whisper.cpp

Remote Providers:

* OpenAI Whisper
* Azure Speech
* FasterWhisper APIs
* Custom APIs

Recommended Local Model:

Whisper Small

---

## Text To Speech

Interface:

```csharp
ITtsProvider
```

Built-In Providers:

* Android TTS

Local AI Providers:

* Kokoro
* Piper

Remote Providers:

* ElevenLabs
* OpenAI TTS
* Azure Speech
* Cartesia
* Custom APIs

Selection Order:

1. User Selection
2. Local AI
3. Android TTS
4. Remote Provider

---

# Conversation Engine

Responsible for:

* Prompt construction
* Chat management
* Tool orchestration
* Streaming responses
* Context assembly

Interface:

```csharp
IConversationProvider
```

---

# LLM Engine

Interface:

```csharp
ILlmProvider
```

Supported Engines:

* llama.cpp
* OpenAI-compatible APIs
* Future providers

---

# Mobile Quantization Policy

All local mobile models must default to Q4 quantization.

Preferred:

* Q4_K_M
* Q4_K_S
* IQ4_XS
* IQ4_NL

Disallowed By Default:

* FP16
* BF16
* Q8

Reason:

Provides optimal balance of:

* Quality
* RAM usage
* Battery life
* Thermal performance

---

# Dynamic Model Registry

The application never depends on a specific model.

Instead:

```text
Registry
 │
 ├── VAD Models
 ├── STT Models
 ├── LLM Models
 ├── Embedding Models
 ├── Reranker Models
 ├── TTS Models
 └── Vision Models
```

---

# Supported Local LLM Families

Examples:

* Gemma 4
* Gemma 3
* Qwen 3
* Phi
* Llama
* DeepSeek Distill

The registry dynamically discovers:

* Installed models
* Downloadable models
* Remote models

---

# Model Discovery

Sources:

* Local storage
* llama.cpp registry
* User catalogs
* Remote APIs
* MCP providers

Startup Flow:

```text
Launch
  ↓
Discover Models
  ↓
Benchmark Models
  ↓
Build Capability Graph
  ↓
Ready
```

---

# Provider Router

Central intelligence layer.

Responsibilities:

* Network awareness
* Battery awareness
* Thermal awareness
* Latency monitoring
* Failover management
* Cost management

---

# Example Routing

Offline:

```text
Silero
  ↓
Whisper
  ↓
Gemma 4 E4B Q4
  ↓
Kokoro
```

Online:

```text
Silero
  ↓
Whisper Small
  ↓
Remote Gemma 4 31B
  ↓
Kokoro
```

---

# Tool Engine

Interface:

```csharp
IToolProvider
```

Capabilities:

* Android Intents
* Local Automation
* MCP
* REST APIs
* Device Controls

Examples:

* Send SMS
* Launch App
* Open Navigation
* Query Home Assistant
* Control Smart Devices

---

# MCP Engine

Full Model Context Protocol support.

Supported Transports:

* STDIO
* HTTP
* SSE
* WebSocket

Examples:

* Filesystem
* GitHub
* Home Assistant
* Jellyfin
* Custom Servers

---

# Memory Engine

Stores:

* Conversations
* User Preferences
* Tool Results
* Summaries

Layers:

Short-Term Memory

Current conversation.

Long-Term Memory

Persistent storage.

Vector Memory

Semantic retrieval.

---

# Embedding Engine

Interface:

```csharp
IEmbeddingProvider
```

Examples:

* BGE
* Nomic
* Qwen Embeddings
* Future Providers

Uses:

* Memory retrieval
* RAG
* Search
* Context building

---

# Reranker Engine

Interface:

```csharp
IRerankerProvider
```

Examples:

* BGE Reranker
* Qwen Reranker
* Future Providers

Uses:

* Memory ranking
* Search quality
* Context optimization

---

# Local Storage Layout

```text
AppData/

models/
  llm/

stt/
  whisper/

tts/
  kokoro/
  piper/

vad/
  silero/

embeddings/

rerankers/

memory/

logs/

cache/

mcp/
```

---

# Model Marketplace

Users can:

* Browse models
* Download models
* Update models
* Remove models
* Pin versions

Features:

* SHA verification
* Resume downloads
* Delta updates
* Storage analysis

---

# Model Profiles

Example:

```json
{
  "id": "gemma-4-e4b",
  "quantization": "Q4_K_M",
  "provider": "llama.cpp",
  "supportsToolCalling": true,
  "supportsVision": true,
  "contextLength": 131072,
  "local": true
}
```

---

# Thermal Protection

Monitors:

* Battery temperature
* CPU temperature
* GPU temperature

Automatic Downgrade Path:

```text
31B Remote
    ↓
12B Remote
    ↓
E4B Local
    ↓
Phi Mini
```

---

# Security

No telemetry by default.

Encrypted:

* API keys
* Memory database
* Configuration

Supports:

* Biometrics
* Secure storage
* Certificate validation

---

# Performance Targets

Cold Start

< 2 seconds

Speech Detection

< 100 ms

Wake Response

< 300 ms

First Token

< 500 ms local

Provider Failover

< 1 second

---

# Future Roadmap

Phase 1

* Offline Assistant
* llama.cpp
* whisper.cpp
* Kokoro
* Silero

Phase 2

* MCP
* Memory
* Tool Calling

Phase 3

* Vision
* Screen Understanding
* Workflow Engine

Phase 4

* Multi-Agent Systems
* Desktop Companion
* Wearables

Phase 5

* Distributed AI Mesh
* Vehicle Integration
* Ambient Computing

---

# Final Requirement

Every subsystem must be replaceable.

Models are replaceable.

Providers are replaceable.

Inference engines are replaceable.

The architecture must remain functional regardless of future changes in the AI ecosystem.
