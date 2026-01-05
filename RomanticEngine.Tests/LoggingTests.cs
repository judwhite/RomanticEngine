using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RomanticEngine.Core;
using Xunit;

namespace RomanticEngine.Tests;

public class LoggingTests : IDisposable
{
    private readonly string _testLogFile = "test_debug.log";

    public LoggingTests()
    {
        if (File.Exists(_testLogFile)) File.Delete(_testLogFile);
    }

    public void Dispose()
    {
        if (File.Exists(_testLogFile)) File.Delete(_testLogFile);
    }

    [Fact]
    public void Test_DebugCommand_TogglesState()
    {
        var engine = new Engine();
        var infoLines = new List<string>();
        engine.OnInfo += infoLines.Add;

        var adapter = new UciAdapter(engine, _ => { });

        adapter.ReceiveCommand("debug on");
        Assert.True(engine.Config.Standard.DebugEnabled);
        Assert.Contains(infoLines, s => s.Contains("debug enabled"));

        adapter.ReceiveCommand("debug off");
        Assert.False(engine.Config.Standard.DebugEnabled);
        Assert.Contains(infoLines, s => s.Contains("debug disabled"));
    }

    [Fact]
    public void Test_LogFile_CreationAndContent()
    {
        var engine = new Engine();
        var adapter = new UciAdapter(engine, _ => { });

        // Enable logging
        engine.SetOption("Debug Log File", _testLogFile);
        
        adapter.ReceiveCommand("isready");
        
        // Wait for flush (AutoFlush is true, but file system might be lazy)
        // Actually StreamWriter with AutoFlush=true should be immediate.
        
        Assert.True(File.Exists(_testLogFile));
        var content = File.ReadAllText(_testLogFile);
        Assert.Contains("[IN ] isready", content);
        Assert.Contains("[OUT] readyok", content);

        // Disable logging
        engine.SetOption("Debug Log File", "<empty>");
        
        var lengthBefore = new FileInfo(_testLogFile).Length;
        adapter.ReceiveCommand("ucinewgame");
        var lengthAfter = new FileInfo(_testLogFile).Length;
        
        Assert.Equal(lengthBefore, lengthAfter);
    }

    [Fact]
    public void Test_ExceptionHandling_DoesNotCrashEngine()
    {
        var engine = new Engine();
        var outputs = new List<string>();
        var adapter = new UciAdapter(engine, outputs.Add);

        // Feed garbage that might cause issues if not caught
        adapter.ReceiveCommand("position fen invalid fen string more tokens");
        
        // Should have received some info string error
        Assert.Contains(outputs, s => s.Contains("info string"));
        
        // Engine should still be alive
        outputs.Clear();
        adapter.ReceiveCommand("isready");
        Assert.Contains("readyok", outputs);
    }
}
