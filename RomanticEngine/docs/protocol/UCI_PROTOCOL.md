# The UCI Protocol

## Description of the Universal Chess Interface (UCI)

UCI (Universal Chess Interface) is a standard, text-based protocol for communicating between a chess engine and a client over standard input/output.

* All communication is done via standard input and output using text commands.
* The engine should boot and wait for input from the client.
  The engine should wait for the `isready` or `setoption` command to set up its internal parameters, as the boot process should be as quick as possible.
* The engine must always be able to process input from `stdin`, even while thinking.
* All command strings the engine receives will end with `\n`.
  Likewise, all commands the client receives should end with `\n`.
* Before the engine is asked to search a position, there will always be a `position` command to tell the engine about the current position.
* If the engine or the client receives an unknown command or token, it should ignore it.
* If the engine receives a command that is not supposed to arrive (for example, `stop` when the engine is not calculating), it should also ignore it.

## Move format

The move format is in long algebraic (coordinate) notation.

A null move from the engine to the client should be sent as `0000`.

Examples:

* `e2e4`
* `e7e5`
* `e1g1` (White short castling)
* `e7e8q` (promotion)

## Client to engine

These are all the commands the engine gets from the interface.

### `uci`

Tell the engine to use UCI (Universal Chess Interface). This will be sent once as the first command after program boot.

After receiving the `uci` command, the engine must identify itself with the `id` command and send the `option` commands to tell the client which engine settings the engine supports.

After that, the engine should send `uciok` to acknowledge UCI mode.

### `isready`

This is used to synchronize the engine with the client. When the client has sent a command (or multiple commands) that can take some time to complete, `isready` can be used to wait for the engine to be ready.

For example, this should be sent after:

* setting the path to the tablebases
* setting the hash size

…as these can take some time.

This command is also required once before the engine is asked to do any search, to wait for the engine to finish initializing.

This command must always be answered with `readyok` when the engine is ready.

When the engine is calculating, the engine should immediately answer with `readyok` without stopping the search.

### `setoption name <name> [value <value>]`

This is sent to the engine when the user wants to change the internal parameters of the engine. For the `button` type, no value is needed.

One string will be sent for each parameter, and this will only be sent when the engine is waiting.

* The `name` of the option should not be case-sensitive and can include spaces.
* The `value` may also include spaces.
* The substrings `value` and `name` should be avoided in option names to allow unambiguous parsing. For example, do not create a UCI option named `draw value`.

Here are some strings for the example below:

```text
setoption name Debug Log File value /home/jud/engine.log\n
setoption name MultiPV value 3\n
setoption name Threads value 22\n
setoption name Hash value 8192\n
setoption name Clear Hash\n
setoption name Move Overhead value 60\n
setoption name SyzygyPath value /home/jud/chess/tb/4:/home/jud/chess/tb/5\n
```

### `ucinewgame`

This is sent to the engine when the next search (started with `position` and `go`) will be from a different game. This can be:

* a new game the engine should play,
* a new game it should analyze, or
* the next position from a test suite containing positions only.

If the client hasn't sent `ucinewgame` before the first `position` command, the engine shouldn't expect any further `ucinewgame` commands, as the client is probably not supporting the `ucinewgame` command.

So the engine should not rely on this command, even though all new clients should support it.

As the engine's reaction to `ucinewgame` can take some time, the client should always send `isready` after `ucinewgame` to wait for the engine to finish its operation.

> **Common practice:** engines typically treat `ucinewgame` as a signal to clear game-related state (for example, hash/history state associated with the previous game). Clients should still not assume a specific internal behavior beyond “the next search is from a different game.”

### `position [fen <fenstring> | startpos] [moves <move1> <move2> ...]`

Set up the position described in the FEN string on the internal board, and play the moves on the internal chess board.

If the game was played from the start position, the string `startpos` will be sent.

**Note:** No `new` command is needed. However, if this position is from a different game than the last position sent to the engine, the client should have sent `ucinewgame`.

**Recommended practice:** If you have the game move history, prefer `startpos ... moves ...` (or `fen ... moves ...`) over sending only a final FEN, because the move list provides the engine additional context that may matter for rules like repetition handling.

Examples:

```text
position startpos
position fen rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1
position startpos moves e2e4 e7e5
position fen rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2
position fen rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2 moves g1f3 b8c6
```

