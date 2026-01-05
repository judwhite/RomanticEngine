# Implementation Plan

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
