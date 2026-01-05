
- [x] Fix `UciAdapter` double info prefix
    - [x] Modify `RomanticEngine.Core/Engine.cs` in `SetPosition` method.
    - [x] Change the error message `info string illegal move...` to `string illegal move...`. `UciAdapter` already adds the `info` prefix.
- [x] Fix `MultiPV` option clamping
    - [x] Modify `RomanticEngine.Core/Engine.cs` in `SetOption` method.
    - [x] Inside the `MultiPV` check, force `spinVal = 1` and update `value = "1"` so the clamped value is applied.
- [x] Fix `PonderHit` timeout bug
    - [x] Modify `RomanticEngine.Core/SearchSession.cs` in `InternalSearchWorker.Start`.
    - [x] After the main search loop, add a check: if `IsPondering()` is true, wait loop until it becomes false or `Token` is cancelled. This ensures `bestmove` isn't sent until PonderHit or Stop.