### `go`

Start calculating on the current position set up with the `position` command.

There are a number of commands that can follow this command; all will be sent in the same string.

If one command is not sent, its value should be interpreted as if it would not influence the search.

> **Practical note:** If multiple limits are provided (for example, `depth` and `movetime`), engines generally treat them as simultaneous constraints and stop when *any* stopping condition is reached.

* `searchmoves ...`
  Restrict search to these moves only.
  Example: After `position startpos` and `go infinite searchmoves e2e4 d2d4`, the engine should only search the two moves `e2e4` and `d2d4` in the initial position.

* `ponder`
  Start searching in **pondering mode** (thinking during the opponent’s turn).

  This mode is most commonly used after the engine previously returned a hint like
  `bestmove <ourMove> ponder <theirMove>`, but it is **not required**: the client may choose
  to start a ponder search (or not) regardless of whether the engine suggested a ponder move.

  **How pondering is used in practice**

    * The client starts a ponder search by sending `go ponder ...` on a **predicted position**:
      a position that already includes the **predicted opponent reply** (the *ponder move*).
      In other words, the client typically sends:
      `position ... moves <ourMove> <theirPredictedReply>`
      and then `go ponder ...`, so the resulting position is the one the engine will face **if**
      the opponent plays that predicted reply.

    * While pondering, the engine may continue searching and sending `info` output, but it should
      **not conclude the search by sending `bestmove`** until the client either:
        - sends `ponderhit` (prediction confirmed; convert to normal search), or
        - sends `stop` (prediction rejected or pondering aborted).

    * The *ponder move* is indicated by the client as the **final move** in the
      `position ... moves ...` list used for the ponder search. Clients generally assume the engine
      is searching that exact resulting position.

* `wtime`
  White has *x* ms left on the clock.

* `btime`
  Black has *x* ms left on the clock.

* `winc`
  White increment per move in ms, if *x* > 0.

* `binc`
  Black increment per move in ms, if *x* > 0.

* `depth`
  Search *x* plies only.

* `nodes`
  Search *x* nodes only.

* `mate`
  Search for a mate in *x* moves.

* `movetime`
  Search exactly *x* ms.

* `infinite`
  Search until the `stop` command. Do not exit the search without being told so in this mode.

### `stop`

Stop calculating as soon as possible.

The engine must still send a terminating `bestmove ...` for the current search (i.e., for the most
recent `go` command), even if `stop` is received while pondering. The engine may also include an
optional ponder suggestion:

```text
bestmove <move> ponder <move>
```

**Client note**: If `stop` is used to abort a ponder search because the opponent played a different
move than predicted, the engine’s resulting `bestmove` applies to the predicted ponder position
and is typically ignored by the client—but it still must be sent by the engine to keep the protocol synchronized.

### `ponderhit`

Indicates that the opponent played the move the engine was pondering for.

This command is meaningful when the engine is currently pondering (i.e., after a `go ponder ...`
search has started). When the engine receives `ponderhit`, it should:

* switch from “pondering” to “normal” search for the **same position**, and
* continue searching toward producing a `bestmove` normally (including any applicable time management).

`ponderhit` does **not** start a new search: it converts the ongoing ponder search into a normal one.
The engine should later emit `bestmove` without requiring another `go`.

If the engine is *not* currently pondering, it should ignore `ponderhit`.

#### Minimal example flow (typical client behavior)

```text
# Engine finishes its move and suggests an expected opponent reply:
<- bestmove e2e4 ponder c7c5

# Client initiates a ponder search on the predicted opponent reply (last move is the ponder move):
-> position startpos moves e2e4 c7c5
-> go ponder wtime 300000 btime 300000 winc 0 binc 0

# If the opponent actually plays c7c5, the client confirms it:
-> ponderhit

# Engine continues (now normal search) and eventually returns a bestmove for its next turn:
<- bestmove g1f3 ponder d7d6
```

If the opponent does **not** play the predicted move, clients typically send `stop` to end the ponder
search, read (and ignore) the required terminating `bestmove`, then send a new `position ...` for the
actual move and a normal `go ...`.

### `quit`

Quit the program as soon as possible.

## Engine to client

### `id`

