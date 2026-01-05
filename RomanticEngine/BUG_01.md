# Title: `go depth 12` causes `IndexOutOfRangeException`

## Description

* `go depth 12` causes `IndexOutOfRangeException`.
* The `info` lines returned by `go` do not contain complete information.
* Additionally, many UCI options need their `default` or `max` value corrected.

### UCI Option Issues:

* UCI Option `Debug Log File` should have a default value of `<empty>`.
* UCI Option `Threads` should have a max value of `28` for this machine because it has 28 cores.
* UCI Option `Hash` should have a max value of `120395` for this machine because it has this many megabytes of RAM.
* UCI Option `MultiPV` should have a max value of `256` for all machines.

### `go depth 12` should not cause `IndexOutOfRangeException`

See logs below.

## Logs

In the "Expected results" and "Actual results" sections, the `>` character denotes a command sent to the engine. Without this character, the line is output from the engine.

Note that the values for `cp`, `nodes`, `nps`, `hashfull`, `tbhits`, `time`, and `pv` will vary, but these fields MUST be present in the `info` lines returned by `go`.

Expected results:
```
> uci
option name Debug Log File type string default <empty>
option name Threads type spin default 1 min 1 max 28
option name Hash type spin default 16 min 1 max 120395
option name Clear Hash type button
option name Ponder type check default false
option name MultiPV type spin default 1 min 1 max 256
option name Move Overhead type spin default 10 min 0 max 5000
option name SyzygyPath type string default <empty>
option name EnableMaterial type check default true
option name EnableRMobility type check default true
option name EnableKingSafety type check default true
option name MaterialWeight type spin default 1 min 0 max 100
option name MobilityWeight type spin default 10 min 0 max 100
option name KingSafetyWeight type spin default 20 min 0 max 100
uciok
> ucinewgame
> position startpos
> isready
readyok
> go depth 12
info depth 1 multipv 1 score cp -1 nodes 20 nps 20000 hashfull 0 tbhits 0 time 1 pv e2e4
info depth 2 multipv 1 score cp 27 nodes 48 nps 48000 hashfull 0 tbhits 0 time 1 pv e2e4
info depth 3 multipv 1 score cp 32 nodes 160 nps 160000 hashfull 0 tbhits 0 time 1 pv e2e4
info depth 4 multipv 1 score cp 47 nodes 290 nps 145000 hashfull 0 tbhits 0 time 2 pv e2e4
info depth 5 multipv 1 score cp 66 nodes 620 nps 206666 hashfull 0 tbhits 0 time 3 pv e2e4 g8f6
info depth 6 multipv 1 score cp 31 nodes 2460 nps 351428 hashfull 0 tbhits 0 time 7 pv e2e4 c7c5 c2c3 b8c6 g1f3 e7e5 d2d4 e5d4 c3d4 c5d4 f3d4
info depth 7 multipv 1 score cp 40 nodes 3540 nps 393333 hashfull 1 tbhits 0 time 9 pv e2e4 c7c5 g1f3 b8c6 f1b5
info depth 8 multipv 1 score cp 42 nodes 5112 nps 426000 hashfull 1 tbhits 0 time 12 pv e2e4 e7e5 g1f3 g8f6 f3e5 f6e4 f1c4
info depth 9 multipv 1 score cp 50 nodes 8699 nps 457842 hashfull 1 tbhits 0 time 19 pv e2e4 e7e5 g1f3 g8f6 b1c3 b8c6 f1b5
info depth 10 multipv 1 score cp 49 nodes 18512 nps 474666 hashfull 5 tbhits 0 time 39 pv e2e4 e7e5 g1f3 g8f6 d2d4 e5d4 e4e5 f6e4 d1d4 d7d5 e5d6 e4d6 f1d3
info depth 11 multipv 1 score cp 41 nodes 37458 nps 506189 hashfull 12 tbhits 0 time 74 pv e2e4 e7e5 g1f3 b8c6 b1c3 g8f6 f1b5 f8b4 c3d5 f6d5 e4d5 c6d4 f3d4 e5d4 e1g1
info depth 12 multipv 1 score cp 49 nodes 54253 nps 531892 hashfull 21 tbhits 0 time 102 pv e2e4 c7c5 g1f3 a7a6 d2d4 c5d4 f3d4
bestmove e2e4 ponder c7c5
```

