Create a UCI chess engine in C#.

{ "id": "RomanticEngine", "author": "Jud White" }

Follow the specification found in UCI_PROTOCOL.md. Fully implement the protocol as described. Create tasks for yourself to break up work into smaller chunks.

Use the Console Application to handle setting up the stdin/stdout loops and other supporting concerns the engine doesn't need to explicitly know about.

Create a new project (or multiple) within the solution to hold core engine logic, etc. where it makes sense.

The evaluation does not need to be perfect. Focus on correctly implementing the protocol and generating legal moves and a *reasonable* evaluation based on heuristics or handcrafted rules. Use ideas from this page https://wiki.chessdom.org/index.php?title=R-Mobility to approximate evaluation.

You may use existing libraries for chess move generation, but implement the core logic yourself to ensure understanding and control.