* `name`
  This must be sent after receiving the `uci` command to identify the engine.
  Example: `id name RomanticChess 6.9\n`
* `author`
  This must be sent after receiving the `uci` command to identify the engine.
  Example: `id author Paul Morphy\n`

### `uciok`

Must be sent after the `id` and optional `option` commands to tell the client that the engine has sent all information and is ready in UCI mode.

### `readyok`

This must be sent when the engine has received an `isready` command, has processed all input, and is ready to accept new commands.

It is usually sent after a command that can take some time, so the client can wait for the engine, but it can be used anytime—even when the engine is searching.

An `isready` command must always be answered with `readyok`.

### `bestmove`

The engine has stopped searching and found the best move in this position.

This command must always be sent if the engine stops searching, so for every `go` command a `bestmove` command is needed.

Directly before that, the engine should send a final `info` command with the final search information, so the client has complete statistics about the last search.

#### Optional `ponder` move

If the engine supports pondering, it may append an expected opponent reply:

```text
bestmove <move> ponder <move>
```

This `ponder` move is a **hint** to the client about what move the engine expects next, and what line it would like to ponder on.

### `info`

The engine wants to send information to the client. This should be done whenever one of the info fields has changed.

The engine can send only selected fields, and multiple fields can be sent with one `info` command, for example:

```text
info depth 12 nodes 123456 nps 100000
```

All info fields belonging to the PV should be sent together, for example:

```text
info depth 2 score cp 214 time 1242 nodes 2124 nps 34928 pv e2e4 e7e5 g1f3
```

Here is a more complete example of `info` where `MultiPV` is set to 3:

```text
info depth 18 seldepth 41 multipv 1 score cp 28 nodes 13490585 nps 10189263 hashfull 543 tbhits 0 time 1324 pv g1f3 b8c6 f1b5 g8f6 e1g1 f6e4 f1e1 e4d6 f3e5 f8e7 b5f1 c6e5 e1e5 e8g8 d2d4 e7f6 e5e1 f8e8 c1f4 e8e1 d1e1 f6d4
info depth 18 seldepth 35 multipv 2 score cp 13 nodes 13490585 nps 10189263 hashfull 543 tbhits 0 time 1324 pv b1c3 g8f6 g1f3 b8c6 f1b5 c6d4 b5c4 d4f3 d1f3 f8c5 f3g3 e8g8 d2d3 d7d6 c1g5 c7c6 a2a4 c8e6 a4a5 f6h5 g3h4 h5f6 c4e6 f7e6 e1g1 h7h6 g5d2 d6d5
info depth 18 seldepth 31 multipv 3 score cp 0 nodes 13490585 nps 10189263 hashfull 543 tbhits 0 time 1324 pv f1c4 g8f6 d2d3 c7c6 c4b3 a7a5 a2a4 d7d5 g1f3 f8b4 c2c3 b4d6 e1g1 e8g8 f1e1 d5e4 d3e4 b8a6 b3c2 d8e7 b1d2 f8d8 d1e2 a6c5
```

Additional info fields:

* `depth`
  Search depth in plies.
* `seldepth`
  Selective search depth in plies.
  If the engine sends `seldepth`, there must also be a `depth` in the same string.
* `time`
  The time searched in ms; this should be sent together with the PV.
* `nodes`
  *x* nodes searched; the engine should send this info regularly.
* `pv ...`
  The best line found.
* `multipv`
  This is for MultiPV mode. For the best move/PV, add `multipv 1` in the string when you send the PV.
  In k-best mode, always send all *k* variants in *k* strings together.
* `score`
    * `cp`
      The score from the engine's point of view in centipawns. Do not include `cp` if mate is found.
    * `mate`
      Mate in *y* moves (not plies).
      If the engine is getting mated, use negative values for *y*.
* `hashfull`
  The hash is *x* per mille full; the engine should send this info regularly (e.g., `543` = 54.3%).
* `nps`
  *x* nodes per second searched; the engine should send this info regularly.
* `tbhits`
  *x* positions found in the endgame tablebases.

### `option`

This command tells the client which parameters can be changed in the engine.

This should be sent once by the engine at startup after the `uci` and the `id` commands, if any parameter can be changed in the engine.

If the user wants to change some settings, the client will send a `setoption` command to the engine.

Note that the client need not send `setoption` for every option when starting the engine if it doesn't want to change the default value.

