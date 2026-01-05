using System;
using System.Collections.Generic;
using RomanticEngine.Core;
using Xunit;

namespace RomanticEngine.Tests;

public class UciAdapterTests
{
    [Fact]
    public void Test_MalformedCommands_DoNotThrow()
    {
        var engine = new Engine();
        var outputs = new List<string>();
        var lockObj = new object();
        var adapter = new UciAdapter(engine, s => { lock (lockObj) outputs.Add(s); });

        string[] malformed = {
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
        };

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
        var fen = "r1b1k1nr/pppp1ppp/2n5/2b1p3/2B1P3/2N5/PPPP1PPP/R1BQK1NR w KQkq - 4 5";
        adapter.ReceiveCommand($"position fen {fen}");
        
        // This is hard to verify without exposing engine state, 
        // but we can at least check it doesn't log a FEN error.
        Assert.DoesNotContain(outputs, s => s.Contains("error"));
        
        // Invalid FEN (fewer than 6 fields)
        outputs.Clear();
        adapter.ReceiveCommand("position fen r1b1k1nr/pppp1ppp/2n5");
        Assert.Contains(outputs, s => s.Contains("FEN requires 6 fields"));
    }

    [Fact]
    public void Test_Position_Moves_Diagnostic()
    {
        var engine = new Engine();
        var outputs = new List<string>();
        var adapter = new UciAdapter(engine, outputs.Add);

        // Illegal move: e2e5 (not legal in startpos)
        // Wait, e2e4 is legal. e2e5 is not for white at start.
        adapter.ReceiveCommand("position startpos moves e2e5");
        
        // Verify illegal move diagnostic
        Assert.Contains(outputs, s => s.Contains("info") && s.Contains("illegal move"));
    }

    [Fact]
    public void Test_Go_SearchMoves_Parsing()
    {
        // ...
    }
}
