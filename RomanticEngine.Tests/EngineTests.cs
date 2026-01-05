using System.Threading;
using Xunit;
using RomanticEngine.Core;

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
        
        // Wait 5 seconds
        bool signaled = bestMoveEvent.WaitOne(5000);
        Assert.True(signaled, $"Search timed out. Errors: {errors}");
    }
    
    [Fact]
    public void Test_Search_OutputsInfo()
    {
        var engine = new Engine();
        var infoReceived = new ManualResetEvent(false);
        engine.OnInfo += (info) => 
        {
            if (info.StartsWith("depth")) infoReceived.Set();
        };
        
        engine.SetPosition("startpos", null);
        engine.Go(new SearchLimits { Depth = 2 });
        
        Assert.True(infoReceived.WaitOne(2000), "Did not receive any depth info string.");
    }
}
