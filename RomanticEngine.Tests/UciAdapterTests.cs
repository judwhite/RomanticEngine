using RomanticEngine.Core;

namespace RomanticEngine.Tests;

public class UciAdapterTests
{
    [Fact]
    public void Test_MalformedCommands_DoNotThrow()
    {
        var engine = new Engine();
        var outputs = new List<string>();
        var lockObj = new Lock();
        var adapter = new UciAdapter(engine, s =>
        {
            lock (lockObj) outputs.Add(s);
        });

        string[] malformed =
        [
            "",
            "   ",
            "position",
            "position fen",
            "position fen r1b1k1nr/pppp1ppp/2n5/2b1p3/2B1P3/2N5/PPPP1PPP/R1BQK1NR w KQkq - 4 5 moves",
            "go depth",
            "go wtime abc",
            "setoption name",
            "setoption name NonExistentOption",
            "setoption name Hash value abc",
            "garbage_command_123",
            "isready extra_tokens"
        ];

        foreach (var cmd in malformed)
        {
            // Should not throw
            adapter.ReceiveCommand(cmd);
        }

        // Ensure engine is still responsive
        adapter.ReceiveCommand("isready");
        lock (lockObj)
        {
            Assert.Contains("readyok", outputs);
        }
    }

    [Fact]
    public void Test_Position_FEN_Correctness()
    {
        var engine = new Engine();
        var outputs = new List<string>();
        var adapter = new UciAdapter(engine, outputs.Add);

        // Valid 6-field FEN
        const string fen = "r1b1k1nr/pppp1ppp/2n5/2b1p3/2B1P3/2N5/PPPP1PPP/R1BQK1NR w KQkq - 4 5";
        adapter.ReceiveCommand($"position fen {fen}");

        // This is hard to verify without exposing engine state,
        // but we can at least check it doesn't log a FEN error.
        Assert.DoesNotContain(outputs, s => s.Contains("FEN requires"));

        // Invalid FEN (fewer than 6 fields)
        outputs.Clear();
        adapter.ReceiveCommand("position fen r1b1k1nr/pppp1ppp/2n5");
        Assert.Contains(outputs, s => s.Contains("FEN requires 6 fields"));
    }

    [Fact]
    public void Test_Position_Moves_Diagnostic_DoesNotDoublePrefixInfo()
    {
        var engine = new Engine();
        var outputs = new List<string>();
        var adapter = new UciAdapter(engine, outputs.Add);

        // Illegal move: e2e5 (not legal in startpos)
        adapter.ReceiveCommand("position startpos moves e2e5");

        var diag = outputs.SingleOrDefault(s => s.Contains("illegal move", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(diag);

        // Output must be protocol-correct: exactly one "info" prefix.
        Assert.StartsWith("info string", diag);
        Assert.DoesNotContain("info info", diag, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Test_Go_SearchMoves_Parsing_RestrictsRootMoves()
    {
        var engine = new Engine();
        var outputs = new List<string>();
        var adapter = new UciAdapter(engine, outputs.Add);
        var bestMoveEvent = new ManualResetEvent(false);

        engine.OnBestMove += _ => bestMoveEvent.Set();

        adapter.ReceiveCommand("position startpos");
        outputs.Clear();

        // Restrict search to a single move
        adapter.ReceiveCommand("go depth 2 searchmoves h2h3");

        Assert.True(bestMoveEvent.WaitOne(5000), "Expected a bestmove within timeout.");

        var bestMoveLine = outputs.LastOrDefault(s => s.StartsWith("bestmove "));
        Assert.NotNull(bestMoveLine);
        Assert.Equal("bestmove h2h3", bestMoveLine);
    }
}