For all allowed combinations, see the example below, as some combinations of these tokens don't make sense.

One string will be sent for each parameter.

* `name`
  The option has the name id.

  Certain commonly-supported options have a conventional type and meaning:

    * `Threads`: type `spin`, default `1`, min `1`, max `[cpu max]`
    * `Hash`: type `spin`, default `16`, min `1`, max `[mem max]`
      The value is in MB for memory used for hash tables.
    * `MultiPV`: type `spin`, default `1`, min `1`, max `256`
      The engine supports multiple best lines (k-best mode). The default value is `1`.
    * `Clear Hash`: type `button`
    * `Ponder`: type `check`, default `false`
      When `true`, the engine is allowed to ponder (think during the opponent’s time) when the client uses `go ponder` / `ponderhit`.
    * `Move Overhead`: type `spin`, default `10`, min `0`, max `5000`
    * `SyzygyPath`: type `string`, default `<empty>`
      This is the path on the hard disk to the Syzygy endgame tablebases.
      Multiple directories are commonly separated by `:` on Unix-like systems and `;` on Windows.

* `type`
  The option has type *t*. There are five different types of options the engine can send:

    * `check`
      A checkbox that can either be `true` or `false`.
    * `spin`
      A spin wheel that can be an integer in a certain range.
    * `combo`
      A combo box that can have different predefined strings as a value.
    * `button`
      A button that can be pressed to send a command to the engine.
    * `string`
      A text field that has a string as a value. An empty string has the value `""`.

* `default`
  The default value of this parameter is *x*.

* `min`
  The minimum value of this parameter is *x*.

* `max`
  The maximum value of this parameter is *x*.

* `var`
  A predefined value of this parameter is *x*.

Example:

Here are 5 strings for each of the 5 possible types of options:

```text
option name Nullmove type check default true\n
option name Selectivity type spin default 2 min 0 max 4\n
option name Style type combo default Normal var Solid var Normal var Risky\n
option name SyzygyPath type string default <empty>\n
option name Clear Hash type button\n
```

## Example

This is how the communication when the engine boots can look.

* `->` denotes a command received by the engine.
* `<-` denotes a command sent by the engine.

