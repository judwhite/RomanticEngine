# Implementation Plan

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
