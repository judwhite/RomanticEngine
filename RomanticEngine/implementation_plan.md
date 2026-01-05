# Implementation Plan

## Project Baseline and Build Hygiene <!-- id: 0 -->

**Problem**: The repository builds and tests today, but it’s easy to accidentally introduce silent correctness regressions (nullability mismatch, inconsistent compiler settings, drifting package versions, test harness not stable). We need a reliable “floor” so every subsequent change is verifiable and repeatable. This covers tasks **1–4**.

**Correctness Conditions**:

1. `dotnet build` succeeds for the full solution on a clean checkout without manual steps.
2. `dotnet test` succeeds reliably (no flaky “sometimes fails” tests).
3. Core logic (`RomanticEngine.Core`) and the console app (`RomanticEngine`) maintain clear separation: Core contains UCI/state/search logic; Console contains stdin/stdout plumbing only.
4. Compiler settings (nullable, implicit usings, language version, warnings) are consistent across projects so we don’t “fix” a bug in one project and reintroduce it in another via different rules.

**Proposed Changes**:

* Audit the solution and project boundaries and document the intended structure so we stop drifting. <!-- id: 1 -->

  * Confirm: `RomanticEngine.Core` = engine/search/state/options/UCI adapter; `RomanticEngine` = minimal UCI loop wiring; `RomanticEngine.Tests` = unit/integration tests.
  * Confirm the console app does **not** implement protocol parsing itself (that becomes a core responsibility later; but we at least pin the intent here).

* Pin dependencies and make package resolution deterministic. <!-- id: 2 -->

  * Ensure `Rudzoft.ChessLib` is referenced from the correct project(s) and is pinned to an explicit version.
  * Ensure test project references are explicit.
  * If multiple projects need the same package, prefer a single consistent version (avoid “Core uses X, Tests uses Y”).

* Unify compiler/runtime settings across all projects. <!-- id: 3 -->

  * Turn on nullable reference types consistently (either via `Directory.Build.props` or per-csproj, but *one* consistent approach).
  * Make sure warning behavior is consistent (we should not have Core “warning-clean” but Tests full of warnings that mask real issues).
  * Ensure language version/framework target is consistent and explicit.

> [!IMPORTANT]
> Do **not** “fix” build issues by swallowing exceptions or disabling warnings wholesale. We want problems to surface early, not get buried.

**Verification Plan**:

* Add a minimal “engine can be constructed” smoke test that runs under the test runner, not manually. <!-- id: 4 -->

  * Construct `Engine`.
  * Assert it exposes a non-empty `Options` list.
  * (If construction is currently expensive, keep it as a single test and avoid repeating in every suite.)

---

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

## Move/State Management Safety (MakeMove/TakeMove) <!-- id: 9 -->

**Problem**: The highest-risk correctness area is **state corruption during make/unmake**. Today the code mixes incorrect patterns (e.g., passing `_game.Pos.State` into `MakeMove`) and ambiguous move objects (`ExtMove` vs `Move`), which is exactly how you get “crash at depth 12” and impossible board states. We need a refactor that makes correct usage *the default* and incorrect usage *hard to reintroduce*. This covers tasks **10–16**.

**Correctness Conditions**:

1. **Never** call `MakeMove` with the current `Position.State` object as the `newState` parameter. Each made move must use a distinct “next state” object.
2. Make/Unmake must be strict LIFO: after exploring a move, we must undo that same move before exploring the next sibling.
3. After `MakeMove` then `TakeMove` at a given ply, the position must restore correctly:

   * `Position.State.PositionKey` equals the previous key.
   * (Debug/validation mode) Full identity (e.g., FEN) matches as well.
4. The engine’s “apply moves from UCI history” path (`position … moves …`) uses the same state-handling rules as search.
5. Search recursion passes the correct move type into make/unmake (use `Move`, not `ExtMove`, and do not rely on implicit conversions).

**Proposed Changes**:

* Introduce a single “state stack owner” abstraction (e.g., `PositionDriver`) and mandate all make/unmake flow through it. <!-- id: 10 -->

  * Responsibilities it must own (explicitly):

    * A preallocated `State[]` sized to max ply.
    * A current ply index.
    * `SetStartPos/SetFen` that initializes ply=0 with `states[0]`.
    * `Push(move)` that:

      * calls `pos.MakeMove(move, states[ply+1])`
      * increments ply
      * returns an `IDisposable`/scope that guarantees `TakeMove` happens.
    * `PushPermanent(move)` for applying a permanent move sequence (UCI move history).

