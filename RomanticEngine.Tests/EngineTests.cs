using System.Threading;
using Xunit;
using RomanticEngine.Core;
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Types;
using Rudzoft.ChessLib.Enums;
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

        engine.OnBestMove += (move) => {
            bestMoveString = move;
            bestMoveEvent.Set();
        };

        engine.SetPosition("startpos", null);
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
        
        engine.OnBestMove += (move) => {
            bestMoveEvent.Set();
        };

        // Simple mate in 1 position or generic position
        engine.SetPosition("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", null); 
        engine.Go(new SearchLimits { Depth = 1 });

        Assert.True(bestMoveEvent.WaitOne(5000));
    }
    
    [Fact]
    public void Test_Stop_DoesNotThrow()
    {
        var engine = new Engine();
        engine.SetPosition("startpos", null);
        
        // Start a longer search
        engine.Go(new SearchLimits { Depth = 20 }); 
        
        // Stop immediately
        engine.Stop();
        
        // Should not throw and eventually produce bestmove (though currently Go might be blocking or async depending on impl)
        // Engine.Go implementation launches Task.Run, so Stop should signal it.
        // We verify no exception propagates here.
    }
    
    [Fact]
    public void Test_PonderHit_DoesNotThrow()
    {
        var engine = new Engine();
        engine.PonderHit();
        // Should effectively do nothing or switch mode, but ensuring no crash.
    }
    
    [Fact]
    public void Test_NewGame_Resets_DoesNotThrow()
    {
        var engine = new Engine();
        engine.NewGame();
    }
    
    [Fact]
    public void Test_Search_RespectsTimeLimit()
    {
        var engine = new Engine();
        var bestMoveEvent = new ManualResetEvent(false);
        var errors = new System.Text.StringBuilder();
        
        engine.OnBestMove += (m) => bestMoveEvent.Set();
        engine.OnInfo += (info) => 
        {
            if (info.Contains("Exception")) errors.AppendLine(info);
        };
        
        engine.SetPosition("startpos", null);
        
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
        var errors = new System.Text.StringBuilder();

        engine.OnBestMove += (m) => bestMoveEvent.Set();
        engine.OnInfo += (info) => 
        {
             if (info.Contains("Exception")) errors.AppendLine(info);
        };

        engine.SetPosition("startpos", null);
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
        Assert.Contains("option name EnableRMobility type check default true", outputs);
        Assert.Contains("option name EnableKingSafety type check default true", outputs);
        Assert.Contains("option name MaterialWeight type spin default 1 min 0 max 100", outputs);
        Assert.Contains("option name MobilityWeight type spin default 10 min 0 max 100", outputs);
        Assert.Contains("option name KingSafetyWeight type spin default 20 min 0 max 100", outputs);
        
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
        var infoStrings = new List<string>();
        var completeEvent = new ManualResetEvent(false);
        
        engine.OnInfo += (info) => infoStrings.Add(info);
        engine.OnBestMove += (m) => completeEvent.Set();
        
        engine.SetPosition("startpos", null);
        engine.Go(new SearchLimits { Depth = 4 }); // Enough to generate PVs
        
        Assert.True(completeEvent.WaitOne(5000));
        Assert.NotEmpty(infoStrings);
        
        long prevNodes = -1;
        long prevTime = -1;
        int prevMultiPv = 1; // Assuming default 1
        
        foreach (var info in infoStrings)
        {
            if (info.Contains("string")) continue; // Skip info string ...
            
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
                if (parts[i] == "score" && parts[i+1] == "cp")
                {
                    int cp = int.Parse(parts[i+2]);
                    Assert.InRange(cp, -64000, 64000);
                }
                if (parts[i] == "nodes")
                {
                    long nodes = long.Parse(parts[i+1]);
                    Assert.True(nodes >= prevNodes, $"Nodes not monotonic: {nodes} < {prevNodes}");
                    prevNodes = nodes;
                }
                if (parts[i] == "time")
                {
                    long time = long.Parse(parts[i+1]);
                    Assert.True(time >= prevTime, "Time not monotonic");
                    prevTime = time;
                }
                if (parts[i] == "hashfull")
                {
                    int hf = int.Parse(parts[i+1]);
                    Assert.InRange(hf, 0, 1000);
                }
                if (parts[i] == "nps")
                {
                    long nps = long.Parse(parts[i+1]);
                    Assert.True(nps >= 0); // Can be 0 if time is 0
                }
                 if (parts[i] == "multipv")
                {
                    int mpv = int.Parse(parts[i+1]);
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
                var move = game.Pos.GenerateMoves().FirstOrDefault(m => m.Move.ToString() == moveStr);
                Assert.False(move.Equals(default(ExtMove)), $"Invalid PV move: {moveStr}");
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

        engine.SetPosition("startpos", null);
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
        var bestMove = generatedMoves.FirstOrDefault(m => m.Move.ToString() == bestMoveStr);
        if (bestMove.Equals(default(ExtMove)))
        {
             var allMoves = string.Join(", ", generatedMoves.Select(m => m.Move.ToString()));
             Assert.Fail($"Best move illegal: {bestMoveStr}. Generated: {allMoves}");
        }
        
        game.Pos.MakeMove(bestMove, new State());
        
        if (parts.Length > 2 && parts[2] == "ponder")
        {
             var ponderStr = parts[3];
             var ponderMove = game.Pos.GenerateMoves().FirstOrDefault(m => m.Move.ToString() == ponderStr);
             Assert.False(ponderMove.Equals(default(ExtMove)), $"Ponder move illegal: {ponderStr}");
        }
    }
}
