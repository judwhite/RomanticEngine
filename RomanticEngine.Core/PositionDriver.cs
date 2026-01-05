using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Types;
using System.Diagnostics;

namespace RomanticEngine.Core;

/// <summary>
/// Handles state-safe move application and reversal using a preallocated state stack.
/// </summary>
public class PositionDriver : IDisposable
{
    private readonly IGame _game;
    private readonly IPosition _pos;
    private readonly State[] _states;
    private int _ply;

    public PositionDriver(IGame game, int maxPly = 256)
    {
        _game = game;
        _pos = game.Pos;
        _states = new State[maxPly];
        for (int i = 0; i < maxPly; i++)
        {
            _states[i] = new State();
        }
        _ply = 0;
    }

    public int Ply => _ply;

    public void SetPosition(string fen)
    {
        _ply = 0;
        if (fen == "startpos")
            _game.NewGame();
        else
            _game.NewGame(fen);
    }

    public MoveScope Push(Move move)
    {
#if DEBUG
        var beforeKey = _pos.State.Key;
#endif

        _pos.MakeMove(move, _states[_ply + 1]);
        _ply++;

#if DEBUG
        return new MoveScope(this, move, beforeKey);
#else
        return new MoveScope(this, move);
#endif
    }

    public void PushPermanent(Move move)
    {
        _pos.MakeMove(move, _states[_ply + 1]);
        _ply++;
    }

    private void Pop(Move move)
    {
        _pos.TakeMove(move);
        _ply--;
    }

    public void Dispose()
    {
        // No-op for now, but part of IDisposable pattern for Scope
    }

    public struct MoveScope : IDisposable
    {
        private readonly PositionDriver _driver;
        private readonly Move _move;
#if DEBUG
        private readonly HashKey _expectedKey;
#endif

#if DEBUG
        public MoveScope(PositionDriver driver, Move move, HashKey expectedKey)
        {
            _driver = driver;
            _move = move;
            _expectedKey = expectedKey;
        }
#else
        public MoveScope(PositionDriver driver, Move move)
        {
            _driver = driver;
            _move = move;
        }
#endif

        public void Dispose()
        {
            _driver.Pop(_move);
#if DEBUG
            if (_driver._pos.State.Key != _expectedKey)
            {
                throw new InvalidOperationException($"State corruption detected at ply {_driver._ply + 1}. Expected key {_expectedKey}, but got {_driver._pos.State.Key}.");
            }
#endif
        }
    }
}
