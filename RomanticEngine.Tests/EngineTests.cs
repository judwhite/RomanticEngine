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
    public void Test_SetOption_UpdatesConfiguration()
    {
        var engine = new Engine();
        
        // Test toggles
        engine.SetOption("EnableKingSafety", "false");
        Assert.False(Configuration.Evaluation.EnableKingSafety);
        
        engine.SetOption("EnableKingSafety", "true");
        Assert.True(Configuration.Evaluation.EnableKingSafety);
        
         // Test weights
        engine.SetOption("MaterialWeight", "50");
        Assert.Equal(50, Configuration.Evaluation.MaterialWeight);
    }

    [Fact]
    public void Test_SetOption_StandardOptions()
    {
        var engine = new Engine();
        
        // Threads
        engine.SetOption("Threads", "4");
        Assert.Equal(4, Configuration.Standard.Threads);

        // Move Overhead
        engine.SetOption("Move Overhead", "500");
        Assert.Equal(500, Configuration.Standard.MoveOverhead);

        // SyzygyPath (Just storage as requested)
        var path = "/path/to/tablebases";
        engine.SetOption("SyzygyPath", path);
        Assert.Equal(path, Configuration.Standard.SyzygyPath);
        
        // Ponder
        engine.SetOption("Ponder", "true");
        Assert.True(Configuration.Standard.Ponder);
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
        Assert.StartsWith("bestmove", bestMoveString);
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
        bool signaled = bestMoveEvent.WaitOne(150);
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
        bool signaled = bestMoveEvent.WaitOne(600);
        Assert.True(signaled, $"Search did not finish within 600ms (MoveTime 500). Errors: {errors}");
        Assert.True(errors.Length == 0, $"Search crashed with errors: {errors}");
    }
    
    [Fact]
    public void Test_Uci_Output_Correctness()
    {
        var engine = new Engine();
        var outputs = new List<string>();
        // Use UciAdapter with a list capture
        var adapter = new UciAdapter(engine, outputs.Add);
        
        // Simulate "uci" command
        adapter.ReceiveCommand("uci");
        
        // Verify output
        Assert.Contains("id name RomanticEngine 1.0", outputs);
        Assert.Contains("id author Jud White", outputs);
        Assert.Contains("uciok", outputs);
        
        // Options check (partial or exact strings)
        Assert.Contains(outputs, s => s.StartsWith("option name Hash") && s.Contains("default 16"));
        Assert.Contains(outputs, s => s.StartsWith("option name Threads") && s.Contains("min 1"));
        Assert.Contains(outputs, s => s.StartsWith("option name Debug Log File") && s.Contains("string"));
        
        // Count total options (Standard 8 + Custom 6 = 14) + 2 IDs + 1 uciok = 17 lines
        Assert.Equal(17, outputs.Count);
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
        string bestMoveLine = "";
        var completeEvent = new ManualResetEvent(false);
        
        engine.OnBestMove += (m) => 
        {
            bestMoveLine = m;
            completeEvent.Set();
        };
        
        engine.SetPosition("startpos", null);
        engine.Go(new SearchLimits { Depth = 4 });
        
        Assert.True(completeEvent.WaitOne(5000));
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
