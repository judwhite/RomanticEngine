using System;
using RomanticEngine.Core;

namespace RomanticEngine;

class Program
{
    static void Main(string[] args)
    {
        var engine = new Engine();
        var uciLoop = new UciLoop(engine);
        uciLoop.Run();
    }
}
