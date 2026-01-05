# RomanticEngine Verification Runbook

This document describes how to manually verify RomanticEngine in a GUI environment.

## 1. Building the Engine
Ensure the engine is built for production:
```bash
dotnet publish -c Release -r <your-runtime> --self-contained
```
(e.g., `-r linux-x64` or `-r win-x64`)

The executable will be in `RomanticEngine/bin/Release/net10.0/<runtime>/publish/RomanticEngine`.

## 2. Configuration in cutechess-cli
To verify protocol compliance via CLI:
```bash
cutechess-cli -engine cmd=RomanticEngine -each proto=uci tc=40/40
```
Expect:
- Handshake completes (`uci`, `isready`).
- Engine plays moves.
- No crashes or illegal move reports.

## 3. Configuration in GUI (Arena / Cutechess / Fritz)
1. Add a new UCI engine.
2. Select the `RomanticEngine` executable.
3. Open the "Engine Management" or "Log" window to see the traffic.
4. **Handshake Test**:
   - Verify `id name RomanticEngine 1.0` appears.
   - Verify all options (Threads, Hash, Ponder, MultiPV, etc.) are listed.
5. **Play Test**:
   - Start a new game.
   - Verify the engine makes legal moves.
   - Verify the "Engine Output" window shows monotonic depth, nodes, and a legal PV string.
6. **Time Management Test**:
   - Play a 1-minute blitz game.
   - Verify the engine respects the clock and doesn't run out of time (flag).
7. **Ponder Test**:
   - Enable pondering in the GUI.
   - Verify the engine shows `ponder` in the info line and continues searching on the opponent's turn.

## 4. Stability Check (Regression BUG_01)
1. Set the engine to search unlimited depth or depth 20+.
2. Stop the search manually after a few seconds.
3. Verify no "Search Exception" appears in the log.
4. Verify the engine is ready for another command immediately.

## 5. Debug Logging
1. In the engine options, set `Debug Log File` to a path like `engine_debug.log`.
2. Play a few moves.
3. Open the log file and verify it contains `[IN ]` and `[OUT]` prefixes with timestamps.
