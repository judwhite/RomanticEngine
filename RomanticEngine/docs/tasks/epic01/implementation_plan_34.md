# Implementation Plan

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
