using RomanticEngine.Core;
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.MoveGeneration;

namespace RomanticEngine.Tests;

public class SearchSessionTests
{
    [Fact]
    public async Task Test_OverlappingGo_CancelsPriorAndDoesNotLeakStaleOutput()
    {
        var engine = new Engine();
        var bestMoves = new List<string>();
        var infoLines = new List<string>();
        var bestMoveEvent = new ManualResetEvent(false);

        engine.OnBestMove += move =>
        {
            lock (bestMoves) bestMoves.Add(move);
            bestMoveEvent.Set();
        };

        engine.OnInfo += info =>
        {
            lock (infoLines) infoLines.Add(info);
        };

        engine.SetPosition("startpos");

        // 1) Kick off a deep search.
        engine.Go(new SearchLimits { Depth = 12 });

        // 2) Immediately start a second search. This must cancel/suppress the first session.
        await Task.Delay(20);
        engine.Go(new SearchLimits { Depth = 1 });

        Assert.True(bestMoveEvent.WaitOne(5000), "Did not receive bestmove from the latest session.");

        // Give a short window for any stale output to arrive.
        await Task.Delay(200);

        lock (bestMoves)
        {
            Assert.Single(bestMoves);
            Assert.False(string.IsNullOrWhiteSpace(bestMoves[0]));
            Assert.True(bestMoves[0].Length >= 4, $"Unexpected bestmove payload: '{bestMoves[0]}'");
        }

        lock (infoLines)
        {
            var depthLines = infoLines.Where(l => l.StartsWith("depth ")).ToList();
            Assert.NotEmpty(depthLines);

            // Since the second search was depth=1, we should not see leaked depth>=2 lines.
            Assert.Contains(depthLines, l => l.StartsWith("depth 1 "));
            Assert.DoesNotContain(depthLines, l => l.StartsWith("depth 2 "));
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

        // Wait for search to actually be busy.
        await infoReceived.Task;

        engine.Stop();

        bool finished = await bestMoveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(finished, "Expected a bestmove after stop.");
        Assert.NotNull(bestMove);
        Assert.False(string.IsNullOrWhiteSpace(bestMove));
        Assert.True(bestMove!.Length >= 4, $"Unexpected bestmove payload: '{bestMove}'");
    }

    [Fact]
    public async Task Test_Stress_RepeatedGoStop_Stability()
    {
        var engine = new Engine();
        int bestMoveCount = 0;
        var exceptionInfos = new List<string>();

        engine.OnBestMove += _ => Interlocked.Increment(ref bestMoveCount);
        engine.OnInfo += info =>
        {
            if (info.Contains("Exception", StringComparison.OrdinalIgnoreCase))
            {
                lock (exceptionInfos) exceptionInfos.Add(info);
            }
        };

        for (int i = 0; i < 50; i++)
        {
            engine.SetPosition("startpos");
            engine.Go(new SearchLimits { Depth = 10 });
            await Task.Delay(10);
            engine.Stop();
        }

        // Allow pending tasks to settle.
        await Task.Delay(750);

        Assert.True(bestMoveCount > 0, "Expected at least one bestmove across repeated go/stop cycles.");
        lock (exceptionInfos)
        {
            Assert.Empty(exceptionInfos);
        }
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
        engine.Go(new SearchLimits { Depth = 4, SearchMoves = ["h2h3"] });

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
        engine.Stop();

        bool finished = await bestMoveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(finished);
        Assert.NotNull(bestMove);
    }

    [Fact]
    public async Task Test_PonderHit_AfterSearchCompletes_DoesNotLoseBestMove_WhenStopped()
    {
        // This is intentionally designed to catch a subtle bug:
        // if a ponder search finishes (depth/nodes reached) while bestmove is suppressed,
        // then a later ponderhit/stop must still result in a bestmove being emitted.
        var engine = new Engine();

        var depth1Info = new TaskCompletionSource<bool>();
        var bestMoveReceived = new TaskCompletionSource<string>();

        engine.OnBestMove += m => bestMoveReceived.TrySetResult(m);
        engine.OnInfo += info =>
        {
            if (info.StartsWith("depth 1 ")) depth1Info.TrySetResult(true);
        };

        engine.SetPosition("startpos");
        engine.Go(new SearchLimits { Depth = 1, Nodes = 1, Ponder = true });

        await depth1Info.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Give a buffer to allow the depth-1 (and node-limited) search to finish while still pondering.
        await Task.Delay(200);

        Assert.False(bestMoveReceived.Task.IsCompleted, "bestmove should be suppressed while pondering.");

        engine.PonderHit();
        engine.Stop();

        var bm = await bestMoveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrWhiteSpace(bm));
        Assert.True(bm.Length >= 4);
    }

    [Fact]
    public async Task Test_Search_Mate_Finding()
    {
        var engine = new Engine();
        var infos = new List<string>();
        var bestMoveReceived = new TaskCompletionSource<bool>();

        engine.OnBestMove += _ => bestMoveReceived.TrySetResult(true);
        engine.OnInfo += info =>
        {
            lock (infos) infos.Add(info);
        };

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

        engine.OnBestMove += _ => bestMoveReceived.TrySetResult(true);
        engine.OnInfo += info =>
        {
            lock (infoLines) infoLines.Add(info);
        };

        engine.SetPosition("startpos");
        engine.Go(new SearchLimits { Depth = 4 });

        await bestMoveReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Find deepest info line with pv
        string? pvLine;
        lock (infoLines)
        {
            pvLine = infoLines.LastOrDefault(s => s.Contains("pv "));
        }

        Assert.NotNull(pvLine);

        var parts = pvLine.Split(' ');
        int pvIndex = Array.IndexOf(parts, "pv");
        var pvMoves = parts.Skip(pvIndex + 1).ToList();

        Assert.NotEmpty(pvMoves);

        // Verify moves are legal using Rudzoft
        var game = Rudzoft.ChessLib.Factories.GameFactory.Create();
        game.NewGame();

        foreach (var moveStr in pvMoves)
        {
            var moves = game.Pos.GenerateMoves();
            var move = moves.FirstOrDefault(m => m.Move.ToString() == moveStr);
            Assert.False(move.Equals(default), $"Illegal PV move {moveStr} in {pvLine}");
            game.Pos.MakeMove(move.Move, new State());
        }
    }
}
