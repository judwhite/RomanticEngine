using System;
using System.Collections.Generic;

namespace RomanticEngine.Core;

public interface IEngine
{
    event Action<string> OnInfo;
    event Action<string> OnBestMove;

    IReadOnlyList<UciOption> Options { get; }

    void NewGame();
    void SetPosition(string fen, string[]? moves = null);
    void Go(SearchLimits limits);
    void Stop();
    void PonderHit();
    void SetOption(string name, string value);
    void SetDebug(bool enabled);
    void Log(string direction, string message);
}
