using System;
using System.Collections.Generic;
using System.Linq;
using RomanticEngine.Core;

namespace RomanticEngine;

public class UciLoop
{
    private readonly IEngine _engine;

    // TODO: this class should be using `UciAdapter`, as discussed in `BUG_01.md`.
    public UciLoop(IEngine engine)
    {
        _engine = engine;
        _engine.OnInfo += info => Console.WriteLine($"info {info}");
        _engine.OnBestMove += move => Console.WriteLine(move);
    }

    public void Run()
    {
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            ProcessCommand(line);
        }
    }

    private void ProcessCommand(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var command = parts[0];

        switch (command)
        {
            case "uci":
                Console.WriteLine("id name RomanticEngine 1.0");
                Console.WriteLine("id author Jud White");
                
                foreach (var opt in _engine.Options)
                {
                    Console.WriteLine(opt.ToString());
                }

                Console.WriteLine("uciok");
                break;
            case "isready":
                Console.WriteLine("readyok");
                break;
            case "setoption":
                // setoption name <parameter> value <value>
                ParseSetOption(parts);
                break;
            case "ucinewgame":
                _engine.NewGame();
                break;
            case "position":
                ParsePosition(parts);
                break;
            case "go":
                ParseGo(parts);
                break;
            case "stop":
                _engine.Stop();
                break;
            case "ponderhit":
                _engine.PonderHit();
                break;
            case "quit":
                Environment.Exit(0);
                break;
            default:
                // Unknown command, ignore
                break;
        }
    }

    private void ParseSetOption(string[] parts)
    {
        // Example: setoption name Hash value 32
        // Name can have spaces.
        // Find "name" index and "value" index.
        int nameIndex = -1;
        int valueIndex = -1;

        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i] == "name") nameIndex = i;
            if (parts[i] == "value") valueIndex = i;
        }

        if (nameIndex != -1)
        {
            string name;
            string value = "";

            if (valueIndex != -1 && valueIndex > nameIndex)
            {
                name = string.Join(" ", parts.Skip(nameIndex + 1).Take(valueIndex - nameIndex - 1));
                value = string.Join(" ", parts.Skip(valueIndex + 1));
            }
            else
            {
                name = string.Join(" ", parts.Skip(nameIndex + 1));
            }

            _engine.SetOption(name, value);
        }
    }

    private void ParsePosition(string[] parts)
    {
        // position [fen <fenstring> | startpos] [moves <move1> ... ]
        string fen = "startpos";
        List<string> moves = new();

        int movesIndex = -1;
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i] == "moves")
            {
                movesIndex = i;
                break;
            }
        }

        if (movesIndex != -1)
        {
            // moves found
            for (int i = movesIndex + 1; i < parts.Length; i++)
            {
                moves.Add(parts[i]);
            }
        }

        // Parse FEN
        if (parts.Length > 1 && parts[1] == "fen")
        {
            int fenEnd = (movesIndex == -1) ? parts.Length : movesIndex;
            fen = string.Join(" ", parts.Skip(2).Take(fenEnd - 2));
        }
        else if (parts.Length > 1 && parts[1] == "startpos")
        {
            fen = "startpos";
        }

        _engine.SetPosition(fen, moves.ToArray());
    }

    private void ParseGo(string[] parts)
    {
        var limits = new SearchLimits();
        for (int i = 1; i < parts.Length; i++)
        {
            switch (parts[i])
            {
                case "infinite":
                    limits.Infinite = true;
                    break;
                case "ponder":
                    limits.Ponder = true;
                    break;
                case "wtime":
                    if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int wtime))
                        limits.WhiteTime = wtime;
                    break;
                case "btime":
                    if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int btime))
                        limits.BlackTime = btime;
                    break;
                case "winc":
                    if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int winc))
                        limits.WhiteIncrement = winc;
                    break;
                case "binc":
                    if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int binc))
                        limits.BlackIncrement = binc;
                    break;
                case "movetime":
                    if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int movetime))
                        limits.MoveTime = movetime;
                    break;
                case "depth":
                    if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int depth))
                        limits.Depth = depth;
                    break;
                case "nodes":
                    if (i + 1 < parts.Length && long.TryParse(parts[i + 1], out long nodes))
                        limits.Nodes = nodes;
                    break;
                case "mate":
                    if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int mate))
                        limits.Mate = mate;
                    break;
                case "movestogo":
                    if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int movestogo))
                        limits.MovesToGo = movestogo;
                    break;
            }
        }
        _engine.Go(limits);
    }
}
