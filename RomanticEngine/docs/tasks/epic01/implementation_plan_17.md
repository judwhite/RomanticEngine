# Implementation Plan

## Search Session Isolation and Cancellation Safety <!-- id: 17 -->

**Problem**: Even with correct make/unmake, we can still corrupt state (and crash) if two searches overlap or if stop/restart races occur. Today the design uses a shared `_stop` flag and fires `Task.Run` without session isolation, which allows the “old search revives when new search sets `_stop=false`” bug and can lead to two searches mutating the same `Position`. We must guarantee: **one active search session at a time**, with **session-local position/state**, and **exactly one `bestmove` per `go`**. This covers tasks **18–24**.

**Correctness Conditions**:

1. Each `go` command creates a distinct search session with its own cancellation mechanism (no shared `_stop` flag across sessions).
2. A new `go` cancels the prior search and prevents further output from the old session (no overlapping mutation; no stale `info`/`bestmove`).
3. `stop` causes the active session to stop “as soon as possible” and still produce exactly one terminal `bestmove`.
4. Search must never mutate the engine’s “main game” position directly; it must operate on a session-local clone/snapshot.
5. Repeated `go/stop` cycles do not leak threads or hang tests.

**Proposed Changes**:

* Introduce a per-`go` `SearchSession` concept with an internal `CancellationTokenSource` and a unique `sessionId`. <!-- id: 18 -->

  * This session owns:

    * its own `IGame` instance (created fresh)
    * its own `PositionDriver` (state stack)
    * its own background Task
    * its own callbacks (`onInfo`, `onBestMove`) that are wrapped/guarded by `sessionId`

* Ensure searches cannot concurrently mutate the same position by design (clone/snapshot). <!-- id: 19 -->

  * Concrete “spell it out” requirement:

    * `Engine` maintains a **main** game/position for `position`/`ucinewgame`.
    * On `go`, `Engine` captures a snapshot of the current main position (typically FEN string) under a lock.
    * The session constructs a brand-new `IGame`, sets it to the snapshot, and searches that.
  * Do **not** attempt to “lock around search” while still mutating the same `IGame`. That’s an easy shortcut that reintroduces subtle races.

* Guarantee exactly one terminal `bestmove` line is emitted per `go`. <!-- id: 20 -->

  * Implement a `bestMoveSent` guard inside the session:

    * If the search completes normally: send bestmove once.
    * If it throws: catch inside session, emit diagnostics, then still send bestmove once.
    * If it is cancelled: still send bestmove once (even if it’s `0000`).
  * Important: the session must not rely on “caller will send bestmove”; the session owns this invariant.

* Ensure new `go` cancels/invalidates the previous session with no revival. <!-- id: 21 -->

  * The “revival” bug happens when:

    * old session checks shared `_stop`
    * new session resets `_stop=false`
  * Concrete requirement:

    * remove shared `_stop` between sessions
    * use cancellation tokens
    * optionally drop outputs from any session whose `sessionId` is not current (defensive).

* Make output callbacks session-safe: prevent stale session output after a new `go`. <!-- id: 21 -->

  * Wrap `onInfo` and `onBestMove` so they check `Engine.CurrentSessionId == sessionId` before emitting.
  * This is a “belt and suspenders” defense: even if cancellation is delayed, stale output is suppressed.

> [!IMPORTANT]
> Do not keep `Search` as a long-lived object that holds `_stop` and `_game` shared across runs.
> That is the exact shape that allows overlapping sessions and deep-search crashes to return.

**Verification Plan**:

* Overlapping `go` calls cancel the prior session and do not corrupt state. <!-- id: 22 -->

  * Test outline:

    1. Set a known position.
    2. Call `go depth 12` (or similar) to ensure it’s “deep enough to matter.”
    3. Immediately call another `go` (different depth or movetime).
    4. Assert:

       * only one final `bestmove` corresponds to the most recent session
       * no exceptions escape
       * engine remains responsive to further commands

* `stop` during deep recursion produces a `bestmove` and does not throw. <!-- id: 23 -->

  * Test outline:

    1. Start a deep search.
    2. Wait until at least one `info` line appears (use a synchronization primitive, not sleeps where possible).
    3. Issue `stop`.
    4. Assert:

       * exactly one `bestmove` is emitted
       * the task terminates
       * no crash

* Stress test repeated `go/stop` cycles for stability (catches hangs/leaks). <!-- id: 24 -->

  * Run a loop (N times):

    * `position startpos`
    * `go depth X`
    * `stop`
  * Assertions:

    * Always receive a `bestmove`
    * Test completes reliably (no deadlocks/hangs)
    * Engine can still run a final `go` after the loop

* Keep these tests deterministic and resistant to timing flakiness. <!-- id: 22 -->

  * Prefer waiting on observed output (`info`/`bestmove`) via events/TaskCompletionSource rather than fixed sleeps.
  * In test failure output, dump captured lines so we can diagnose session ordering issues quickly.

---
