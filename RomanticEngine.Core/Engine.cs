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
    private readonly PositionDriver _driver;
    
    private SearchSession? _currentSession;
    private long _nextSessionId = 1;
    private readonly object _sessionLock = new();

    private readonly List<UciOption> _options = new();
    public IReadOnlyList<UciOption> Options => _options;

    private readonly ISystemInfo _sysInfo;
    private readonly EngineConfig _config = new();
    private readonly UciLogger _logger = new();
    
    public EngineConfig Config => _config;

    public Engine(ISystemInfo? sysInfo = null)
    {
        _sysInfo = sysInfo ?? new ProductionSystemInfo();
        _game = GameFactory.Create();
        _game.NewGame();
        _driver = new PositionDriver(_game);
        InitializeOptions();
    }

    private void InitializeOptions()
    {
        // Standard Options per UCI_PROTOCOL.md
        _options.Add(new UciOption { Name = "Debug Log File", Type = UciOptionType.String, DefaultValue = "<empty>", OnChanged = val => { _config.Standard.DebugLogFile = val; _logger.SetLogFile(val, OnInfo); } });
        _options.Add(new UciOption { Name = "Threads", Type = UciOptionType.Spin, DefaultValue = "1", Min = 1, Max = _sysInfo.MaxThreads, OnChanged = val => _config.Standard.Threads = int.Parse(val) });
        _options.Add(new UciOption { Name = "Hash", Type = UciOptionType.Spin, DefaultValue = "16", Min = 1, Max = _sysInfo.MaxHashMb, OnChanged = val => { _config.Standard.Hash = int.Parse(val); } });
        _options.Add(new UciOption { Name = "Clear Hash", Type = UciOptionType.Button, OnChanged = val => { OnInfo?.Invoke("string cleared hash"); } });
        _options.Add(new UciOption { Name = "Ponder", Type = UciOptionType.Check, DefaultValue = "false", OnChanged = val => _config.Standard.Ponder = bool.Parse(val) });
        _options.Add(new UciOption { Name = "MultiPV", Type = UciOptionType.Spin, DefaultValue = "1", Min = 1, Max = 256, OnChanged = val => _config.Standard.MultiPV = int.Parse(val) });
        _options.Add(new UciOption { Name = "Move Overhead", Type = UciOptionType.Spin, DefaultValue = "10", Min = 0, Max = 5000, OnChanged = val => _config.Standard.MoveOverhead = int.Parse(val) });
        _options.Add(new UciOption { Name = "SyzygyPath", Type = UciOptionType.String, DefaultValue = "<empty>", OnChanged = val => _config.Standard.SyzygyPath = val });

        // Custom Heuristics
        _options.Add(new UciOption { Name = "EnableMaterial", Type = UciOptionType.Check, DefaultValue = "true", OnChanged = val => _config.Evaluation.EnableMaterial = bool.Parse(val) });
        _options.Add(new UciOption { Name = "EnableRMobility", Type = UciOptionType.Check, DefaultValue = "true", OnChanged = val => _config.Evaluation.EnableRMobility = bool.Parse(val) });
        _options.Add(new UciOption { Name = "EnableKingSafety", Type = UciOptionType.Check, DefaultValue = "true", OnChanged = val => _config.Evaluation.EnableKingSafety = bool.Parse(val) });

        _options.Add(new UciOption { Name = "MaterialWeight", Type = UciOptionType.Spin, DefaultValue = "1", Min = 0, Max = 100, OnChanged = val => _config.Evaluation.MaterialWeight = int.Parse(val) });
        _options.Add(new UciOption { Name = "MobilityWeight", Type = UciOptionType.Spin, DefaultValue = "10", Min = 0, Max = 100, OnChanged = val => _config.Evaluation.MobilityWeight = int.Parse(val) });
        _options.Add(new UciOption { Name = "KingSafetyWeight", Type = UciOptionType.Spin, DefaultValue = "20", Min = 0, Max = 100, OnChanged = val => _config.Evaluation.KingSafetyWeight = int.Parse(val) });
    }

    public void NewGame()
    {
        _game.NewGame();
    }

    public void SetPosition(string fen, string[]? moves = null)
    {
        if (fen == "startpos")
            _driver.SetPosition("startpos");
        else
            _driver.SetPosition(fen);

        if (moves != null)
        {
            foreach (var moveStr in moves)
            {
                var moveList = _game.Pos.GenerateMoves(); 
                var move = moveList.FirstOrDefault(m => m.Move.ToString() == moveStr);

                if (!move.Equals(default(ExtMove)))
                {
                    _driver.PushPermanent(move.Move);
                }
                else
                {
                    OnInfo?.Invoke($"info string illegal move in history: {moveStr}");
                    break;
                }
            }
        }
    }

    public void Go(SearchLimits limits)
    {
        lock (_sessionLock)
        {
            _currentSession?.Stop();
            _currentSession?.Dispose();

            long sessionId = _nextSessionId++;
            // Capture FEN snapshot for isolation
            string fen = _game.Pos.GenerateFen().ToString();
            
            _currentSession = new SearchSession(sessionId, fen, _config, limits,
                msg => {
                    if (CheckSession(sessionId)) OnInfo?.Invoke(msg);
                },
                move => {
                    if (CheckSession(sessionId)) OnBestMove?.Invoke(move);
                });
            
            _currentSession.Start(limits);
        }
    }

    private bool CheckSession(long sessionId)
    {
        lock (_sessionLock)
        {
            return _currentSession != null && _currentSession.SessionId == sessionId;
        }
    }

    public void Stop()
    {
        lock (_sessionLock)
        {
            _currentSession?.Stop();
        }
    }

    public void PonderHit()
    {
        lock (_sessionLock)
        {
            _currentSession?.PonderHit();
        }
    }

    public void SetOption(string name, string value)
    {
        var option = _options.FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (option == null) return;

        if (option.Type == UciOptionType.Button)
        {
            option.OnChanged?.Invoke("");
            return;
        }

        // Normalize string options
        if (option.Type == UciOptionType.String)
        {
            if (value == "<empty>") value = "";
        }

        // Validate Spin
        if (option.Type == UciOptionType.Spin)
        {
            if (!int.TryParse(value, out var spinVal) || (option.Min.HasValue && spinVal < option.Min) || (option.Max.HasValue && spinVal > option.Max))
            {
                OnInfo?.Invoke($"string invalid {option.Name} value: {value}");
                return;
            }
            if (option.Name.Equals("MultiPV", StringComparison.OrdinalIgnoreCase) && spinVal > 1)
            {
                OnInfo?.Invoke("string MultiPV > 1 not implemented; using 1");
            }
        }

        // Validate Check
        if (option.Type == UciOptionType.Check)
        {
            if (!value.Equals("true", StringComparison.OrdinalIgnoreCase) && !value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                OnInfo?.Invoke($"string invalid {option.Name} value: {value}");
                return;
            }
        }

        option.CurrentValue = value;
        try 
        {
            option.OnChanged?.Invoke(value);
        }
        catch (Exception ex)
        {
            OnInfo?.Invoke($"string error applying option {option.Name}: {ex.Message}");
        }
    }

    public void SetDebug(bool enabled)
    {
        _config.Standard.DebugEnabled = enabled;
        OnInfo?.Invoke($"string debug {(enabled ? "enabled" : "disabled")}");
    }

    public void Log(string direction, string message)
    {
        _logger.Log(direction, message);
    }
}
