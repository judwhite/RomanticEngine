# Implementation Plan

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
