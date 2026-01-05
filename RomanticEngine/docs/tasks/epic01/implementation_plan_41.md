# Implementation Plan

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
