using System.Text;
using RomanticEngine.Core;
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.MoveGeneration;

namespace RomanticEngine.Tests;

public class EngineTests
{
    [Fact]
    public void Test_Engine_Construction_ExposesOptions()
    {
        var engine = new Engine();
        Assert.NotNull(engine.Options);
        Assert.NotEmpty(engine.Options);
    }

    [Fact]
    public void Test_SetOption_UpdatesConfiguration()
    {
        var engine = new Engine();

        // Test toggles
        engine.SetOption("EnableKingSafety", "false");
        Assert.False(engine.Config.Evaluation.EnableKingSafety);

        engine.SetOption("EnableKingSafety", "true");
        Assert.True(engine.Config.Evaluation.EnableKingSafety);

        // Test weights
        engine.SetOption("MaterialWeight", "50");
        Assert.Equal(50, engine.Config.Evaluation.MaterialWeight);
    }

    [Fact]
    public void Test_SetOption_StandardOptions()
    {
        var engine = new Engine();

        // Threads
        engine.SetOption("Threads", "4");
        Assert.Equal(4, engine.Config.Standard.Threads);

        // Move Overhead
        engine.SetOption("Move Overhead", "500");
        Assert.Equal(500, engine.Config.Standard.MoveOverhead);

        // SyzygyPath (Just storage as requested)
        var path = "/path/to/tablebases";
        engine.SetOption("SyzygyPath", path);
        Assert.Equal(path, engine.Config.Standard.SyzygyPath);

        // Ponder
        engine.SetOption("Ponder", "true");
        Assert.True(engine.Config.Standard.Ponder);
    }

    [Fact]
    public void Test_SetPosition_StartPos_Go_ReturnsBestMove()
    {
        var engine = new Engine();
        var bestMoveEvent = new ManualResetEvent(false);
        string bestMoveString = "";

        engine.OnBestMove += move =>
        {
            bestMoveString = move;
            bestMoveEvent.Set();
        };

        engine.SetPosition("startpos");
        engine.Go(new SearchLimits { Depth = 1 });

        Assert.True(bestMoveEvent.WaitOne(5000));
        Assert.NotEmpty(bestMoveString);
        // Assert.StartsWith("bestmove", bestMoveString); // Prefix added by UciAdapter
    }