```text
-> uci
<- id name RomanticChess 6.9
<- id author Paul Morphy
<- option name Debug Log File type string default <empty>
<- option name Threads type spin default 1 min 1 max 1024
<- option name Hash type spin default 16 min 1 max 33554432
<- option name Clear Hash type button
<- option name Ponder type check default false
<- option name MultiPV type spin default 1 min 1 max 256
<- option name Move Overhead type spin default 10 min 0 max 5000
<- option name SyzygyPath type string default <empty>
<- uciok
-> setoption name Threads value 12
<- info string Using 12 threads
-> setoption name Hash value 6144
-> setoption name SyzygyPath value /home/jud/chess/tb/4:/home/jud/chess/tb/5
-> setoption name MultiPV value 3
-> isready
<- readyok
-> ucinewgame
-> position startpos moves e2e4 e7e5
-> isready
<- readyok
-> go depth 12
<- info string Available processors: 0-27
<- info string Using 12 threads
<- info depth 1 seldepth 4 multipv 1 score cp 3 nodes 1080 nps 1080000 hashfull 0 tbhits 0 time 1 pv g1f3
<- info depth 1 seldepth 2 multipv 2 score cp 0 nodes 1080 nps 1080000 hashfull 0 tbhits 0 time 1 pv g1e2
<- info depth 1 seldepth 4 multipv 3 score cp -6 nodes 1080 nps 1080000 hashfull 0 tbhits 0 time 1 pv d2d4 e5d4
<- info depth 2 seldepth 3 multipv 1 score cp 44 nodes 3119 nps 3119000 hashfull 0 tbhits 0 time 1 pv g1f3 d7d6
<- info depth 2 seldepth 4 multipv 2 score cp 32 nodes 3119 nps 3119000 hashfull 0 tbhits 0 time 1 pv f1c4
<- info depth 2 seldepth 3 multipv 3 score cp 29 nodes 3119 nps 3119000 hashfull 0 tbhits 0 time 1 pv g1e2
<- info depth 3 seldepth 4 multipv 1 score cp 53 nodes 4887 nps 4887000 hashfull 0 tbhits 0 time 1 pv g1f3 d7d6
<- info depth 3 seldepth 5 multipv 2 score cp 33 nodes 4887 nps 4887000 hashfull 0 tbhits 0 time 1 pv f1c4
<- info depth 3 seldepth 5 multipv 3 score cp 16 nodes 4887 nps 4887000 hashfull 0 tbhits 0 time 1 pv b1c3 g8f6
<- info depth 4 seldepth 5 multipv 1 score cp 59 nodes 11688 nps 5844000 hashfull 0 tbhits 0 time 2 pv g1f3 b8c6
<- info depth 4 seldepth 6 multipv 2 score cp 46 nodes 11688 nps 5844000 hashfull 0 tbhits 0 time 2 pv f1c4 g8f6
<- info depth 4 seldepth 6 multipv 3 score cp 45 nodes 11688 nps 5844000 hashfull 0 tbhits 0 time 2 pv b1c3 g8f6
<- info depth 5 seldepth 6 multipv 1 score cp 52 nodes 22741 nps 5685250 hashfull 0 tbhits 0 time 4 pv g1f3 b8c6 f1b5 g8f6 b1c3
<- info depth 5 seldepth 11 multipv 2 score cp 49 nodes 22741 nps 5685250 hashfull 0 tbhits 0 time 4 pv b1c3 b8c6
<- info depth 5 seldepth 7 multipv 3 score cp 30 nodes 22741 nps 5685250 hashfull 0 tbhits 0 time 4 pv f1c4 g8f6 b1c3 f8b4 g1f3 b8c6
<- info depth 6 seldepth 11 multipv 1 score cp 52 nodes 29466 nps 7366500 hashfull 0 tbhits 0 time 4 pv g1f3 g8f6 b1c3 b8c6 f1b5 c6d4 f3e5
<- info depth 6 seldepth 10 multipv 2 score cp 52 nodes 29466 nps 7366500 hashfull 0 tbhits 0 time 4 pv b1c3 b8c6 g1f3 g8f6 f1b5 c6d4
<- info depth 6 seldepth 9 multipv 3 score cp 35 nodes 29466 nps 7366500 hashfull 0 tbhits 0 time 4 pv f1c4 g8f6 b1c3 f8b4 g1f3 e8g8
<- info depth 7 seldepth 9 multipv 1 score cp 45 nodes 49083 nps 7011857 hashfull 0 tbhits 0 time 7 pv g1f3 g8f6 b1c3 b8c6 f1b5 f8b4
<- info depth 7 seldepth 13 multipv 2 score cp 39 nodes 49083 nps 7011857 hashfull 0 tbhits 0 time 7 pv b1c3 g8f6 g1f3 f8b4 f3e5 e8g8 e5f3 b4c3 d2c3 f6e4
<- info depth 7 seldepth 8 multipv 3 score cp 26 nodes 49083 nps 7011857 hashfull 0 tbhits 0 time 7 pv f1c4 g8f6 b1c3 f8c5 g1f3 b8c6 e1g1
<- info depth 8 seldepth 9 multipv 1 score cp 51 nodes 72808 nps 8089777 hashfull 0 tbhits 0 time 9 pv g1f3 b8c6 f1b5 g8e7 b1c3 a7a6 b5c6 e7c6
<- info depth 8 seldepth 11 multipv 2 score cp 44 nodes 72808 nps 8089777 hashfull 0 tbhits 0 time 9 pv b1c3 g8f6 g1f3 f8b4 f3e5 e8g8 e5f3 b4c3 d2c3 f6e4
<- info depth 8 seldepth 12 multipv 3 score cp 26 nodes 72808 nps 8089777 hashfull 0 tbhits 0 time 9 pv f1c4 g8f6 b1c3 f8c5
<- info depth 9 seldepth 18 multipv 1 score cp 41 nodes 311572 nps 7989025 hashfull 0 tbhits 0 time 39 pv b1c3 b8c6 g1f3 g8f6 f1b5 f8d6
<- info depth 9 seldepth 15 multipv 2 score cp 38 nodes 311572 nps 7989025 hashfull 0 tbhits 0 time 39 pv g1f3 g8f6 d2d4 f6e4 f3e5 d7d5 b1d2 f8d6 d2e4 d5e4 d1h5
<- info depth 9 seldepth 14 multipv 3 score cp 10 nodes 311572 nps 7989025 hashfull 0 tbhits 0 time 39 pv d2d4 e5d4 g1f3 f8b4 b1d2 d8e7 a2a3 b4d2 d1d2 e7e4 e1d1
<- info depth 10 seldepth 21 multipv 1 score cp 38 nodes 469577 nps 7958932 hashfull 0 tbhits 0 time 59 pv g1f3 g8f6 d2d4 d7d5 e4d5
<- info depth 10 seldepth 23 multipv 2 score cp 27 nodes 469577 nps 7958932 hashfull 0 tbhits 0 time 59 pv b1c3 b8c6 g1f3 g8f6 f1b5 f8b4 e1g1 e8g8 f1e1 f8e8 a2a3 b4c5 h2h3
<- info depth 10 seldepth 29 multipv 3 score cp 13 nodes 469577 nps 7958932 hashfull 0 tbhits 0 time 59 pv d2d4 e5d4 g1f3 f8c5 c1g5 f7f6 g5f4 b8c6 c2c3
-> stop // here the user has seen enough and asks to stop the search
<- info depth 11 seldepth 20 multipv 1 score cp 30 nodes 740137 nps 8044967 hashfull 0 tbhits 0 time 92 pv g1f3 g8f6 d2d4 f6e4 f1d3 d7d5 f3e5 b8d7 e5d7 c8d7
<- info depth 11 seldepth 24 multipv 2 score cp 23 nodes 740137 nps 8044967 hashfull 0 tbhits 0 time 92 pv b1c3 g8f6 g1f3 b8c6 d2d4 e5d4 f3d4 f8b4 d4c6 b7c6 f1d3 e8g8
<- info depth 10 seldepth 29 multipv 3 score cp 13 nodes 469577 nps 7958932 hashfull 0 tbhits 0 time 59 pv d2d4 e5d4 g1f3 f8c5 c1g5 f7f6 g5f4 b8c6 c2c3
<- bestmove g1f3 ponder g8f6
```

