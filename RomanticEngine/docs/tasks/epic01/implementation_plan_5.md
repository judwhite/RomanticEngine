# Implementation Plan

## Spec Alignment and Acceptance Criteria <!-- id: 5 -->

**Problem**: We’re currently implementing “some UCI” and “some search,” but we don’t have a single, enforceable definition of “done” that matches (a) UCI protocol requirements and (b) your BUG_01 expectations (no crash at depth, correct option output formatting, etc.). This covers tasks **6–8**.

**Correctness Conditions**:

1. There is a written acceptance checklist mapping each UCI feature we claim to support → exact required behavior (inputs, outputs, and failure modes).
2. We treat malformed input as non-fatal (no thrown exceptions escaping to the top-level UCI loop).
3. For the **`uci` handshake**, output is stable and exactly formatted (id lines, option lines, `uciok`), so GUI integration doesn’t break.
4. The acceptance checklist includes explicit “BUG_01-class” conditions: deep search must not crash, and any previously observed failure modes have regression tests.

**Proposed Changes**:

* Create a concrete acceptance checklist doc and keep it in-repo (so it’s reviewed with code). <!-- id: 6 -->

  * Include:

    * Required command behaviors (`uci`, `isready`, `setoption`, `position`, `go`, `stop`, `ponderhit`, `quit`, `ucinewgame`).
    * Output requirements (exact line formats; strict where required).
    * Error-handling policy: unknown/malformed commands are ignored or produce `info string …` diagnostics, but never crash.

* Define “operator-friendly” failure defaults explicitly. <!-- id: 7 -->

  * Examples that must be spelled out:

    * If `position` is missing required tokens, ignore it safely.
    * If `setoption` has a bad value, do not throw; optionally emit `info string invalid value for <option>`.
    * If `go` is malformed, do not start search; optionally emit `info string invalid go parameters`.

* Add a *golden* output test for the `uci` handshake that asserts exact lines, ordering, and no extra prefixes. <!-- id: 8 -->

  * This should not use `Contains(...)` except where truly necessary.
  * It must assert:

    * The `id name …` line is exact.
    * The `id author …` line is exact.
    * Each advertised `option` line is exact and appears once.
    * `uciok` appears exactly once at the end.

> [!IMPORTANT]
> Do not “solve” this by loosening tests. The whole point is to stop “half-assing” protocol compliance. Golden tests are intentionally strict.

**Verification Plan**:

* Implement the golden handshake test as a pure unit/integration test that calls the UCI handler with `uci` and captures output in-memory. <!-- id: 8 -->

  * The test harness should capture all lines emitted and compare to a checked-in expected array/list.
  * The harness must not depend on console timing or threads.

---