* Refactor search recursion to *only* use `PositionDriver.Push(...)` and to pass the correct `Move` type. <!-- id: 11 -->

  * Concretely:

    * Where we iterate `ExtMove[] moves = pos.GenerateMoves()`, we must extract `var m = ext.Move;`.
    * The make/unmake pattern must become:

      * `using var _ = driver.Push(m);`
      * recurse
    * Absolutely no direct `pos.MakeMove(...)` calls remain in search code.

* Refactor `Engine.SetPosition` move application to use the same state-safe mechanism and correct UCI move matching. <!-- id: 12 -->

  * Concrete requirements:

    * Stop using `_game.Pos.MakeMove(move, _game.Pos.State)` entirely.
    * When matching a UCI move string, match against the **move’s UCI representation** (e.g., `ext.Move.ToString()`), not `ext.ToString()` unless we’ve verified it’s identical.
    * If a move cannot be matched as legal in the current position:

      * do **not** make any move
      * emit a diagnostic (later sections will standardize `info string`, but for now it must at least be observable in tests/logs)

* Add debug-time guardrails to catch state corruption at the exact ply it happens. <!-- id: 13 -->

  * In Debug builds, `Push` should capture:

    * `beforeKey = pos.State.PositionKey`
    * optionally `beforeFen = pos.GenerateFen().ToString()`
  * The corresponding pop must assert equality after undo:

    * `afterKey == beforeKey`
    * (optionally) `afterFen == beforeFen`
  * These assertions must be inside the state-stack abstraction so developers cannot “forget” them.

> [!IMPORTANT]
> The anti-pattern that must be eliminated everywhere:
> **`pos.MakeMove(move, pos.State)`**
> That breaks the library’s expected `State.Previous` chain and will eventually explode during undo (often deep in search where it’s hardest to debug).

**Verification Plan**:

* Add a randomized round-trip invariant test that repeatedly makes legal moves and undoes them back to root. <!-- id: 14 -->

  * Steps the test must follow:

    1. Initialize to startpos.
    2. For N iterations:

       * generate legal moves
       * pick one (random or deterministic seeded)
       * `PushPermanent` it (so the line grows)
    3. Undo back to root using the driver’s pop mechanism (or by storing the moves and undoing in reverse via scoped pushes).
    4. Assert root `PositionKey` and identity restored.

* Add special-move invariants explicitly: castling, en-passant, promotion. <!-- id: 15 -->

  * These are the first places state handling mistakes show up.
  * Use curated FENs that force the special move to be legal, then round-trip.

* Add a test suite mirroring the library’s intended usage patterns (similar to your `ZobristHashTest` example). <!-- id: 16 -->

  * The point is to prove we’re using `State` objects the way the library expects:

    * initialize with `states[0]`
    * each ply uses a fresh state object (from the stack)
    * undo restores the original key

---

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

## UCI Parsing Hardening and Single Source of Truth <!-- id: 25 -->

**Problem**: We currently have duplicated UCI parsing logic (console loop vs. core adapter), and the parsing itself is not defensive (it can throw `IndexOutOfRangeException`, `FormatException`, etc.). This is both a stability problem (engine crashes on malformed input) and a correctness problem (different parsers interpret the same command differently). We need **one canonical parser/dispatcher** that never throws on bad input and that handles tricky UCI constructs correctly (`setoption` names/values with spaces, `position fen` with 6 FEN fields, `go searchmoves` consuming the rest of the line). This covers tasks **26–33**.

**Correctness Conditions**:

1. There is exactly **one** code path that parses and dispatches UCI commands in production (no “shadow parser” with different behavior).
2. The UCI handler **never throws** for any input line (including empty lines, whitespace-only lines, unknown commands, incomplete commands).
3. `setoption name … value …` is parsed correctly when **option name and/or value contain spaces**, and option name matching is case-insensitive.
4. `position startpos …` and `position fen …` are parsed correctly:

   * FEN parsing requires exactly 6 FEN fields after `fen`
   * `moves` list is optional and applied in-order
   * illegal/unmatched moves do not corrupt state and do not crash
5. `go` parsing is robust:

   * keys that require a value do not read past the end
   * numeric parsing uses `TryParse` (never `int.Parse`)
   * `searchmoves` consumes the remainder of the command line as move tokens
