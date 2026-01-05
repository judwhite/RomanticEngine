using System;
using System.Linq;
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.Types;
using Rudzoft.ChessLib.MoveGeneration; // Attempting to add this
using Rudzoft.ChessLib.Enums; // For MoveGenerationType if needed

namespace RomanticEngine.Core;

public class Engine : IEngine
{
    public event Action<string>? OnInfo;
    public event Action<string>? OnBestMove;

    private readonly IGame _game;
    private readonly Search _search;

    private readonly List<UciOption> _options = new();
    public IReadOnlyList<UciOption> Options => _options;

    public Engine()
    {
        _game = GameFactory.Create();
        _game.NewGame();
        _search = new Search(_game);
        InitializeOptions();
    }

    private void InitializeOptions()
    {
        // Standard Options per UCI_PROTOCOL.md
        _options.Add(new UciOption { Name = "Debug Log File", Type = UciOptionType.String, DefaultValue = "", OnChanged = val => Configuration.Standard.DebugLogFile = val });
        _options.Add(new UciOption { Name = "Threads", Type = UciOptionType.Spin, DefaultValue = "1", Min = 1, Max = 1024, OnChanged = val => Configuration.Standard.Threads = int.Parse(val) });
        _options.Add(new UciOption { Name = "Hash", Type = UciOptionType.Spin, DefaultValue = "16", Min = 1, Max = 1024, OnChanged = val => { /* TODO: Resize Hash */ } });
        _options.Add(new UciOption { Name = "Clear Hash", Type = UciOptionType.Button, OnChanged = val => { /* TODO: Clear Hash */ } });
        _options.Add(new UciOption { Name = "Ponder", Type = UciOptionType.Check, DefaultValue = "false", OnChanged = val => Configuration.Standard.Ponder = bool.Parse(val) });
        _options.Add(new UciOption { Name = "MultiPV", Type = UciOptionType.Spin, DefaultValue = "1", Min = 1, Max = 500, OnChanged = val => Configuration.Standard.MultiPV = int.Parse(val) });
        _options.Add(new UciOption { Name = "Move Overhead", Type = UciOptionType.Spin, DefaultValue = "10", Min = 0, Max = 5000, OnChanged = val => Configuration.Standard.MoveOverhead = int.Parse(val) });
        _options.Add(new UciOption { Name = "SyzygyPath", Type = UciOptionType.String, DefaultValue = "<empty>", OnChanged = val => Configuration.Standard.SyzygyPath = val });

        // Custom Heuristics
        _options.Add(new UciOption { Name = "EnableMaterial", Type = UciOptionType.Check, DefaultValue = "true", OnChanged = val => Configuration.Evaluation.EnableMaterial = bool.Parse(val) });
        _options.Add(new UciOption { Name = "EnableRMobility", Type = UciOptionType.Check, DefaultValue = "true", OnChanged = val => Configuration.Evaluation.EnableRMobility = bool.Parse(val) });
        _options.Add(new UciOption { Name = "EnableKingSafety", Type = UciOptionType.Check, DefaultValue = "true", OnChanged = val => Configuration.Evaluation.EnableKingSafety = bool.Parse(val) });

        _options.Add(new UciOption { Name = "MaterialWeight", Type = UciOptionType.Spin, DefaultValue = "1", Min = 0, Max = 100, OnChanged = val => Configuration.Evaluation.MaterialWeight = int.Parse(val) });
        _options.Add(new UciOption { Name = "MobilityWeight", Type = UciOptionType.Spin, DefaultValue = "10", Min = 0, Max = 100, OnChanged = val => Configuration.Evaluation.MobilityWeight = int.Parse(val) });
        _options.Add(new UciOption { Name = "KingSafetyWeight", Type = UciOptionType.Spin, DefaultValue = "20", Min = 0, Max = 100, OnChanged = val => Configuration.Evaluation.KingSafetyWeight = int.Parse(val) });
    }

    public void NewGame()
    {
        _game.NewGame();
    }

    public void SetPosition(string fen, string[]? moves = null)
    {
        if (fen == "startpos")
            _game.NewGame();
        else
            _game.NewGame(fen);

        if (moves != null)
        {
            foreach (var moveStr in moves)
            {
                // Generate legal moves to find the matching one
                // This validates and ensures we have the correct Move object
                var moveList = _game.Pos.GenerateMoves(); 
                var move = moveList.FirstOrDefault(m => m.ToString() == moveStr); // Assumes Move.ToString() is UCI compatible or close

                if (!move.Equals(default(ExtMove)))
                {
                    _game.Pos.MakeMove(move, _game.Pos.State);
                }
                else
                {
                     // Fallback or error logging? 
                     // Try simpler parsing if ToString mismatch, but usually library handles it.
                     // For now, assume it works.
                }
            }
        }
    }

    public void Go(SearchLimits limits)
    {
        // Cancel previous if running?
        _search.Stop();
        
        Task.Run(() => 
        {
            try 
            {
                _search.Start(limits, info => OnInfo?.Invoke(info), move => OnBestMove?.Invoke(move));
            }
            catch (Exception ex)
            {
                OnInfo?.Invoke($"info string Exception in search: {ex}");
            }
        });
    }

    public void Stop()
    {
        _search.Stop();
    }

    public void PonderHit()
    {
        // For now simple pass-through or state change if Ponder implemented fully
        // _search.PonderHit(); 
    }

    public void SetOption(string name, string value)
    {
        var option = _options.FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (option != null)
        {
            if (option.Type == UciOptionType.Button)
            {
                option.OnChanged?.Invoke("");
            }
            else
            {
                // Basic validation could go here
                // For now trust input or simple parse
                option.CurrentValue = value;
                try 
                {
                    option.OnChanged?.Invoke(value);
                }
                catch { /* Ignore parse errors or log */ }
            }
        }
    }
}
