# AGENTS.md - Development Guidelines for Project QuantumZ

This document serves as the primary set of constraints and guidelines for all AI agents contributing to the QuantumZ codebase. Adherence to these rules is mandatory to ensure maintainability, security, and architectural integrity.

## 1. Architectural North Star
QuantumZ follows a modular design to separate concerns and prevent monolithic growth:
- **`QuantumZ.Core`**: Domain models, interfaces, and core business logic. No dependencies on other modules.
- **`QuantumZ.Infrastructure`**: Implementation of external services (llama.cpp REST clients, MCP Client), persistence (SQLite), and low-level utilities.
- **`QuantumZ.Android`**: Platform-specific implementations, including the Android Foreground Service (`microphone` type) and `AudioManager` routing logic.
- **`QuantumZ.UI`**: MAUI Pages, ViewModels (MVVM), and styling/theming ("Cyber Red / Dark").

**Dependency Rule:** Dependencies must flow inward: `UI` $\rightarrow$ `Infrastructure`/`Core`, `Infrastructure` $\rightarrow$ `Core`, `Android` $\rightarrow$ `Core`. Circular references are strictly forbidden.

## 2. Coding Standards (.NET 10)
All code must leverage the latest .NET 10 features to reduce boilerplate and improve performance:
- **Primary Constructors**: Use primary constructors for dependency injection in services and ViewModels.
- **Collection Expressions**: Use `[]` syntax for initializing arrays, lists, and spans.
- **Required Members**: Utilize the `required` keyword for properties that must be initialized during object creation.
- **Asynchronous Patterns**: Prefer `ValueTask` over `Task` for high-frequency background operations to minimize heap allocations.

## 3. File & Complexity Constraints
To maintain readability and facilitate easier AI context windows:
- **Line Limit**: No single file shall exceed **1,000 lines**. If a class or page grows beyond this limit, it must be refactored into smaller, focused components (e.g., using Partial Classes or splitting services).
- **Single Responsibility**: Each class should have one clear reason to change.

## 4. Quality Bar & Validation
A task is not considered "complete" until the following criteria are met:
- **Zero Warnings/Errors**: The project must compile with zero build warnings and zero errors (`TreatWarningsAsErrors` enabled).
- **UX Verification**: For any UI or platform-specific change, validation using `native-devtools-mcp` on a real Android device is mandatory to ensure accessibility and visual correctness.

## 5. Security & Professionalism
- **API Handling**: All calls to the llama.cpp REST API must be handled securely. Do not hardcode sensitive endpoints in source code; use configuration providers or environment variables.
- **Error Handling**: Implement robust try-catch blocks around network I/O and platform APIs, providing meaningful logs without leaking system internals to the UI.
- **Code Structure**: Maintain professional naming conventions (PascalCase for methods/classes) and provide XML documentation for public interfaces in `QuantumZ.Core`.

## 6. Knowledge Acquisition Protocol
Agents must not assume framework behavior or API signatures based on outdated training data:
1. **Obsidian First**: Query the project's Obsidian vault via MCP to find prior decisions, documented behaviors, or existing patterns.
2. **Official Docs**: Verify .NET 10 and Android API 36 specifications through official documentation if Obsidian does not provide a definitive answer.
3. **Evidence-Based Implementation**: Base all changes on verified evidence (logs, execution output, or current codebase state).

---
*Last Updated: 2026-06-11*