6. Parsing behavior is consistent across console, tests, and any future host (e.g., a GUI runner).

**Proposed Changes**:

* Route the console input loop through a single core UCI handler, and make the console loop “dumb plumbing.” <!-- id: 26 -->

  * The console app should:

    * read a line
    * pass it verbatim to the core handler (`HandleLine(string line)`)
    * write whatever output lines the handler emits (see task 34 section)
  * The console app must **not** interpret tokens itself (no `switch` on `uci/isready/...` in the console project).

* Retire (or hard-disable) any duplicated parsing paths so there’s no “accidental” split-brain behavior. <!-- id: 27 -->

  * If `UciLoop` and `UciAdapter` both exist today:

    * pick *one* canonical entry point (prefer core)
    * make the other either:

      * a thin wrapper that delegates to the canonical handler, or
      * removed from production wiring
  * Explicitly delete/comment out any TODO that implies “we’ll keep both for now.” That’s how we reintroduce inconsistent behavior.

* Implement a defensive parsing style guideline and apply it consistently across commands. <!-- id: 28 -->

  * Spell-it-out rules (these are non-negotiable):

    * **Never** assume `tokens[1]` exists.
    * **Never** use `int.Parse`, `long.Parse`, etc.
    * **Never** increment an index and immediately index (`tokens[++i]`) unless you’ve verified bounds first.
    * Unknown tokens/commands are ignored safely (optionally emit `info string …` in debug mode; but never crash).
  * Normalize input:

    * Trim leading/trailing whitespace.
    * Treat repeated spaces as separators.
    * Treat command keywords case-insensitively (`UCI`, `uCi`, etc.), but preserve original casing for values.

* Make `setoption` parsing robust by scanning for `name` and `value` markers and reconstructing strings. <!-- id: 29 -->

  * Requirements you must implement explicitly:

    * `setoption name <option name...> value <option value...>`
    * `value` may be missing → treat as empty string for string options, or “no-op” for button options.
    * Option names must be matched case-insensitively.
  * Do not take shortcuts like `string.Join(" ", tokens.Skip(2))` without respecting the boundary between name and value.

* Make `position` parsing robust by correctly handling `startpos` and `fen` forms and the optional `moves` tail. <!-- id: 30 -->

  * `startpos` form:

    * `position startpos` sets base position
    * optional `moves` list applied after
  * `fen` form:

    * `position fen` must be followed by **exactly 6** FEN fields (if fewer, diagnose and ignore)
    * optional `moves` list applied after
  * Applying moves:

    * for each move token:

      * attempt to match it against the current position’s legal moves (by UCI string)
      * if not found:

        * emit a diagnostic (`info string illegal move … at ply …`) and stop applying remaining moves (or ignore remainder), but do not crash

* Make `go` parsing robust and complete for the supported limit keys, including `searchmoves`. <!-- id: 31 -->

  * Implement an explicit loop over tokens with:

    * flag tokens (`infinite`, `ponder`)
    * key/value tokens (`depth N`, `nodes N`, `mate N`, etc.)
    * `searchmoves` token that consumes the remainder of the line as move strings
  * Treat negative or nonsensical values as invalid (ignore and optionally emit diagnostics).
  * Store parse results into a single `SearchLimits` object (no partial “some fields in one place, some in another”).

> [!IMPORTANT]
> The number-one anti-pattern to eliminate here is “index-first parsing”: `tokens[1]`, `tokens[++i]`, `int.Parse(tokens[i])`.
> That is how we got the original crash class of bugs and how we’ll get them again if we allow shortcuts.

**Verification Plan**:

* Add a “malformed commands never throw” test suite that directly feeds lines into the canonical handler and asserts it stays alive. <!-- id: 32 -->

  * Include cases like:

    * empty line / whitespace line
    * `position`
    * `position fen` (missing fields)
    * `go depth` (missing number)
    * `setoption` (missing name/value)
    * random garbage tokens
  * Assertions:

    * no exceptions
    * handler can still process a valid `isready` afterward and returns `readyok`

* Add a `position` correctness test suite for applying moves and diagnosing illegal moves. <!-- id: 33 -->

  * Cases to include explicitly:

    * `position startpos moves e2e4 e7e5 g1f3`
    * `position fen <valid fen> moves …`
    * illegal move in the middle (verify diagnostic + no crash + state is not corrupted past that point)

---

