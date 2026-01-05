using System;
using System.Diagnostics;
using System.Linq;
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Types;
using Rudzoft.ChessLib.Enums;
using Rudzoft.ChessLib.MoveGeneration;

namespace RomanticEngine.Core;

public class Search
{
    private readonly IGame _game;
    private long _nodes;
    private long _startTime;
    private long _stopTime;
    private bool _stop;

    public Search(IGame game)
    {
        _game = game;
    }

    public void Start(SearchLimits limits, Action<string> onInfo, Action<string> onBestMove)
    {
        _stop = false;
        _nodes = 0;
        _startTime = Stopwatch.GetTimestamp();
        
        int maxDepth = limits.Depth > 0 ? limits.Depth : 64;
        
        // Simple Time Management
        long timeAllocated = 0;
        if (limits.WhiteTime > 0 && limits.BlackTime > 0)
        {
             // Assume 20 moves to go or 1/20th of time
             int timeRemaining = _game.Pos.SideToMove.IsWhite ? limits.WhiteTime : limits.BlackTime;
             int inc = _game.Pos.SideToMove.IsWhite ? limits.WhiteIncrement : limits.BlackIncrement;
             timeAllocated = (timeRemaining / 20) + (inc / 2);
             if (timeAllocated > timeRemaining - 50) timeAllocated = timeRemaining - 50; 
             if (timeAllocated < 10) timeAllocated = 10;
        }
        else if (limits.MoveTime > 0)
        {
            timeAllocated = limits.MoveTime;
        }

        if (timeAllocated > 0 && !limits.Infinite)
        {
             long freq = Stopwatch.Frequency;
             _stopTime = _startTime + (long)((timeAllocated / 1000.0) * freq);
        }
        else
        {
            _stopTime = 0;
        }

        Move bestMove = Move.EmptyMove;
        
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            if (_stop) break;

            int score = AlphaBeta(depth, -50000, 50000, out var mpv);
            
            if (_stop) break;

            bestMove = mpv;
            
            long currentTime = GetCurrentTimeMs();
            string scoreStr = IsMate(score, out int mateIn) ? $"mate {mateIn}" : $"cp {score}";
            long nps = currentTime > 0 ? (_nodes * 1000) / currentTime : 0;
            
            // Standardizing output
            onInfo?.Invoke($"depth {depth} multipv 1 score {scoreStr} nodes {_nodes} nps {nps} hashfull 0 tbhits 0 time {currentTime} pv {bestMove}");
            
            // Check time after iteration
             if (_stopTime > 0 && Stopwatch.GetTimestamp() > _stopTime)
             {
                 _stop = true;
             }
        }

        string bestMoveStr = bestMove.ToString();
        // Simple Ponder: just pick the second best line or if PV exists, the second move in PV?
        // For now, if we have a PV, the ponder move is the next move.
        // But we only return 'bestMove'. We don't track the full PV in this simple loop yet except via string.
        // Let's just output bestmove for now. Enhancing with ponder requires tracking PV line.
        
        onBestMove?.Invoke($"bestmove {bestMoveStr}");
    }

    public void Stop()
    {
        _stop = true;
    }
    
    private long GetCurrentTimeMs()
    {
        return (long)((Stopwatch.GetTimestamp() - _startTime) * 1000.0 / Stopwatch.Frequency);
    }

    private bool IsMate(int score, out int mateIn)
    {
        mateIn = 0;
        if (Math.Abs(score) > 10000)
        {
            mateIn = (score > 0 ? 20000 - score : -20000 - score + 1) / 2; 
            return true;
        }
        return false;
    }

    private int AlphaBeta(int depth, int alpha, int beta, out Move bestMove)
    {
        if ((_nodes++ & 127) == 0) CheckTime();
        
        bestMove = Move.EmptyMove;
        if (depth == 0) return Evaluation.Evaluate(_game);

        var moves = _game.Pos.GenerateMoves();
        if (moves.Length == 0)
        {
            if (_game.Pos.InCheck) return -20000 + (64 - depth); // Mated
            return 0; // Stalemate
        }

        // Move ordering would go here
        
        // Count pseudo-legal moves for simple selectivity metric? 
        // Or just use depth. For now seldepth = depth.
        
        foreach (var move in moves)
        {
            var newState = new State();
            _game.Pos.MakeMove(move, newState);
            
            int score = -AlphaBeta(depth - 1, -beta, -alpha, out _);
            
            _game.Pos.TakeMove(move);

            if (_stop) return 0;

            if (score >= beta)
            {
                return beta; // Cutoff
            }

            if (score > alpha)
            {
                alpha = score;
                bestMove = move.Move;
            }
        }

        return alpha;
    }
    
    private void CheckTime()
    {
        if (_stop) return;
        if (_stopTime > 0 && Stopwatch.GetTimestamp() > _stopTime)
        {
            _stop = true;
        }
    }
}
