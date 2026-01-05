# Implementation Plan

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