## UCI Output Correctness and Consistency <!-- id: 34 -->

**Problem**: Output formatting is currently inconsistent (notably the “`info info string …`” double-prefix bug) and not rigorously validated. This breaks GUIs, makes logs confusing, and prevents reliable tests. We need a **single, enforced output contract** and helpers that make it hard for anyone to emit malformed UCI lines. This covers tasks **35–40**.

**Correctness Conditions**:

1. Every UCI output line emitted by the engine is syntactically correct and starts with the correct keyword exactly once (`id`, `option`, `uciok`, `readyok`, `info`, `bestmove`, etc.).
2. No output line ever contains a doubled keyword prefix (e.g., `info info …`).
3. `bestmove` output is always correctly formatted:

   * `bestmove <move>` or `bestmove 0000`
   * optional `ponder <move>` appears only as `bestmove <move> ponder <move>` (no other ordering)
4. All error diagnostics use protocol-safe output: `info string <message>`.
5. The output contract is testable in-memory (not tied to `Console.WriteLine`) and used consistently in console + tests.

**Proposed Changes**:

* Define and enforce a single output contract for “info emission” so double-prefixing cannot happen. <!-- id: 35 -->

  * Pick one of these two contracts and enforce it everywhere (do not mix):

    1. **Payload contract**: engine emits info payload (e.g., `string …`, `depth …`) and the UCI layer prefixes with `info `.
    2. **Full-line contract**: engine emits the full `info …` line and the UCI layer prints it verbatim.
  * The key is consistency. Based on past bugs, prefer a design where *only one layer adds the keyword prefixes*.

* Introduce a single UCI line formatter/writer utility and require all output to go through it. <!-- id: 37 -->

  * This utility should provide explicit methods such as:

    * `FormatInfoString(message)`
    * `FormatInfo(searchStats)`
    * `FormatBestMove(best, ponder?)`
    * `FormatOption(optionDef)`
  * Do not let arbitrary code do `Console.WriteLine($"info {something}")` scattered around the codebase.

* Fix `bestmove` formatting, including the null-move case. <!-- id: 36 -->

  * Explicit rule:

    * If there is no legal move (checkmate/stalemate) or the search never produced a candidate move, output **`bestmove 0000`**.
  * Do not rely on library `ToString()` for an empty move unless we’ve verified it outputs `0000` in all cases.

* Ensure error reporting is protocol-safe and does not corrupt output format. <!-- id: 38 -->

  * Requirements:

    * Exceptions in search must be caught inside the session/task boundary.
    * Emit `info string Exception in search: <…>` exactly once (or rate-limited), then still produce a `bestmove`.
  * Do not include the `info` prefix inside the message if the outer layer is already formatting `info …`.

> [!IMPORTANT]
> Do **not** “fix” malformed output by weakening tests or stripping prefixes heuristically.
> The fix is to centralize formatting so malformed output is impossible to generate accidentally.

**Verification Plan**:

* Add strict formatting tests for `info` and `bestmove` output lines. <!-- id: 39 -->

  * For `bestmove`:

    * assert exact match for `bestmove 0000`
    * assert regex/pattern for `bestmove [a-h][1-8][a-h][1-8][qrbn]?` (and optional `ponder …`)
  * For `info`:

    * assert every info line starts with `info `
    * assert it contains only one `info` prefix
    * assert required tokens appear when we claim they will (depth, nodes, time, pv, etc.—as applicable)

* Add explicit regression tests that ensure we never produce “info info …”. <!-- id: 40 -->

  * Feed a command sequence that triggers an exception path (or simulate one via dependency injection/fake search) and assert:

    * output contains `info string …`
    * output does **not** contain `info info`

---

## Complete UCI Search Controls and Semantics <!-- id: 41 -->

**Problem**: We parse some `go` fields today, but we don’t correctly implement key UCI semantics: `go infinite`, `go ponder`/`ponderhit`, `searchmoves`, `nodes`, `mate`, and combined stopping logic. Without these, real GUIs will misbehave (hangs, premature `bestmove`, ignoring constraints). We need correct behavior with defensive defaults and deterministic stop conditions. This covers tasks **42–50**.

**Correctness Conditions**:

1. `go infinite` searches indefinitely and **does not output `bestmove`** until `stop` is received.
2. `go ponder` starts a ponder search and **does not output `bestmove`** until either:

   * `ponderhit` is received (then it transitions to normal search completion), or
   * `stop` is received (then it terminates and outputs `bestmove`)