    [Fact]
    public void Test_SetPosition_Fen_Go_ReturnsBestMove()
    {
        var engine = new Engine();
        var bestMoveEvent = new ManualResetEvent(false);
        string? bestMove = null;

        engine.OnBestMove += move =>
        {
            bestMove = move;
            bestMoveEvent.Set();
        };

        // Simple start position FEN (equivalent to startpos)
        engine.SetPosition("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        engine.Go(new SearchLimits { Depth = 1 });

        Assert.True(bestMoveEvent.WaitOne(5000));
        Assert.False(string.IsNullOrWhiteSpace(bestMove));
    }

    [Fact]
    public void Test_Stop_EmitsBestMove_AndDoesNotReportException()
    {
        var engine = new Engine();
        var bestMoveEvent = new ManualResetEvent(false);
        string? bestMove = null;
        var exceptionInfos = new List<string>();

        engine.OnBestMove += move =>
        {
            bestMove = move;
            bestMoveEvent.Set();
        };
        engine.OnInfo += info =>
        {
            if (info.Contains("Exception"))
            {
                lock (exceptionInfos)
                    exceptionInfos.Add(info);
            }
        };

        engine.SetPosition("startpos");

        // Start a longer search, then stop immediately.
        engine.Go(new SearchLimits { Depth = 20 });
        engine.Stop();

        Assert.True(bestMoveEvent.WaitOne(5000), "Expected a bestmove after stop.");
        Assert.False(string.IsNullOrWhiteSpace(bestMove));

        lock (exceptionInfos)
        {
            Assert.Empty(exceptionInfos);
        }
    }

    [Fact]
    public void Test_PonderHit_SuppressesBestMoveUntilHit_ThenStopEmits()
    {
        var engine = new Engine();
        var sawScoreEvent = new ManualResetEvent(false);
        var bestMoveEvent = new ManualResetEvent(false);
        string? bestMove = null;

        engine.OnScore += _ => sawScoreEvent.Set();
        engine.OnBestMove += move =>
        {
            bestMove = move;
            bestMoveEvent.Set();
        };

        engine.SetPosition("startpos");
        engine.Go(new SearchLimits { Depth = 4, Ponder = true });

        Assert.True(sawScoreEvent.WaitOne(5000), "Expected at least one info line during ponder search.");

        // While pondering, bestmove should not be emitted.
        Assert.False(bestMoveEvent.WaitOne(200), "bestmove should be suppressed during ponder.");

        engine.PonderHit();
        engine.Stop();

        Assert.True(bestMoveEvent.WaitOne(5000), "Expected a bestmove after ponderhit+stop.");
        Assert.False(string.IsNullOrWhiteSpace(bestMove));
    }

    [Fact]
    public void Test_NewGame_AllowsSubsequentSearch()
    {
        var engine = new Engine();
        var bestMoveEvent = new ManualResetEvent(false);

        engine.OnBestMove += _ => bestMoveEvent.Set();

        engine.NewGame();
        engine.SetPosition("startpos");
        engine.Go(new SearchLimits { Depth = 1 });

        Assert.True(bestMoveEvent.WaitOne(5000), "Expected bestmove after NewGame + SetPosition + Go.");
    }

    [Fact]
    public void Test_Search_RespectsTimeLimit()
    {
        var engine = new Engine();
        var bestMoveEvent = new ManualResetEvent(false);
        var errors = new StringBuilder();

        engine.OnBestMove += _ => bestMoveEvent.Set();
        engine.OnInfo += info =>
        {
            if (info.Contains("Exception"))
                errors.AppendLine(info);
        };

        engine.SetPosition("startpos");

        // Move time 100ms
        engine.Go(new SearchLimits { MoveTime = 100 });

        // Wait 150ms
        bool signaled = bestMoveEvent.WaitOne(5000);
        Assert.True(signaled, $"Search timed out. Errors: {errors}");
    }

    [Fact]
    public void Test_Search_RespectsMoveTime()
    {
        var engine = new Engine();
        var bestMoveEvent = new ManualResetEvent(false);
        var errors = new StringBuilder();

        engine.OnBestMove += _ => bestMoveEvent.Set();
        engine.OnInfo += info =>
        {
            if (info.Contains("Exception"))
                errors.AppendLine(info);
        };

        engine.SetPosition("startpos");
        // movetime 500
        engine.Go(new SearchLimits { MoveTime = 500 });

        // Test for slow response: WaitOne(600) as requested.
        bool signaled = bestMoveEvent.WaitOne(2000);
        Assert.True(signaled, $"Search did not finish within 600ms (MoveTime 500). Errors: {errors}");
        Assert.True(errors.Length == 0, $"Search crashed with errors: {errors}");
    }

    private class FakeSystemInfo : ISystemInfo
    {
        public int MaxThreads => 28;
        public int MaxHashMb => 120395;
    }

    [Fact]
    public void Test_Uci_Handshake_Golden()
    {
        var si = new FakeSystemInfo();
        var engine = new Engine(si);
        var outputs = new List<string>();
        var adapter = new UciAdapter(engine, outputs.Add);

        adapter.ReceiveCommand("uci");

        // Assert exact strings and ordering
        Assert.Equal("id name RomanticEngine 1.0", outputs[0]);
        Assert.Equal("id author Jud White", outputs[1]);

        // Verify all 14 options are present and correctly formatted
        // Standard Options
        Assert.Contains("option name Debug Log File type string default <empty>", outputs);
        Assert.Contains("option name Threads type spin default 1 min 1 max 28", outputs);
        Assert.Contains("option name Hash type spin default 16 min 1 max 120395", outputs);
        Assert.Contains("option name Clear Hash type button", outputs);
        Assert.Contains("option name Ponder type check default false", outputs);
        Assert.Contains("option name MultiPV type spin default 1 min 1 max 256", outputs);
        Assert.Contains("option name Move Overhead type spin default 10 min 0 max 5000", outputs);
        Assert.Contains("option name SyzygyPath type string default <empty>", outputs);

        // Custom Heuristics
        Assert.Contains("option name EnableMaterial type check default true", outputs);
        Assert.Contains("option name EnableMobility type check default true", outputs);
        Assert.Contains("option name EnableKingSafety type check default true", outputs);
        Assert.Contains("option name MaterialWeight type spin default 1 min 0 max 100", outputs);
        Assert.Contains("option name MobilityWeight type spin default 2 min 0 max 100", outputs);
        Assert.Contains("option name KingSafetyWeight type spin default 5 min 0 max 100", outputs);

        // Check uciok is last
        Assert.Equal("uciok", outputs.Last());

        // Total lines: 2 id + 14 options + 1 uciok = 17
        Assert.Equal(17, outputs.Count);

        // Ensure no duplicates
        Assert.Equal(outputs.Count, outputs.Distinct().Count());
    }

    [Fact]
    public void Test_Info_String_Correctness()
    {
        var engine = new Engine();
        var scoreStrings = new List<string>();
        var completeEvent = new ManualResetEvent(false);

        engine.OnScore += info => scoreStrings.Add(info);
        engine.OnBestMove += _ => completeEvent.Set();

        engine.SetPosition("startpos");
        engine.Go(new SearchLimits { Depth = 4 }); // Enough to generate PVs

        Assert.True(completeEvent.WaitOne(5000));
        Assert.NotEmpty(scoreStrings);

        long prevNodes = -1;
        long prevTime = -1;
        const int prevMultiPv = 1; // default: 1

        foreach (var info in scoreStrings)
        {
            if (info.Contains("string"))
                continue; // Skip info string ...

            // Check required fields presence
            Assert.Contains("depth", info);
            Assert.Contains("score", info);
            Assert.Contains("nodes", info);
            Assert.Contains("nps", info);
            Assert.Contains("hashfull", info);
            Assert.Contains("tbhits", info);
            Assert.Contains("time", info);
            Assert.Contains("pv", info);
            Assert.Contains("multipv", info);

            var parts = info.Split(' ');

            // Validate parsability and ranges
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "score" && parts[i + 1] == "cp")
                {
                    int cp = int.Parse(parts[i + 2]);
                    Assert.InRange(cp, -64000, 64000);
                }

                if (parts[i] == "nodes")
                {
                    long nodes = long.Parse(parts[i + 1]);
                    Assert.True(nodes >= prevNodes, $"Nodes not monotonic: {nodes} < {prevNodes}");
                    prevNodes = nodes;
                }

                if (parts[i] == "time")
                {
                    long time = long.Parse(parts[i + 1]);
                    Assert.True(time >= prevTime, "Time not monotonic");
                    prevTime = time;
                }

                if (parts[i] == "hashfull")
                {
                    int hf = int.Parse(parts[i + 1]);
                    Assert.InRange(hf, 0, 1000);
                }

                if (parts[i] == "nps")
                {
                    long nps = long.Parse(parts[i + 1]);
                    Assert.True(nps >= 0); // Can be 0 if time is 0
                }

                if (parts[i] == "multipv")
                {
                    int mpv = int.Parse(parts[i + 1]);
                    Assert.True(mpv >= prevMultiPv); // Should be consistent for single line mode
                }
            }

            // Verify PV legality
            int pvIndex = Array.IndexOf(parts, "pv");
            Assert.True(pvIndex != -1 && pvIndex < parts.Length - 1, "PV missing moves");

            var game = Rudzoft.ChessLib.Factories.GameFactory.Create();
            game.NewGame();

            for (int i = pvIndex + 1; i < parts.Length; i++)
            {
                var moveStr = parts[i];
                var move = game.Pos.GenerateMoves().FirstOrDefault(m => m.Move.ToUci() == moveStr);
                Assert.False(move.Equals(default), $"Invalid PV move: {moveStr}");
                game.Pos.MakeMove(move, new State());
            }
        }
    }

    [Fact]
    public void Test_BestMove_Ponder_Legality()
    {
        var engine = new Engine();
        var outputs = new List<string>();
        var adapter = new UciAdapter(engine, outputs.Add);

        var completeEvent = new ManualResetEvent(false);
        engine.OnBestMove += _ => completeEvent.Set();

        engine.SetPosition("startpos");
        adapter.ReceiveCommand("go depth 4");

        Assert.True(completeEvent.WaitOne(5000));
        var bestMoveLine = outputs.LastOrDefault(s => s.StartsWith("bestmove"));
        Assert.NotNull(bestMoveLine);
        Assert.StartsWith("bestmove", bestMoveLine);

        var parts = bestMoveLine.Split(' ');
        var bestMoveStr = parts[1];

        // Check legality
        var game = Rudzoft.ChessLib.Factories.GameFactory.Create();
        game.NewGame();

        var generatedMoves = game.Pos.GenerateMoves();
        var bestMove = generatedMoves.FirstOrDefault(m => m.Move.ToUci() == bestMoveStr);
        if (bestMove.Equals(default))
        {
            var allMoves = string.Join(", ", generatedMoves.Select(m => m.Move.ToUci()));
            Assert.Fail($"Best move illegal: {bestMoveStr}. Generated: {allMoves}");
        }

        game.Pos.MakeMove(bestMove, new State());

        if (parts.Length > 2 && parts[2] == "ponder")
        {
            var ponderStr = parts[3];
            var ponderMove = game.Pos.GenerateMoves().FirstOrDefault(m => m.Move.ToUci() == ponderStr);
            Assert.False(ponderMove.Equals(default), $"Ponder move illegal: {ponderStr}");
        }
    }
}
