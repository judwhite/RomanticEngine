# UCI Protocol Acceptance Checklist

This document defines the strict correctness criteria for the RomanticEngine's UCI protocol implementation.

## Command Behaviors

| Command | Expected Behavior |
| --- | --- |
| `uci` | Engine must respond with `id name`, `id author`, all available `option` lines, and finally `uciok`. |
| `isready` | Engine must respond with `readyok`. If a search is running, it must respond immediately without stopping the search. |
| `setoption` | Engine updates internal configuration. Must handle spaces in names/values. Must be case-insensitive for names. |
| `ucinewgame` | Engine resets game-specific state (hash, history). Usually followed by `isready`. |
| `position` | Sets up the board via `startpos` or `fen`. Applies optional `moves`. Illegal moves should be diagnosed (info string) but not crash the engine. |
| `go` | Starts search. Must handle various limits (depth, nodes, movetime, etc.). Must support `infinite` and `ponder`. |
| `stop` | Stops the current search and forces the output of `bestmove`. |
| `ponderhit` | Transitions a ponder search to a normal search. |
| `quit` | Engine must exit cleanly. |

## Output Requirements

1. **Syntactic Correctness**: Every line MUST start with a valid UCI keyword (`id`, `option`, `uciok`, `readyok`, `info`, `bestmove`).
2. **No Double Prefixing**: Output MUST NOT contain doubled prefixes like `info info string`.
3. **Bestmove Format**:
    - `bestmove <move>`
    - `bestmove <move> ponder <move>`
    - `bestmove 0000` (if no legal move exists or search was aborted before finding one).
4. **Info Fields**: `info` lines should contain `depth`, `multipv`, `score`, `nodes`, `nps`, `hashfull`, `tbhits`, `time`, `pv` where applicable.
5. **Diagnostics**: Non-protocol messages must be prefixed with `info string `.

## Error Handling Policy

- **Malformed Commands**: Ignored safely. Do not throw exceptions.
- **Missing Parameters**: If `position` is sent without `startpos` or `fen`, ignore it.
- **Bad Values**: If `setoption` receives an out-of-range value, ignore it and optionally emit `info string invalid value`.
- **Search Exceptions**: Caught within the session, reported via `info string`, and engine must still output a `bestmove` to maintain protocol flow.

## BUG_01 Regression Conditions

- `go depth 12` (and higher) must not cause `IndexOutOfRangeException` or any state corruption.
- All `info` lines must contain the full set of required fields (`cp`, `nodes`, `nps`, `hashfull`, `tbhits`, `time`, `pv`).
- Option `Debug Log File` default is `<empty>`.
- Option `Threads` max is `28`.
- Option `Hash` max is `120395`.
- Option `MultiPV` max is `256`.