3. `ponderhit` affects the currently running ponder session only and is safe to call at any time (no crash if not pondering).
4. `searchmoves` restricts root move candidates to exactly the provided legal moves (invalid entries are ignored/diagnosed, but do not crash).
5. `nodes` stops searching once visited node count reaches the limit (and still outputs a `bestmove`).
6. `mate` limit is interpreted consistently:

   * if a mate is found within the requested bound, the engine may stop early
   * output score uses `score mate <N>` semantics (not cp masquerading as mate)
7. When multiple stopping constraints are present (time, depth, nodes, mate), the engine stops when **any** active constraint triggers (unless overridden by `infinite`/ponder semantics).
8. All of this works without races: no extra `bestmove`, no output from stale sessions.

**Proposed Changes**:

* Implement `go infinite` semantics explicitly in the search session’s stop-condition logic. <!-- id: 42 -->

  * Spell-it-out rules:

    * If `limits.Infinite == true`, automatic termination conditions are disabled.
    * The only stop signal is external cancellation (`stop`) (and optionally `quit`).
    * The search loop must still periodically check cancellation and emit `info` updates, but must not emit `bestmove` on its own.

* Implement `go ponder` semantics explicitly, separate from “normal go.” <!-- id: 43 -->

  * Requirements:

    * Ponder sessions must behave like “infinite” sessions *with a special transition*:

      * no `bestmove` while pondering
      * `ponderhit` transitions the session from “ponder mode” to “normal completion mode”
  * Do not treat ponder as a boolean you ignore; it must actually change output behavior.

* Implement `ponderhit` behavior as a state transition on the active session. <!-- id: 44 -->

  * Concrete behavior:

    * If there is no active session: no-op (and do not throw).
    * If there is an active non-ponder session: no-op (and do not throw).
    * If there is an active ponder session:

      * flip a session flag `IsPondering=false`
      * apply the appropriate stop conditions from the original `go` parameters (time controls, depth, etc.)
      * allow the session to eventually emit `bestmove`

* Implement `searchmoves` support end-to-end: parse → store in limits → apply at root. <!-- id: 45 -->

  * Parsing (ties back to task 31): collect all tokens after `searchmoves`.
  * Application:

    * at root only, filter generated legal moves to those whose UCI string matches one of the requested moves
    * if the filter results in an empty set, diagnose and either:

      * search all legal moves (operator-friendly fallback), or
      * immediately return `bestmove 0000` (strict)
    * Choose one behavior and document it; do not silently do something surprising.

* Implement `nodes` stopping: add a node counter that is checked during search recursion. <!-- id: 46 -->

  * Implementation requirement:

    * Node count must be incremented in a single consistent place (e.g., at entry of alpha-beta).
    * The check must be frequent enough to respect limits, but safe (no overflow, no negative).
    * On hitting the node limit, the search must terminate cleanly and still produce a `bestmove`.

* Implement `mate` limit support with explicit mate scoring conventions. <!-- id: 47 -->

  * Spell it out (because this is where shortcuts ruin correctness):

    * If there are no legal moves:

      * if side to move is in check: return a mate score (e.g., `-MATE + ply`)
      * else: return draw/stalemate score
    * Use a consistent mapping from internal mate score to UCI `score mate N`.
    * Only emit `score mate N` when the score actually represents mate; otherwise emit `score cp`.
  * If mate scoring is not implemented yet, do not fake it by outputting `mate` based on large cp scores.

* Implement combined stopping logic in one place (and make it auditable). <!-- id: 48 -->

  * Create a single “should stop?” function that evaluates:

    * cancellation requested
    * time exceeded
    * depth reached
    * nodes reached
    * mate reached (if applicable)
  * Ensure “infinite/ponder” semantics override the normal stop conditions exactly as specified above.
  * Avoid scattering stop checks in five different functions—this is how semantics drift over time.

> [!IMPORTANT]
> Don’t implement “ponder” by simply setting `Infinite=true`.
> Ponder is not just “infinite search”: it’s “infinite **without bestmove** until `ponderhit`”.

**Verification Plan**:

* Add deterministic tests for each supported stop mechanism and for combined logic. <!-- id: 49 -->

  * For `nodes`:

    * use a small nodes limit to stop quickly and deterministically
    * assert bestmove emitted once
  * For `searchmoves`:

    * provide a known set of root moves and assert the chosen bestmove is within the restricted set
  * For `mate`:

    * use a curated FEN where mate-in-1 exists and assert:

      * engine can find it (at shallow depth)
      * score output is `mate 1` (or appropriate sign), not cp

