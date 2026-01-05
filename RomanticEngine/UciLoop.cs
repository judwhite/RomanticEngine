using RomanticEngine.Core;

namespace RomanticEngine;

public class UciLoop(IEngine engine)
{
    private readonly UciAdapter _adapter = new(engine, Console.WriteLine);

    public void Run()
    {
        while (Console.ReadLine() is { } line)
        {
            if (line.Trim().Equals("quit", StringComparison.InvariantCultureIgnoreCase))
                break;
            _adapter.ReceiveCommand(line);
        }
    }
}
