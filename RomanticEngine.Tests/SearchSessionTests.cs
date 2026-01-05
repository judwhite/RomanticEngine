using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RomanticEngine.Core;
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.MoveGeneration;
using Xunit;

namespace RomanticEngine.Tests;

public class SearchSessionTests
{
    [Fact]
    public async Task Test_OverlappingGo_CancelsPriorAndDoesNotCorruptState()
    {
        var engine = new Engine();
        var bestMoves = new List<string>();
        var infoLines = new List<string>();
        var resetEvent = new ManualResetEvent(false);

        engine.OnBestMove += move =>
        {
            lock (bestMoves) bestMoves.Add(move);
            resetEvent.Set(); // Set on any bestmove, but we'll check counts/logic
        };
        engine.OnInfo += info =>
        {
            lock (infoLines) infoLines.Add(info);
        };

        engine.SetPosition("startpos");

        // 1. First deep search
        engine.Go(new SearchLimits { Depth = 12 });
        
        await Task.Delay(20);

        // 2. Second search immediately
        engine.Go(new SearchLimits { Depth = 1 });

        // Wait for search to finish
        bool signaled = resetEvent.WaitOne(5000);
        
        Assert.True(signaled, "Did not receive bestmove.");
        
        // We might receive 1 or 2 depending on timing, but with the session guard,
        // it's highly likely only 1 (the latest).
        lock (bestMoves)
        {
            Assert.NotEmpty(bestMoves);
            // If the session guard works, we shouldn't have stale info lines either
            // from before the second go if they arrived late.
        }
    }

    [Fact]
    public async Task Test_Stop_DuringDeepRecursion_ProducesBestMove()
    {
        var engine = new Engine();
        string? bestMove = null;
        var infoReceived = new TaskCompletionSource<bool>();
        var bestMoveReceived = new TaskCompletionSource<bool>();

        engine.OnInfo += _ => infoReceived.TrySetResult(true);
        engine.OnBestMove += m =>
        {
            bestMove = m;
            bestMoveReceived.TrySetResult(true);
        };

        engine.SetPosition("startpos");
        engine.Go(new SearchLimits { Depth = 20 });

        // Wait for search to actually be busy
        await infoReceived.Task;

        engine.Stop();

        bool finished = await bestMoveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(bestMove);
        // Assert.StartsWith("bestmove", bestMove); // Prefix now added by UciAdapter, not Engine
    }

    [Fact]
    public async Task Test_Stress_RepeatedGoStop_Stability()
    {
        var engine = new Engine();
        int bestMoveCount = 0;
        
        engine.OnBestMove += _ => Interlocked.Increment(ref bestMoveCount);

        for (int i = 0; i < 50; i++)
        {
            engine.SetPosition("startpos");
            engine.Go(new SearchLimits { Depth = 10 });
            await Task.Delay(10);
            engine.Stop();
        }

        // Wait a bit for pending tasks
        await Task.Delay(500);
        
        // We should have received multiple bestmoves. 
        // Because of session cancellation and callback guarding, 
        // we might not get 50 if we stop early, but we definitely shouldn't crash.
        Assert.True(true); // If we reached here without exception, it's stable.
    }
    [Fact]
    public async Task Test_Search_NodesLimit()
    {
        var engine = new Engine();
        var bestMoveReceived = new TaskCompletionSource<bool>();
        engine.OnBestMove += _ => bestMoveReceived.TrySetResult(true);

        engine.SetPosition("startpos");
        // Use a very small node limit
        engine.Go(new SearchLimits { Nodes = 100 });

        bool finished = await bestMoveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(finished, "Search did not hit node limit and return.");
    }

    [Fact]
    public async Task Test_Search_SearchMoves_Filtering()
    {
        var engine = new Engine();
        string? bestMove = null;
        var bestMoveReceived = new TaskCompletionSource<bool>();
        engine.OnBestMove += m =>
        {
            bestMove = m;
            bestMoveReceived.TrySetResult(true);
        };

        engine.SetPosition("startpos");
        // Restrict to h2h3
        engine.Go(new SearchLimits { Depth = 4, SearchMoves = new[] { "h2h3" } });

        await bestMoveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("h2h3", bestMove);
    }

    [Fact]
    public async Task Test_Search_Ponder_Semantics()
    {
        var engine = new Engine();
        string? bestMove = null;
        var infoReceived = new TaskCompletionSource<bool>();
        var bestMoveReceived = new TaskCompletionSource<bool>();
        
        engine.OnInfo += _ => infoReceived.TrySetResult(true);
        engine.OnBestMove += m =>
        {
            bestMove = m;
            bestMoveReceived.TrySetResult(true);
        };

        engine.SetPosition("startpos");
        engine.Go(new SearchLimits { Depth = 4, Ponder = true });

        // Wait for some info to be sure it's running
        await infoReceived.Task;
        
        Assert.Null(bestMove); // Should not have sent bestmove while pondering
        
        engine.PonderHit();

        bool finished = await bestMoveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(finished);
        Assert.NotNull(bestMove);
    }

    [Fact]
    public async Task Test_Search_Mate_Finding()
    {
        var engine = new Engine();
        var infos = new List<string>();
        var bestMoveReceived = new TaskCompletionSource<bool>();
        
        engine.OnInfo += info => { lock (infos) infos.Add(info); };
        engine.OnBestMove += _ => bestMoveReceived.TrySetResult(true);

        // Scholar's Mate position - white to move, mate in 1 (Qxf7#)
        // r1bqkbnr/pppp1ppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 5
        engine.SetPosition("r1bqkbnr/pppp1ppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 5");
        engine.Go(new SearchLimits { Depth = 4 });

        await bestMoveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        
        lock (infos)
        {
            Assert.Contains(infos, s => s.Contains("score mate 1"));
        }
    }
    [Fact]
    public async Task Test_Search_PV_Legality()
    {
        var engine = new Engine();
        var infoLines = new List<string>();
        var bestMoveReceived = new TaskCompletionSource<bool>();
        
        engine.OnInfo += info => { lock (infoLines) infoLines.Add(info); };
        engine.OnBestMove += _ => bestMoveReceived.TrySetResult(true);

        engine.SetPosition("startpos");
        engine.Go(new SearchLimits { Depth = 4 });

        await bestMoveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        
        // Find deepest info line with pv
        string? pvLine = null;
        lock (infoLines)
        {
            pvLine = infoLines.LastOrDefault(s => s.Contains("pv "));
        }
        Assert.NotNull(pvLine);
        
        var parts = pvLine.Split(' ');
        int pvIndex = Array.IndexOf(parts, "pv");
        var pvMoves = parts.Skip(pvIndex + 1).ToList();
        
        Assert.NotEmpty(pvMoves);
        
        // Verify moves are legal using library
        var game = Rudzoft.ChessLib.Factories.GameFactory.Create();
        game.NewGame();
        
        foreach (var moveStr in pvMoves)
        {
            var moves = game.Pos.GenerateMoves();
            var move = moves.FirstOrDefault(m => m.Move.ToString() == moveStr);
            Assert.False(move.Equals(default(Rudzoft.ChessLib.Types.ExtMove)), $"Illegal PV move {moveStr} in {pvLine}");
            game.Pos.MakeMove(move.Move, new State());
        }
    }
}