Actual results:
```
> uci
id name RomanticEngine 1.0
id author Jud White
option name Debug Log File type string default 
option name Threads type spin default 1 min 1 max 1024
option name Hash type spin default 16 min 1 max 1024
option name Clear Hash type button
option name Ponder type check default false
option name MultiPV type spin default 1 min 1 max 500
option name Move Overhead type spin default 10 min 0 max 5000
option name SyzygyPath type string default <empty>
option name EnableMaterial type check default true
option name EnableRMobility type check default true
option name EnableKingSafety type check default true
option name MaterialWeight type spin default 1 min 0 max 100
option name MobilityWeight type spin default 10 min 0 max 100
option name KingSafetyWeight type spin default 20 min 0 max 100
uciok
> ucinewgame
> position startpos
> isready
readyok
> go depth 12
info depth 1 score cp -1010 nodes 21 time 13 nps 1615 pv a2a3
info depth 2 score cp 1050 nodes 129 time 17 nps 7588 pv b1c3
info depth 3 score cp -1030 nodes 1341 time 28 nps 47892 pv d2d3
info info string Exception in search: System.IndexOutOfRangeException: Index was outside the bounds of the array.
   at Rudzoft.ChessLib.Board.AddPiece(Piece pc, Square sq)
   at Rudzoft.ChessLib.Position.TakeMove(Move m)
   at RomanticEngine.Core.Search.AlphaBeta(Int32 depth, Int32 alpha, Int32 beta, Move& bestMove) in /home/jud/projects/RomanticEngine/RomanticEngine.Core/Search.cs:line 133
   at RomanticEngine.Core.Search.AlphaBeta(Int32 depth, Int32 alpha, Int32 beta, Move& bestMove) in /home/jud/projects/RomanticEngine/RomanticEngine.Core/Search.cs:line 132
   at RomanticEngine.Core.Search.AlphaBeta(Int32 depth, Int32 alpha, Int32 beta, Move& bestMove) in /home/jud/projects/RomanticEngine/RomanticEngine.Core/Search.cs:line 132
   at RomanticEngine.Core.Search.Start(SearchLimits limits, Action`1 onInfo, Action`1 onBestMove) in /home/jud/projects/RomanticEngine/RomanticEngine.Core/Search.cs:line 64
   at RomanticEngine.Core.Engine.<>c__DisplayClass15_0.<Go>b__0() in /home/jud/projects/RomanticEngine/RomanticEngine.Core/Engine.cs:line 96
```

## Acceptance Criteria

* [ ] `go depth 12` should not cause `IndexOutOfRangeException`.
* [ ] The `info` lines returned by `go` must contain complete information.
* [ ] All UCI options must have their `default` and `max` value corrected.
* [ ] Thoroughly unit tests with meaningful asserts which fully cover the behavior described in this task are necessary to consider this task done.

---

# Follow-up Comments after AI Agent attempted implementation

`Test_Uci_Output_Correctness` is bullshit. I need you to test string output. It's nice to have a clean interface that's usable by a library in `IEngine`/`Engine`, but we need an alternative mechanism for interacting with the engine. Create a `UciAdapter` class that wraps `Engine`, then much of the logic in `UciLoop` in the main Application project could go there and keep that logic properly living with the rest of the engine code instead of in the binary, which is a domain leak we don't really want in the binary itself. Does this make sense? If so, repeat back using your own words. If not, ask follow-up questions. Do not write or modify any code yet.