## Practical state model (engine behavior)

UCI engines must be able to accept input at any time (even while searching), but it helps to think of the engine as operating in a small number of **conceptual states**. This is not a separate protocol feature—just a practical mental model for implementers.

- **Idle (Ready)**
    - Meaning: No search is currently running.
    - Typical inputs: `setoption`, `ucinewgame`, `position`, `go`, `isready`, `quit`
    - Typical outputs: `readyok` in response to `isready`

- **Searching (Normal)**
    - Entered when: the client sends `go` (without `ponder`), or the engine receives `ponderhit` while pondering (converting ponder → normal search).
    - Meaning: The engine is actively searching for the side to move in the current `position`.
    - Typical outputs: one or more `info ...` lines, then exactly one terminating `bestmove ...`
    - Typical inputs while searching:
        - `stop` → engine transitions toward stopping and must still emit `bestmove ...`
        - `isready` → engine must reply `readyok` immediately (without stopping the search)
        - `quit` → engine should exit as soon as possible

- **Pondering**
    - Entered when: the client sends `go ponder ...`
    - Meaning: The engine is searching “during the opponent’s time” on a position that already includes the *predicted opponent reply* (the ponder move).
    - Typical outputs: `info ...` lines (optionally), but **no terminating `bestmove` yet**
    - Typical inputs while pondering:
        - `ponderhit` → convert the ongoing search into **normal** searching for the same position (no new `go`), then later emit `bestmove ...`
        - `stop` → abort pondering; engine must still emit a terminating `bestmove ...` for that `go ponder ...` search, then return to Idle
        - `isready` → reply `readyok` immediately

**Synchronization rule of thumb:** every `go ...` (including `go ponder ...`) must eventually be “closed out” by exactly one `bestmove ...` line. Even if the client plans to ignore that `bestmove` (e.g., because it aborted pondering due to a mismatched opponent move), it should still read it to keep the protocol stream aligned.

## External Links

* https://github.com/official-stockfish/Stockfish/wiki/UCI-&-Commands
* https://backscattering.de/chess/uci/
* https://www.chessprogramming.org/UCI
* https://wiki.chessdom.org/index.php?title=R-Mobility
* https://www.chessprogramming.org/Mobility