* Add ponder-mode tests that specifically assert the absence of `bestmove` until `ponderhit` or `stop`. <!-- id: 50 -->

  * Test outline:

    1. `go ponder`
    2. wait until at least one `info` arrives (synchronization, not sleeps if possible)
    3. assert no `bestmove` yet
    4. send `ponderhit`
    5. assert a `bestmove` is eventually emitted exactly once
  * Repeat with `stop` instead of `ponderhit` and assert it terminates promptly.

---

## Options and Configuration Correctness <!-- id: 51 -->

**Problem**: We currently advertise options but do not consistently store/apply them (e.g., Hash not persisted), and we risk emitting option lines that don’t match required spec (defaults/min/max). This breaks GUI expectations and makes the engine hard to operate. We need correct option definitions, correct parsing/storage/validation, and predictable output backed by strict tests. This covers tasks **52–60**.

**Correctness Conditions**:

1. The `uci` handshake emits **exactly** the required option lines (name/type/default/min/max), stable ordering, and no duplicates.
2. `setoption` correctly updates configuration for supported options and rejects invalid values safely (no exceptions, no silent corruption).
3. `Threads` and `Hash` have correct validation:

   * `Threads` ∈ [1, maxThreads]
   * `Hash` ∈ [1, maxHashMb]
4. Hash value is persisted in configuration even if actual TT resize logic is not yet implemented.
5. `Clear Hash` (button) triggers a defined action (even if minimal) and is observable (e.g., via `info string cleared hash`).
6. `MultiPV` behavior is not misleading:

   * either implement it, or if not implemented, clearly degrade to 1 while keeping the option stable per spec
7. String/path options treat `<empty>` and empty consistently (normalized and predictable).

**Proposed Changes**:

* Ensure advertised option lines exactly match the spec and are produced from a single source of truth. <!-- id: 52 -->

  * Requirements:

    * Do not build option lines via ad-hoc string concatenation scattered around the code.
    * Use option definition objects (`OptionDefinition`) that include:

      * Name (exact string)
      * Type (`spin`, `string`, `check`, `button`)
      * Default
      * Min/Max (if spin)
    * Emit options in a stable order (choose and lock it down; tests will enforce).

* Implement correct storage and validation for `Threads`. <!-- id: 53 -->

  * Must-haves:

    * Determine `maxThreads` from a single system-info provider (see below).
    * Validate value is an integer and within range.
    * Store the value in configuration immediately on success.
    * If invalid:

      * do not change the current value
      * emit `info string invalid Threads value: <x>` (operator-friendly)

* Implement correct storage and validation for `Hash`. <!-- id: 54 -->

  * Must-haves:

    * Determine `maxHashMb` from a single system-info provider.
    * Validate value is an integer and within range.
    * Store value in configuration immediately.
    * If resize logic is not implemented yet, store anyway and optionally emit:

      * `info string Hash resize deferred (not implemented yet)`
    * Do not leave TODOs that silently ignore the new value.

* Ensure Hash value is persisted even if resize is deferred. <!-- id: 55 -->

  * This is specifically to prevent the “GUI set Hash=1024, engine still uses 16” situation.
  * Persist means:

    * configuration field updates
    * `uci` output reflects the default, not necessarily the last set value (UCI typically shows default; runtime state is separate), but the engine should actually operate with the configured value.

* Implement `Clear Hash` button behavior. <!-- id: 56 -->

  * Define the action clearly:

    * If a transposition table exists: clear it
    * If not: clear any existing search caches/history you have, and still emit `info string cleared hash`
  * Do not implement as a silent no-op.

* Align `MultiPV` option behavior with search reality. <!-- id: 57 -->

  * You have two acceptable paths; pick one explicitly and document it:

    1. **Implement minimal MultiPV now** (more work, but truthful): search top N root lines and emit `info multipv i ... pv ...` for each.
    2. **Degrade gracefully** (simpler, but must be explicit):

       * accept and store the option value
       * if value > 1 and MultiPV isn’t implemented, force effective value to 1 for search
       * emit `info string MultiPV > 1 not implemented; using 1`
  * Do not silently ignore the user’s setting.

