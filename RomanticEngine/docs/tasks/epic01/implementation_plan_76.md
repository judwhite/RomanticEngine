# Implementation Plan

## Code Cleanup, Consistency, and Guardrails <!-- id: 76 -->

**Problem**: Even after functional fixes, we need to eliminate confusing/dead code and enforce patterns that prevent regressions (especially bypassing the state-stack abstraction and reintroducing parsing duplication). We also need to reduce test flakiness and make APIs consistent. This section covers tasks **77–82**.

**Correctness Conditions**:

1. There is a single source of truth for evaluation/config values (no unused static knobs drifting).
2. Engine events are consistent and subscribed through the interface surface (no concrete-type checks).
3. Naming and ownership boundaries are consistent (Core owns protocol logic; Console does I/O only).
4. Tests are deterministic and avoid timing sleeps where possible.
5. There are guardrails that make it difficult to reintroduce:

   * direct `pos.MakeMove` usage in search outside the state-stack owner
   * duplicated UCI parsing
6. A basic end-to-end command sequence works reliably under tests (no GUI required).

**Proposed Changes**:

* Remove dead/unused config/evaluation fields and unify configuration sources. <!-- id: 77 -->

  * Delete unused statics (e.g., evaluation weights that are not referenced).
  * Ensure evaluation reads from one config object (and that config is updated by `setoption` where intended).

* Fix event subscriptions to rely on interfaces (no `if (_engine is Engine concreteEngine)`). <!-- id: 78 -->

  * Make event nullability consistent between interface and implementation.
  * Subscribe using `_engine.OnInfo += ...` directly.
  * Ensure event invocation is safe (no null ref; standard event pattern).

* Standardize naming and simplify constructs across Core/Console boundary. <!-- id: 79 -->

  * Ensure there’s exactly one “UCI handler” type name in Core and one “console loop” type.
  * Remove “TODO: should be using adapter” by actually doing it (no TODO debt left).

* Reduce test flakiness with deterministic stopping and synchronization. <!-- id: 80 -->

  * Prefer `nodes`/`depth` limits and explicit `TaskCompletionSource` waiting for outputs rather than `Thread.Sleep`.
  * Where time-based tests are necessary, use generous tolerances and avoid asserting exact timings.

* Add an end-to-end unit/integration test for a minimal command sequence. <!-- id: 81 -->

  * Script:

    * `uci`
    * `isready`
    * `position startpos`
    * `go depth 2`
  * Assert:

    * handshake correct
    * `readyok` occurs
    * exactly one `bestmove` occurs
    * no malformed lines

* Add architecture safety guardrails to prevent bypassing the state-stack abstraction. <!-- id: 82 -->

  * Strongest guardrail (preferred):

    * introduce wrappers/interfaces so general search code cannot access raw `IPosition.MakeMove/TakeMove` directly
    * only the `PositionDriver` (or equivalent) sees the underlying library position
  * Additional guardrail (pragmatic):

    * a unit test that scans source files to ensure `MakeMove(` / `TakeMove(` appears only in approved files (PositionDriver and maybe position setup code)
    * not pretty, but effective at stopping “quick hack” regressions.

> [!IMPORTANT]
> Do not leave “temporary” duplicate code paths or TODO notes around core correctness areas.
> That’s exactly how the next model will take a shortcut and reintroduce the bug.

**Verification Plan**:

* End-to-end command sequence test (Core handler + captured output). <!-- id: 81 -->

  * Must run without console and without sleeps.

* Architecture guardrail test to prevent “direct MakeMove” usage outside approved scope. <!-- id: 82 -->

  * Fail loudly if new direct calls appear.

---
