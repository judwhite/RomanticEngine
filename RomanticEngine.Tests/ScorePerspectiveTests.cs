namespace RomanticEngine.Tests;

public class ScorePerspectiveTests
{
    [Fact]
    public void Test_Score_Perspective_Winning_Black()
    {
        // Black to move, winning (up a Knight after Bxg4 Nxd5 Nxd5). Score should be positive.
        const string fen = "rnb1kb1r/ppp1pppp/5n2/3q4/6Q1/2N5/PPPP1PPP/R1B1KBNR b KQkq - 3 4";

        using var harness = new UciHarness();
        harness.Send($"position fen {fen}");
        harness.Send("go depth 4");

        harness.WaitForLine(s => s.StartsWith("bestmove"));

        var infos = harness.AllHistory
            .Where(s => s.StartsWith("info depth") && s.Contains("score cp") && ParseDepth(s) >= 2)
            .ToList();

        Assert.NotEmpty(infos);

        foreach (var info in infos)
        {
            int cp = ParseCp(info);
            // Black is winning, so score matches black's perspective -> Positive
            Assert.True(cp > 0, $"Expected positive score for winning black, got {cp} in line: {info}");
        }
    }

    [Fact]
    public void Test_Score_Perspective_Losing_Black()
    {
        // Black to move, losing (down a Queen). Score should be negative.
        const string fen = "rnb1kb1r/ppp1pppp/5n2/3N4/8/8/PPPP1PPP/R1BQKBNR b KQkq - 0 4";

        using var harness = new UciHarness();
        harness.Send($"position fen {fen}");
        harness.Send("go depth 4");

        harness.WaitForLine(s => s.StartsWith("bestmove"));

        var infos = harness.AllHistory
            .Where(s => s.StartsWith("info depth") && s.Contains("score cp") && ParseDepth(s) >= 2)
            .ToList();

        Assert.NotEmpty(infos);

        foreach (var info in infos)
        {
            int cp = ParseCp(info);
            // Black is losing, so score matches black's perspective -> Negative
            Assert.True(cp < 0, $"Expected negative score for losing black, got {cp} in line: {info}");
        }
    }

    private static int ParseDepth(string info)
    {
        // Format: ... depth 4 ...
        var parts = info.Split(' ');
        int index = Array.IndexOf(parts, "depth");
        if (index != -1 && index + 1 < parts.Length)
        {
            return int.Parse(parts[index + 1]);
        }
        throw new Exception($"Could not parse depth from {info}");
    }

    private static int ParseCp(string info)
    {
        // Format: ... score cp 123 ...
        var parts = info.Split(' ');
        int index = Array.IndexOf(parts, "cp");
        if (index != -1 && index + 1 < parts.Length)
        {
            return int.Parse(parts[index + 1]);
        }
        throw new Exception($"Could not parse cp from {info}");
    }
}
