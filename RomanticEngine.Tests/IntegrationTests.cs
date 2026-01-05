using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RomanticEngine.Core;
using Xunit;

namespace RomanticEngine.Tests;

public class IntegrationTests
{
    [Fact]
    public void Test_Minimal_Command_Sequence_EndToEnd()
    {
        var engine = new Engine();
        var outputs = new List<string>();
        var lockObj = new object();
        var adapter = new UciAdapter(engine, s => { lock (lockObj) outputs.Add(s); });
        
        var bestMoveEvent = new ManualResetEvent(false);
        engine.OnBestMove += _ => bestMoveEvent.Set();

        // 1. uci
        adapter.ReceiveCommand("uci");
        lock (lockObj)
        {
            Assert.Contains(outputs, s => s.Contains("id name"));
            Assert.Contains(outputs, s => s.Contains("uciok"));
        }

        // 2. isready
        outputs.Clear();
        adapter.ReceiveCommand("isready");
        lock (lockObj)
        {
            Assert.Contains("readyok", outputs);
        }

        // 3. position + go
        outputs.Clear();
        adapter.ReceiveCommand("position startpos");
        adapter.ReceiveCommand("go depth 2");

        Assert.True(bestMoveEvent.WaitOne(5000), "Search did not complete depth 2.");
        
        lock (lockObj)
        {
            Assert.Contains(outputs, s => s.StartsWith("info depth 1"));
            Assert.Contains(outputs, s => s.StartsWith("info depth 2"));
            Assert.Contains(outputs, s => s.StartsWith("bestmove"));
            
            // Exactly one bestmove
            Assert.Single(outputs, s => s.StartsWith("bestmove"));
        }
    }
}
