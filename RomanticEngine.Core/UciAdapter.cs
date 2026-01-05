using System;
using System.Linq;

namespace RomanticEngine.Core;

public class UciAdapter
{
    private readonly IEngine _engine;
    private readonly Action<string> _outputWriter;

    public UciAdapter(IEngine engine, Action<string> outputWriter)
    {
        _engine = engine;
        _outputWriter = outputWriter;

        // Hook up engine events to output
        if (_engine is Engine concreteEngine)
        {
            concreteEngine.OnInfo += msg => _outputWriter($"info {msg}");
            concreteEngine.OnBestMove += msg => _outputWriter(msg);
        }
    }

    public void Loop()
    {
        // For scenarios where UciAdapter drives the loop from a reader?
        // Or just expose ProcessCommand.
    }

    public void ReceiveCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = tokens[0].ToLowerInvariant();

        switch (cmd)
        {
            case "uci":
                _outputWriter("id name RomanticEngine 1.0");
                _outputWriter("id author Jud White");
                foreach (var opt in _engine.Options)
                {
                   _outputWriter(opt.ToString()); 
                }
                _outputWriter("uciok");
                break;

            case "isready":
                _outputWriter("readyok");
                break;

            case "setoption":
                ParseSetOption(tokens);
                break;

            case "ucinewgame":
                _engine.NewGame();
                break;

            case "position":
                ParsePosition(tokens);
                break;

            case "go":
                ParseGo(tokens);
                break;

            case "stop":
                _engine.Stop();
                break;

            case "ponderhit":
                _engine.PonderHit();
                break;

            case "quit":
                // Handled by caller usually, but we can have an event or just ignore
                break;

            default:
                // Unknown command
                break;
        }
    }

    private void ParseSetOption(string[] tokens)
    {
        // setoption name <id> [value <x>]
        // tokens: setoption, name, ..., value, ...
        int nameIndex = -1;
        int valueIndex = -1;

        for (int i = 1; i < tokens.Length; i++)
        {
            if (tokens[i] == "name") nameIndex = i;
            if (tokens[i] == "value") valueIndex = i;
        }

        if (nameIndex == -1) return;

        string name;
        string value = "";

        if (valueIndex != -1 && valueIndex > nameIndex)
        {
            name = string.Join(" ", tokens.Skip(nameIndex + 1).Take(valueIndex - nameIndex - 1));
            value = string.Join(" ", tokens.Skip(valueIndex + 1));
        }
        else
        {
            name = string.Join(" ", tokens.Skip(nameIndex + 1));
        }

        _engine.SetOption(name, value);
    }

    private void ParsePosition(string[] tokens)
    {
        // position [fen <fenstring> | startpos]  moves <move1> ....
        int movesIndex = -1;
        for (int i = 1; i < tokens.Length; i++)
        {
            if (tokens[i] == "moves")
            {
                movesIndex = i;
                break;
            }
        }

        string fen = "";
        string[]? moves = null;

        if (tokens[1] == "startpos")
        {
            fen = "startpos";
        }
        else if (tokens[1] == "fen")
        {
            // Join tokens until movesIndex or end
            int end = (movesIndex == -1) ? tokens.Length : movesIndex;
            fen = string.Join(" ", tokens.Skip(2).Take(end - 2));
        }

        if (movesIndex != -1)
        {
            moves = tokens.Skip(movesIndex + 1).ToArray();
        }

        _engine.SetPosition(fen, moves);
    }

    private void ParseGo(string[] tokens)
    {
        var limits = new SearchLimits();
        for (int i = 1; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "infinite": limits.Infinite = true; break;
                case "ponder": limits.Ponder = true; break;
                case "wtime": limits.WhiteTime = int.Parse(tokens[++i]); break;
                case "btime": limits.BlackTime = int.Parse(tokens[++i]); break;
                case "winc": limits.WhiteIncrement = int.Parse(tokens[++i]); break;
                case "binc": limits.BlackIncrement = int.Parse(tokens[++i]); break;
                case "movestogo": limits.MovesToGo = int.Parse(tokens[++i]); break;
                case "depth": limits.Depth = int.Parse(tokens[++i]); break;
                case "nodes": limits.Nodes = long.Parse(tokens[++i]); break;
                case "movetime": limits.MoveTime = int.Parse(tokens[++i]); break;
                case "mate": limits.Mate = int.Parse(tokens[++i]); break;
            }
        }
        _engine.Go(limits);
    }
}