* Normalize `<empty>` and empty-string handling for file/path options. <!-- id: 58 -->

  * Concrete rule:

    * If user sets value to `<empty>` or provides no value, store `""` as the canonical “disabled” state.
  * Apply this uniformly to:

    * Debug Log File
    * SyzygyPath
    * any future path options

* Make system-dependent maxima testable via injection (avoid brittle tests). <!-- id: 52 -->

  * Create a small `ISystemInfo` abstraction used only for:

    * `MaxThreads`
    * `MaxHashMb`
  * Production implementation reads from environment/runtime.
  * Tests use a fake with fixed values so golden output is stable.

> [!IMPORTANT]
> Do not write tests that “expect whatever the machine currently reports” for Threads/Hash maxima.
> That produces non-reproducible tests and encourages people to loosen assertions. Inject fixed system info in tests instead.

**Verification Plan**:

* Add `setoption` behavior tests: updates, validation, and graceful rejection. <!-- id: 59 -->

  * For each supported option, assert:

    * valid value updates config
    * invalid value does not update config
    * invalid value emits `info string …` (if we choose diagnostics)
    * no exceptions are thrown

* Add strict golden tests for `uci` option output, including exact option lines. <!-- id: 60 -->

  * Use the fake `ISystemInfo` to force:

    * `Threads max 28`
    * `Hash max 120395`
  * Assert the emitted lines exactly match the expected strings and ordering (no `Contains` shortcuts).
  * Include a regression assertion that `uciok` occurs exactly once and at the end.

---

## Search Algorithm and Reporting Upgrades (post-correctness) <!-- id: 61 -->

**Problem**: Once state safety and session isolation are correct, we still need a search loop that behaves like a real engine: iterative deepening, time management, and consistent `info` reporting that GUIs can display. Today search is closer to “call alpha-beta once,” and time/limits/reporting are incomplete. This section covers tasks **62–68**.

**Correctness Conditions**:

1. Iterative deepening runs depths 1..N (or infinite) and updates the best move progressively.
2. Time management honors `movetime` and time controls (`wtime/btime/inc/movestogo`) with sane, conservative defaults (avoid flagging).
3. Search can stop early due to:

   * token cancellation (`stop`)
   * time budget exceeded
   * node limit
   * mate limit satisfaction
     and still emits exactly one final `bestmove` (unless ponder suppression is active).
4. `info` lines are consistent and reflect real stats:

   * depth increases monotonically
   * nodes/time/nps make sense (non-negative, time in ms)
   * PV moves are legal from the root position
5. MultiPV reporting, if enabled, is internally consistent (`multipv i` for i=1..N).

**Proposed Changes**:

* Implement iterative deepening as the primary search driver. <!-- id: 62 -->

  * “Spell it out” loop structure:

    * Determine max depth (from limits; if infinite, use an unbounded loop until cancelled).
    * For depth = 1..maxDepth:

      * run a depth-limited search
      * update “best so far”
      * emit one (or N) `info` lines for this completed depth
      * check stopping conditions before starting the next depth
  * Do not “fake” iterative deepening by printing “depth” without actually searching that depth.

* Implement time management for real UCI time controls. <!-- id: 63 -->

  * Priority order:

    1. If `movetime` is set: that is the budget (minus a small safety margin).
    2. Else if `wtime/btime` is provided: compute a per-move budget:

       * If `movestogo` is set: budget ≈ remainingTime / movestogo + (increment * small factor)
       * Else: budget ≈ remainingTime / defaultMoves (pick a sane default like 30–40) + (increment * small factor)
       * Apply minimum and maximum clamps (never spend all remaining time).
    3. If no time info is present: fall back to depth or nodes; do not invent time stops.
  * Implementation guidance to avoid shortcuts:

    * Use a `Stopwatch` per session.
    * Check time periodically (e.g., every N nodes) to keep overhead down but remain responsive.

* Improve reporting behavior and PV stability. <!-- id: 64 -->

  * After each completed depth, emit a coherent `info` line that includes:

    * `depth <d>`
    * `multipv <i>` (if MultiPV)
    * `score cp <x>` or `score mate <n>`
    * `nodes <count>`
    * `nps <count>`
    * `time <ms>`
    * `pv <move1> <move2> ...`
  * PV must be derived from actual search results:

    * maintain a PV table (or equivalent) so PV moves are legal and follow from the root.
  * Do not print a PV that contains moves from a different position or illegal moves (GUIs will show nonsense and this is a big “engine feels broken” signal).

