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
        _engine.OnInfo += msg => SendOutput($"info {msg}");
        _engine.OnBestMove += msg => SendOutput($"bestmove {msg}");
    }

    private void SendOutput(string message)
    {
        _engine.Log("OUT", message);
        _outputWriter(message);
    }

    public void ReceiveCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        _engine.Log("IN ", command);

        var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = tokens[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "uci":
                    SendOutput("id name RomanticEngine 1.0");
                    SendOutput("id author Jud White");
                    foreach (var opt in _engine.Options)
                    {
                        SendOutput(opt.ToString());
                    }

                    SendOutput("uciok");
                    break;

                case "isready":
                    SendOutput("readyok");
                    break;

                case "debug":
                    if (tokens.Length >= 2)
                        _engine.SetDebug(tokens[1].Equals("on", StringComparison.OrdinalIgnoreCase));
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
        catch (Exception ex)
        {
            SendOutput($"info string error processing command '{command}': {ex.Message}");
        }
    }

    private void ParseSetOption(string[] tokens)
    {
        // setoption name <id> [value <x>]
        int nameIndex = -1;
        int valueIndex = -1;

        for (int i = 1; i < tokens.Length; i++)
        {
            if (tokens[i] == "name") nameIndex = i;
            if (tokens[i] == "value") valueIndex = i;
        }

        if (nameIndex == -1 || nameIndex + 1 >= tokens.Length) return;

        string name;
        string value = "";

        if (valueIndex != -1 && valueIndex > nameIndex)
        {
            name = string.Join(" ", tokens.Skip(nameIndex + 1).Take(valueIndex - nameIndex - 1));
            if (valueIndex + 1 < tokens.Length)
            {
                value = string.Join(" ", tokens.Skip(valueIndex + 1));
            }
        }
        else
        {
            name = string.Join(" ", tokens.Skip(nameIndex + 1));
        }

        _engine.SetOption(name.Trim(), value.Trim());
    }

    private void ParsePosition(string[] tokens)
    {
        // position [fen <fenstring> | startpos] moves <move1> ....
        if (tokens.Length < 2) return;

        int movesIndex = Array.FindIndex(tokens, 1, t => t == "moves");

        string fen;
        string[]? moves = null;

        if (tokens[1] == "startpos")
        {
            fen = "startpos";
        }
        else if (tokens[1] == "fen")
        {
            // FEN requires exactly 6 fields:
            // 1. Piece placement
            // 2. Side to move
            // 3. Castling ability
            // 4. En passant square
            // 5. Halfmove clock
            // 6. Fullmove counter

            int fenFieldsCount = 6;
            int fenStartIndex = 2;
            int end = (movesIndex == -1) ? tokens.Length : movesIndex;

            if (end - fenStartIndex < fenFieldsCount)
            {
                SendOutput($"info string error: FEN requires {fenFieldsCount} fields.");
                return;
            }

            fen = string.Join(" ", tokens.Skip(fenStartIndex).Take(fenFieldsCount));
        }
        else
        {
            return;
        }

        if (movesIndex != -1 && movesIndex + 1 < tokens.Length)
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
            var token = tokens[i].ToLowerInvariant();
            switch (token)
            {
                case "infinite": limits.Infinite = true; break;
                case "ponder": limits.Ponder = true; break;
                case "wtime":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var wt)) limits.WhiteTime = wt;
                    break;
                case "btime":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var bt)) limits.BlackTime = bt;
                    break;
                case "winc":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var wi)) limits.WhiteIncrement = wi;
                    break;
                case "binc":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var bi)) limits.BlackIncrement = bi;
                    break;
                case "movestogo":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var mtg)) limits.MovesToGo = mtg;
                    break;
                case "depth":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var d)) limits.Depth = d;
                    break;
                case "nodes":
                    if (++i < tokens.Length && long.TryParse(tokens[i], out var n)) limits.Nodes = n;
                    break;
                case "movetime":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var mt)) limits.MoveTime = mt;
                    break;
                case "mate":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var mat)) limits.Mate = mat;
                    break;
                case "searchmoves":
                    // Consume remainder
                    if (i + 1 < tokens.Length)
                    {
                        limits.SearchMoves = tokens.Skip(i + 1).ToArray();
                        i = tokens.Length; // End loop
                    }

                    break;
            }
        }

        _engine.Go(limits);
    }
}
