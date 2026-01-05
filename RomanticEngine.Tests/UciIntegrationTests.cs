using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.MoveGeneration;
using Rudzoft.ChessLib.Types;

namespace RomanticEngine.Tests;

public class UciIntegrationTests
{
    [Fact]
    public void Test_Uci_Handshake_Golden_Strict()
    {
        using var harness = new UciHarness();
        harness.Send("uci");

        var outputs = harness.DrainOutput();

        // Asserts exact content and ordering for key lines
        Assert.Equal("id name RomanticEngine 1.0", outputs[0]);
        Assert.Equal("id author Jud White", outputs[1]);

        Assert.Contains("option name Threads type spin default 1 min 1 max 28", outputs);
        Assert.Contains("option name Hash type spin default 16 min 1 max 120395", outputs);

        Assert.Equal("uciok", outputs.Last());
    }

    [Fact]
    public void Test_Regression_BUG_01_DeepSearch_NoCrash()
    {
        using var harness = new UciHarness();
        harness.Send("position startpos");
        harness.Send("go depth 12");

        // Wait till we get at least one info line
        var info = harness.WaitForLine(s => s.StartsWith("info depth 1"));
        Assert.NotNull(info);

        // Stop early
        harness.Send("stop");

        var bestmove = harness.WaitForLine(s => s.StartsWith("bestmove"));
        Assert.NotNull(bestmove);

        var outputs = harness.AllHistory;
        Assert.DoesNotContain(outputs, s => s.Contains("Exception"));
        // Exactly one bestmove for this go
        Assert.Single(outputs, s => s.StartsWith("bestmove"));
    }

    [Fact]
    public void Test_Exactly_One_BestMove_Per_Go()
    {
        using var harness = new UciHarness();

        // 1. Normal go
        harness.Send("position startpos");
        harness.Send("go depth 1");
        Assert.NotNull(harness.WaitForLine(s => s.StartsWith("bestmove")));
        Assert.DoesNotContain(harness.DrainOutput(), s => s.StartsWith("bestmove"));

        // 2. Go then Stop
        harness.Send("go depth 10");
        harness.Send("stop");
        Assert.NotNull(harness.WaitForLine(s => s.StartsWith("bestmove")));
        Assert.DoesNotContain(harness.DrainOutput(), s => s.StartsWith("bestmove"));

        // 3. Overlapping go
        harness.Send("go depth 10");
        harness.Send("go depth 1");
        Assert.NotNull(harness.WaitForLine(s => s.StartsWith("bestmove")));

        // We might get multiple bestmoves in history, but we only care
        // that after the final WaitForLine, no MORE bestmoves are pending.
        // Also, we can check AllHistory for exactly 3 bestmoves total.
        Assert.DoesNotContain(harness.DrainOutput(), s => s.StartsWith("bestmove"));
        Assert.Equal(3, harness.AllHistory.Count(s => s.StartsWith("bestmove")));
    }

    [Fact]
    public void Test_PV_Legality_And_Formatting()
    {
        using var harness = new UciHarness();

        // Use a position with more tactical complexity
        harness.Send("position fen rnbqkbnr/ppp2ppp/8/3pp3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3");
        harness.Send("go depth 4");

        var bestmove = harness.WaitForLine(s => s.StartsWith("bestmove"));
        Assert.NotNull(bestmove);

        var outputs = harness.AllHistory;
        var infoWithPv = outputs.LastOrDefault(s => s.Contains("pv "));
        Assert.NotNull(infoWithPv);

        var tokens = infoWithPv.Split(' ');
        int pvStart = Array.IndexOf(tokens, "pv") + 1;
        var pvMoves = tokens.Skip(pvStart).ToList();

        Assert.NotEmpty(pvMoves);

        // Verify moves are legal using Rudzoft
        var game = GameFactory.Create();
        game.NewGame("rnbqkbnr/ppp2ppp/8/3pp3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3");

        foreach (var moveStr in pvMoves)
        {
            var moves = game.Pos.GenerateMoves();
            var move = moves.FirstOrDefault(m => m.Move.ToString() == moveStr);
            Assert.False(move.Equals(default(ExtMove)), $"Illegal PV move {moveStr} in {infoWithPv}");
            game.Pos.MakeMove(move.Move, new State());
        }
    }

    [Fact]
    public void Test_Stress_Repeated_Cycles()
    {
        using var harness = new UciHarness();

        for (int i = 0; i < 20; i++)
        {
            harness.Send("ucinewgame");
            harness.Send("position startpos");
            harness.Send("go depth 2");
            var bestmove = harness.WaitForLine(s => s.StartsWith("bestmove"), 2000);
            Assert.NotNull(bestmove);
            harness.DrainOutput();
        }
    }
}
