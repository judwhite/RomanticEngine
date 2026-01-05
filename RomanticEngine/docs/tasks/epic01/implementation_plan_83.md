# Implementation Plan

## Verification and Regression Suite <!-- id: 83 -->

**Problem**: We need a repeatable, automated way to prove correctness and prevent regressions (especially BUG_01: deep search unmake crash). We also need integration-level confidence for “exactly one bestmove per go,” PV legality, and stable handshake/options output. This section covers tasks **84–90**.

**Correctness Conditions**:

1. We can run scripted UCI sequences in tests and assert output deterministically.
2. Golden-file (or golden-array) tests exist for handshake/options and key protocol flows.
3. BUG_01 is covered by an automated regression test:

   * `go depth 12` must not throw (even if stopped early)
4. Stress/integration scenarios validate the engine survives repeated cycles and remains responsive.
5. Integration tests enforce:

   * exactly one `bestmove` per `go`
   * PV move sequences are legal
6. There is a minimal manual verification runbook for real GUI testing (so humans can validate without guessing steps).

**Proposed Changes**:

* Build a scripted UCI integration harness (in-memory). <!-- id: 84 -->

  * Requirements to spell out (no shortcuts):

    * Host the Core UCI handler + Engine in-process.
    * Capture every output line (thread-safe).
    * Provide helper methods:

      * `Send("command")`
      * `WaitForLine(prefix, timeout)`
      * `WaitForBestMove(timeout)`
      * `DrainOutput()`
    * Do not depend on console streams for tests.

* Add golden-file integration tests for handshake/options and key protocol sequences. <!-- id: 85 -->

  * Golden sequences to include:

    * `uci` handshake
    * `isready`
    * `setoption` success and failure cases (diagnostics)
    * `position startpos moves …` happy path
  * Golden assertions must be strict (line-by-line), not “contains”.

* Add explicit regression test for BUG_01: `go depth 12` does not crash. <!-- id: 86 -->

  * Minimal deterministic test shape:

    1. `position startpos`
    2. `go depth 12`
    3. wait until at least one `info` line is observed (or short bounded time)
    4. `stop`
    5. assert:

       * no `info string search exception` line occurred
       * exactly one `bestmove` occurred
  * This test is about **stability**, not strength. It must be reliable.

* Add stress/integration scenarios for repeated cycles. <!-- id: 87 -->

  * Script loop:

    * `ucinewgame`
    * `position startpos`
    * `go depth 4`
    * wait for `bestmove`
  * Repeat N times.
  * Assert:

    * always completes
    * never emits malformed output
    * never hangs

* Create a manual verification runbook for at least one GUI workflow. <!-- id: 88 -->

  * Include exact steps:

    * how to run engine binary
    * how to configure in Arena/cutechess
    * which commands/positions to test
    * what “good output” looks like (bestmove appears, no crashes)
  * This is for human sanity checks; keep it short and concrete.

* Integration test: exactly one `bestmove` per `go` across scripts. <!-- id: 89 -->

  * Provide scripts with:

    * repeated `go` calls
    * `stop` mid-search
    * `go ponder` then `ponderhit`
  * Assert:

    * for each `go`, exactly one terminal `bestmove` (except ponder suppression rules)

* Integration test: validate legality and formatting of PV moves in `info pv`. <!-- id: 90 -->

  * Parse PV from `info` lines:

    * take the root position used in the session
    * replay PV moves one by one
    * assert each move is legal from that intermediate position
  * Also validate PV formatting:

    * moves must be in UCI move notation (library `Move.ToString()` should match)

**Verification Plan**:

* Implement the UCI harness first; all integration tests should use it. <!-- id: 84 -->
* Golden tests run under CI and locally, producing readable diffs if output changes. <!-- id: 85 -->
* BUG_01 regression test is mandatory and must run on every PR. <!-- id: 86 -->
* Stress tests can be marked as “long-running” if needed, but must still be runnable locally and periodically in CI. <!-- id: 87 -->
* PV legality tests should run at small depths to avoid slowness but still cover special moves over time. <!-- id: 90 -->
