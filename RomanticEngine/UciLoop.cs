using System;
using System.Collections.Generic;
using System.Linq;
using RomanticEngine.Core;

namespace RomanticEngine;

public class UciLoop
{
    private readonly UciAdapter _adapter;

    public UciLoop(IEngine engine)
    {
        _adapter = new UciAdapter(engine, Console.WriteLine);
    }

    public void Run()
    {
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            if (line.Trim().ToLowerInvariant() == "quit") break;
            _adapter.ReceiveCommand(line);
        }
    }
}
