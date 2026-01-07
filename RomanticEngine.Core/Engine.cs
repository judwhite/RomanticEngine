using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.MoveGeneration;

namespace RomanticEngine.Core;

public class Engine : IEngine
{
    public event Action<string>? OnScore;
    public event Action<string>? OnInfo;
    public event Action<string>? OnBestMove;

    private readonly IGame _game;
    private readonly PositionDriver _driver;

    private SearchSession? _currentSession;
    private long _nextSessionId = 1;
    private readonly Lock _sessionLock = new();

    private readonly List<UciOption> _options = [];
    public IReadOnlyList<UciOption> Options => _options;

    private readonly ISystemInfo _sysInfo;
    private readonly UciLogger _logger = new();

    public EngineConfig Config { get; } = new();

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
        // Standard Options
        _options.Add(new UciOption
        {
            Name = "Debug Log File", Type = UciOptionType.String, DefaultValue = "<empty>", OnChanged = val =>
            {
                Config.Standard.DebugLogFile = val;
                _logger.SetLogFile(val, OnInfo);
            }
        });
        _options.Add(new UciOption
        {
            Name = "Threads", Type = UciOptionType.Spin, DefaultValue = "1", Min = 1, Max = _sysInfo.MaxThreads,
            OnChanged = val => Config.Standard.Threads = int.Parse(val)
        });
        _options.Add(new UciOption
        {
            Name = "Hash", Type = UciOptionType.Spin, DefaultValue = "16", Min = 1, Max = _sysInfo.MaxHashMb,
            OnChanged = val => { Config.Standard.Hash = int.Parse(val); }
        });
        _options.Add(new UciOption
        {
            Name = "Clear Hash", Type = UciOptionType.Button,
            OnChanged = _ => { OnInfo?.Invoke("cleared hash"); }
        });
        _options.Add(new UciOption
        {
            Name = "Ponder", Type = UciOptionType.Check, DefaultValue = "false",
            OnChanged = val => Config.Standard.Ponder = bool.Parse(val)
        });
        _options.Add(new UciOption
        {
            Name = "MultiPV", Type = UciOptionType.Spin, DefaultValue = "1", Min = 1, Max = 256,
            OnChanged = val => Config.Standard.MultiPV = int.Parse(val)
        });
        _options.Add(new UciOption
        {
            Name = "Move Overhead", Type = UciOptionType.Spin, DefaultValue = "10", Min = 0, Max = 5000,
            OnChanged = val => Config.Standard.MoveOverhead = int.Parse(val)
        });
        _options.Add(new UciOption
        {
            Name = "SyzygyPath", Type = UciOptionType.String, DefaultValue = "<empty>",
            OnChanged = val => Config.Standard.SyzygyPath = val
        });

        // Custom Heuristics
        _options.Add(new UciOption
        {
            Name = "EnableMaterial", Type = UciOptionType.Check, DefaultValue = "true",
            OnChanged = val => Config.Evaluation.EnableMaterial = bool.Parse(val)
        });
        _options.Add(new UciOption
        {
            Name = "EnableMobility", Type = UciOptionType.Check, DefaultValue = "true",
            OnChanged = val => Config.Evaluation.EnableMobility = bool.Parse(val)
        });
        _options.Add(new UciOption
        {
            Name = "EnableKingSafety", Type = UciOptionType.Check, DefaultValue = "true",
            OnChanged = val => Config.Evaluation.EnableKingSafety = bool.Parse(val)
        });

        _options.Add(new UciOption
        {
            Name = "MaterialWeight", Type = UciOptionType.Spin, DefaultValue = "1", Min = 0, Max = 100,
            OnChanged = val => Config.Evaluation.MaterialWeight = int.Parse(val)
        });
        _options.Add(new UciOption
        {
            Name = "MobilityWeight", Type = UciOptionType.Spin, DefaultValue = "2", Min = 0, Max = 100,
            OnChanged = val => Config.Evaluation.MobilityWeight = int.Parse(val)
        });
        _options.Add(new UciOption
        {
            Name = "KingSafetyWeight", Type = UciOptionType.Spin, DefaultValue = "5", Min = 0, Max = 100,
            OnChanged = val => Config.Evaluation.KingSafetyWeight = int.Parse(val)
        });
    }

    public void NewGame()
    {
        _game.NewGame();
    }

    public void SetPosition(string fen)
    {
        SetPosition(fen, []);
    }

    public void SetPosition(string fen, string[] moves)
    {
        fen = fen.Trim();

        _driver.SetPosition(fen.Equals("startpos", StringComparison.OrdinalIgnoreCase) ? "startpos" : fen);

        for (int i = 0; i < moves.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(moves[i]))
                continue;

            var moveStr = moves[i].Trim().ToLowerInvariant();

            var moveList = _game.Pos.GenerateMoves();
            var move = moveList.FirstOrDefault(m => m.Move.IsUciEqual(moveStr));

            if (!move.Equals(default))
            {
                _driver.PushPermanent(move.Move);
            }
            else
            {
                OnInfo?.Invoke($"illegal move in history at ply {i + 1}: {moveStr}");
                foreach (var m in moveList)
                {
                    OnInfo?.Invoke($"legal move: {m.Move.ToUci()}, is_castling: {m.Move.IsCastleMove()}, moveStr: {moveStr}, equal: {m.Move.IsUciEqual(moveStr)}");
                }
                break;
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

            _currentSession = new SearchSession(sessionId, fen, Config, limits,
                score =>
                {
                    if (CheckSession(sessionId))
                        OnScore?.Invoke(score);
                },
                move =>
                {
                    if (CheckSession(sessionId))
                        OnBestMove?.Invoke(move);
                },
                msg =>
                {
                    if (CheckSession(sessionId))
                        OnInfo?.Invoke(msg);
                }
            );

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
        if (option == null)
            return;

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
            if (!int.TryParse(value, out var spinVal) ||
                (option.Min.HasValue && spinVal < option.Min) ||
                (option.Max.HasValue && spinVal > option.Max))
            {
                OnInfo?.Invoke($"invalid {option.Name} value: {value}");
                return;
            }

            if (option.Name.Equals("MultiPV", StringComparison.OrdinalIgnoreCase) && spinVal > 1)
            {
                OnInfo?.Invoke("MultiPV > 1 not implemented; using 1");
                value = "1";
            }
        }

        // Validate Check
        if (option.Type == UciOptionType.Check)
        {
            if (!value.Equals("true", StringComparison.OrdinalIgnoreCase) &&
                !value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                OnInfo?.Invoke($"invalid {option.Name} value: {value}");
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
            OnInfo?.Invoke($"error applying option {option.Name}: {ex.Message}");
        }
    }

    public void SetDebug(bool enabled)
    {
        Config.Standard.DebugEnabled = enabled;
        OnInfo?.Invoke($"debug {(enabled ? "enabled" : "disabled")}");
    }

    public void Log(LogDirection direction, string message)
    {
        _logger.Log(direction, message);
    }
}
