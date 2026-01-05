namespace RomanticEngine.Core;

public class UciAdapter
{
    private const string EngineId = "RomanticEngine";
    private const string Version = "1.0";
    private const string Author = "Jud White";

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
        if (string.IsNullOrWhiteSpace(command))
            return;

        _engine.Log("IN ", command);

        var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = tokens[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "uci":
                    SendOutput($"id name {EngineId} {Version}");
                    SendOutput($"id author {Author}");
                    foreach (var opt in _engine.Options)
                        SendOutput(opt.ToString());
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
            if (tokens[i] == "name")
                nameIndex = i;
            else if (tokens[i] == "value")
                valueIndex = i;
        }

        if (nameIndex == -1 || nameIndex + 1 >= tokens.Length)
            return;

        string name;
        string value = "";

        if (valueIndex != -1 && valueIndex > nameIndex)
        {
            name = string.Join(" ", tokens.Skip(nameIndex + 1).Take(valueIndex - nameIndex - 1));
            if (valueIndex + 1 < tokens.Length)
                value = string.Join(" ", tokens.Skip(valueIndex + 1));
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
        if (tokens.Length < 2)
            return;

        int movesIndex = Array.FindIndex(tokens, 1, t => t == "moves");

        string fen;
        string[]? moves = null;

        switch (tokens[1])
        {
            case "startpos":
                fen = "startpos";
                break;
            case "fen":
                // FEN requires exactly 6 fields:
                // 1. Piece placement
                // 2. Side to move
                // 3. Castling ability
                // 4. En passant square
                // 5. Halfmove clock
                // 6. Fullmove counter

                int fenFieldsCount = 6;
                int fenStartIndex = 2;
                int end = movesIndex == -1 ? tokens.Length : movesIndex;

                if (end - fenStartIndex < fenFieldsCount)
                {
                    SendOutput($"info string error: FEN requires {fenFieldsCount} fields.");
                    return;
                }

                fen = string.Join(" ", tokens.Skip(fenStartIndex).Take(fenFieldsCount));
                break;
            default:
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
                case "infinite":
                    limits.Infinite = true;
                    break;
                case "ponder":
                    limits.Ponder = true;
                    break;
                case "wtime":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var wtime))
                        limits.WhiteTime = wtime;
                    break;
                case "btime":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var btime))
                        limits.BlackTime = btime;
                    break;
                case "winc":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var winc))
                        limits.WhiteIncrement = winc;
                    break;
                case "binc":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var binc))
                        limits.BlackIncrement = binc;
                    break;
                case "movestogo":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var movesToGo))
                        limits.MovesToGo = movesToGo;
                    break;
                case "depth":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var depth))
                        limits.Depth = depth;
                    break;
                case "nodes":
                    if (++i < tokens.Length && long.TryParse(tokens[i], out var nodes))
                        limits.Nodes = nodes;
                    break;
                case "movetime":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var moveTime))
                        limits.MoveTime = moveTime;
                    break;
                case "mate":
                    if (++i < tokens.Length && int.TryParse(tokens[i], out var mate))
                        limits.Mate = mate;
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
