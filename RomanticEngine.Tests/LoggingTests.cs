using RomanticEngine.Core;

namespace RomanticEngine.Tests;

public sealed class LoggingTests : IDisposable
{
    private const string TestLogFile = "test_debug.log";

    public LoggingTests()
    {
        if (File.Exists(TestLogFile)) File.Delete(TestLogFile);
    }

    public void Dispose()
    {
        if (File.Exists(TestLogFile)) File.Delete(TestLogFile);
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
        engine.SetOption("Debug Log File", TestLogFile);

        adapter.ReceiveCommand("isready");

        // Wait for flush (AutoFlush is true, but file system might be lazy)

        Assert.True(File.Exists(TestLogFile));
        var content = File.ReadAllText(TestLogFile);
        Assert.Contains("[IN ] isready", content);
        Assert.Contains("[OUT] readyok", content);

        // Disable logging
        engine.SetOption("Debug Log File", "<empty>");

        var lengthBefore = new FileInfo(TestLogFile).Length;
        adapter.ReceiveCommand("ucinewgame");
        var lengthAfter = new FileInfo(TestLogFile).Length;

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
