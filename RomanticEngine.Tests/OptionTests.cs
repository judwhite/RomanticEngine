using RomanticEngine.Core;

namespace RomanticEngine.Tests;

public class OptionTests
{
    private class FakeSystemInfo : ISystemInfo
    {
        public int MaxThreads => 28;
        public int MaxHashMb => 120395;
    }

    [Fact]
    public void Test_Uci_Options_Golden_Output()
    {
        var si = new FakeSystemInfo();
        var engine = new Engine(si);
        var outputs = new List<string>();
        var adapter = new UciAdapter(engine, outputs.Add);

        adapter.ReceiveCommand("uci");

        // Verify exact strings for Threads and Hash using the fake limits
        Assert.Contains("option name Threads type spin default 1 min 1 max 28", outputs);
        Assert.Contains("option name Hash type spin default 16 min 1 max 120395", outputs);

        // Verify button
        Assert.Contains("option name Clear Hash type button", outputs);
    }

    [Fact]
    public void Test_SetOption_Validation()
    {
        var si = new FakeSystemInfo();
        var engine = new Engine(si);
        var infos = new List<string>();
        engine.OnInfo += infos.Add;

        // Valid spin
        engine.SetOption("Threads", "4");
        Assert.Equal(4, engine.Config.Standard.Threads);

        // Invalid spin (above max)
        infos.Clear();
        engine.SetOption("Threads", "99");
        Assert.Equal(4, engine.Config.Standard.Threads); // Should not have changed
        Assert.Contains(infos, s => s.Contains("invalid Threads value"));

        // Invalid check
        infos.Clear();
        engine.SetOption("Ponder", "maybe");
        Assert.Contains(infos, s => s.Contains("invalid Ponder value"));

        // Button action
        infos.Clear();
        engine.SetOption("Clear Hash", ""); // value ignored for button
        Assert.Contains(infos, s => s.Contains("cleared hash"));
    }

    [Fact]
    public void Test_Option_CaseInsensitivity()
    {
        var engine = new Engine();
        engine.SetOption("hAsH", "64");
        Assert.Equal(64, engine.Config.Standard.Hash);
    }

    [Fact]
    public void Test_SetOption_MultiPV_NotImplemented_ClampsToOne()
    {
        var si = new FakeSystemInfo();
        var engine = new Engine(si);
        var infos = new List<string>();
        engine.OnInfo += infos.Add;

        engine.SetOption("MultiPV", "3");

        // If we tell the client "using 1 for MultiPV", then the effective stored value should match.
        Assert.Contains(infos, s => s.Contains("MultiPV > 1 not implemented"));
        Assert.Equal(1, engine.Config.Standard.MultiPV);
    }
}