* Ensure stats fields are sensible, consistent, and don’t regress. <!-- id: 65 -->

  * Nodes:

    * increment exactly once per node definition (be explicit).
  * NPS:

    * compute as `nodes * 1000 / max(1, elapsedMs)`.
  * Hashfull/tbhits:

    * if TT not implemented yet, output 0 consistently (don’t omit fields if tests/GUI expect them).

**Verification Plan**:

* Unit tests for iterative deepening depth progression. <!-- id: 66 -->

  * Use deterministic stopping (e.g., `depth 4`) and assert:

    * at least one `info depth 1 ...`
    * at least one `info depth 2 ...`
    * …
    * final `bestmove ...` appears exactly once

* Unit tests for time management without flakiness. <!-- id: 67 -->

  * Prefer:

    * node-limited tests for determinism
    * or a time provider abstraction if we want precise control
  * If using real time:

    * add generous margins
    * assert “stops within reasonable bound” rather than exact ms

* Unit tests validating `info` contains required fields across iterations. <!-- id: 68 -->

  * Parse emitted lines and assert each completed depth info line includes:

    * depth, score, nodes, nps, time, pv
  * Validate PV legality by replaying PV moves from the root using the library.

---

## Logging and Operator Friendliness <!-- id: 69 -->

**Problem**: Operators need predictable diagnostics and optional debug logging without breaking UCI output. Currently, exceptions and invalid inputs may be silent or malformed (`info info`), and there’s no consistent debug/log-file behavior. This section covers tasks **70–75**.

**Correctness Conditions**:

1. `debug on/off` is supported (even if logging is minimal) and does not affect protocol correctness.
2. `Debug Log File` option can enable file logging without writing garbage to stdout (stdout must remain protocol-only).
3. Invalid moves/FEN/unsupported tokens yield clear, protocol-safe diagnostics (`info string ...`) and do not crash or corrupt state.
4. All exceptions in background search tasks are caught, diagnosed safely, and still lead to a terminal `bestmove`.
5. Logging is thread-safe and cannot deadlock search or command handling.

**Proposed Changes**:

* Implement `debug` command behavior and toggle state. <!-- id: 70 -->

  * Maintain `IsDebugEnabled` in Core configuration.
  * On `debug on`: set true; emit `info string debug enabled`.
  * On `debug off`: set false; emit `info string debug disabled`.
  * Do not print to stderr/stdout except via `info string` (stdout protocol-safe).

* Implement `Debug Log File` option integration. <!-- id: 71 -->

  * When a non-empty path is set:

    * attempt to open/append a log file (lazy-open is fine)
    * failures must be caught and reported as `info string unable to open debug log file: <message>`
  * When path is empty:

    * close/disable file logging
  * Critical: log writing must never throw out of the logging layer.

* Improve diagnostics for invalid moves / bad FEN / unsupported tokens. <!-- id: 72 -->

  * When `position fen` invalid:

    * do not change current position
    * emit `info string invalid fen: <summary>`
  * When `position … moves …` contains illegal move:

    * stop applying moves
    * emit `info string illegal move in history: <move>`
  * When `setoption` invalid:

    * emit `info string invalid value for <option>: <value>`
  * Keep diagnostics short and single-line (don’t dump multiline stack traces to stdout).

* Ensure all exceptions are caught and surfaced without breaking UCI output. <!-- id: 73 -->

  * Catch exceptions at:

    * command handling boundary (so malformed input doesn’t kill the loop)
    * search session task boundary (so search exceptions don’t become unobserved task exceptions)
  * Emit `info string ...` diagnostic (payload-only; no double prefix)
  * Always follow with a valid terminal `bestmove` (or suppress only if in ponder before hit/stop).

**Verification Plan**:

* Unit tests verifying debug/log options don’t affect protocol output or formatting. <!-- id: 74 -->

  * Script:

    * `debug on`
    * set debug log file
    * `uci`, `isready`, etc.
  * Assertions:

    * stdout contains valid UCI lines only
    * no extra noise appears

* Unit tests verifying invalid inputs produce `info string` diagnostics and engine continues. <!-- id: 75 -->

  * Feed invalid fen, invalid moves, invalid setoption values.
  * Assert:

    * diagnostic lines exist
    * engine still answers `isready` correctly
    * subsequent valid `position` and `go` still work

---

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
