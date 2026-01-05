# RomanticEngine 🥀

*A UCI Engine that's mostly AI slop*.

RomanticEngine is a small, experimental **UCI-compatible chess engine** written in C#. It’s built to speak the **UCI (Universal Chess Interface)** protocol over stdin/stdout so it can be connected to common chess GUIs (Arena, Cute Chess, En Croissant, etc.).

The project’s primary purpose is to serve as a practical testbed for **AI-assisted development workflows**: which agents perform well on which tasks, what kinds of changes they can safely handle, and what guardrails (tests, contracts, architecture) are needed to keep the codebase stable.

## Features (Current / Intended)

- UCI protocol support (`uci`, `isready`, `setoption`, `position`, `go`, `stop`, `ponderhit`, `quit`, etc.)
- Basic alpha-beta search (iterative deepening / time management may be in progress depending on the branch)
- Defensive parsing and operator-friendly diagnostics (`info string ...`)
- Guardrails aimed at preventing state corruption
- Test harness for scripted UCI sessions (golden output and regression tests)

> The engine is primarily intended for workflow experimentation, not competitive play strength.

## Project Layout

May evolve as experiments continue:

- `RomanticEngine.Core/`
The engine implementation: UCI handling, configuration/options, position/state management, search, evaluation, and testable logic.

- `RomanticEngine/`
Console host that reads stdin and writes stdout. Intended to be a thin wrapper around Core.

- `RomanticEngine.Tests/`
Unit and integration tests, including UCI harness scripts and regression tests.

## Building and Testing

Requires a recent .NET SDK.

```bash
dotnet build
dotnet test
````

## Running

You can run the console host directly:

```bash
dotnet run --project RomanticEngine
```

Then you can type UCI commands manually, or connect the engine binary to a GUI that supports UCI engines.

Example manual session:

```text
uci
isready
position startpos moves e2e4 e7e5
go depth 4
stop
quit
```

## AI Workflow Notes

This repository is intentionally used to track and evaluate different AI-driven coding approaches.

* **IDE for agent-based work:** Antigravity
* **Models used inside Antigravity:** Gemini 3 Pro (High), Gemini 3 Flash
* **Planning/supervision outside Antigravity:** ChatGPT 5.2 Pro
* **Commit message convention:** commits attempt to identify which model is responsible for which change set

The core point is to test:

* how reliably agents follow specs,
* where they tend to take shortcuts,
* what guardrails (tests, architecture boundaries, invariants) prevent regressions,
* and how to structure tasks so the obvious thing for the agent is the correct thing.

## Contributing / Expectations

This repo is experimental by design. If you’re reading the history, expect:

* refactors driven by workflow experiments,
* strict tests added to prevent repeat regressions,
* and occasional “intentionally failing” tests used to enforce correctness contracts.
