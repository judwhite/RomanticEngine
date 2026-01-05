using RomanticEngine.Core;

namespace RomanticEngine;

internal static class Program
{
    private static void Main()
    {
        var engine = new Engine();
        var uciLoop = new UciLoop(engine);
        uciLoop.Run();
    }
}
