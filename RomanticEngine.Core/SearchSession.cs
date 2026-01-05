using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.Types;
using Rudzoft.ChessLib.MoveGeneration;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RomanticEngine.Core;

public class SearchSession : IDisposable
{
    private readonly long _sessionId;
    private readonly Action<string> _onInfo;
    private readonly Action<string> _onBestMove;
    private readonly IGame _game;
    private readonly PositionDriver _driver;
    private readonly CancellationTokenSource _cts;
    private bool _bestMoveSent;
    private readonly object _lock = new();
    private bool _isPondering;

    public long SessionId => _sessionId;

    private readonly EngineConfig _config;

    public SearchSession(long sessionId, string fen, EngineConfig config, SearchLimits limits, Action<string> onInfo, Action<string> onBestMove)
    {
        _sessionId = sessionId;
        _onInfo = onInfo;
        _onBestMove = onBestMove;
        _config = config;
        _isPondering = limits.Ponder;
        
        // Clone position: create new game and set to FEN
        _game = GameFactory.Create();
        if (string.IsNullOrEmpty(fen) || fen == "startpos")
            _game.NewGame();
        else
            _game.NewGame(fen);
        
        _driver = new PositionDriver(_game);
        _cts = new CancellationTokenSource();
    }

    public void Start(SearchLimits limits)
    {
        Task.Run(() => SearchTask(limits), _cts.Token);
    }

    public void Stop()
    {
        _cts.Cancel();
        _isPondering = false;
    }

    public void PonderHit()
    {
        lock (_lock)
        {
            _isPondering = false;
        }
    }

    private void SearchTask(SearchLimits limits)
    {
        try
        {
            var search = new InternalSearchWorker(_game, _driver, _cts.Token, _config, () => _isPondering);
            search.Start(limits, 
                info => EmitInfo(info), 
                move => EmitBestMove(move));
        }
        catch (OperationCanceledException)
        {
            EmitBestMove("0000"); 
        }
        catch (Exception ex)
        {
            EmitInfo($"string Exception in search: {ex.Message}");
            EmitBestMove("0000");
        }
        finally
        {
            EmitBestMove("0000");
        }
    }

    private void EmitInfo(string message)
    {
        _onInfo?.Invoke(message);
    }

    private void EmitBestMove(string moveStr)
    {
        lock (_lock)
        {
            if (_isPondering) return; // Never send bestmove while pondering
            if (_bestMoveSent) return;
            _bestMoveSent = true;
        }
        _onBestMove?.Invoke(moveStr);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private class InternalSearchWorker
    {
        private readonly IGame _game;
        private readonly PositionDriver _driver;
        private readonly CancellationToken _token;
        private readonly EngineConfig _config;
        private readonly Func<bool> _isPondering;
        private long _nodes;
        private long _startTime;
        private long _stopTime;
        private long _nodeLimit;
        private Move _bestMoveSoFar = Move.EmptyMove;

        private const int MateValue = 30000;
        private const int MaxPly = 128;
        private readonly Move[,] _pvTable = new Move[MaxPly, MaxPly];
        private readonly int[] _pvLength = new int[MaxPly];

        public InternalSearchWorker(IGame game, PositionDriver driver, CancellationToken token, EngineConfig config, Func<bool> isPondering)
        {
            _game = game;
            _driver = driver;
            _token = token;
            _config = config;
            _isPondering = isPondering;
        }

        public void Start(SearchLimits limits, Action<string> onInfo, Action<string> onBestMove)
        {
            _nodes = 0;
            _startTime = Stopwatch.GetTimestamp();
            int maxDepth = limits.Depth > 0 ? limits.Depth : 64;
            _nodeLimit = limits.Nodes > 0 ? limits.Nodes : long.MaxValue;

            long timeAllocated = 0;
            if (limits.MoveTime > 0)
            {
                timeAllocated = limits.MoveTime - _config.Standard.MoveOverhead;
            }
            else if (limits.WhiteTime > 0 || limits.BlackTime > 0)
            {
                int timeRemaining = _game.Pos.SideToMove.IsWhite ? limits.WhiteTime : limits.BlackTime;
                int inc = _game.Pos.SideToMove.IsWhite ? limits.WhiteIncrement : limits.BlackIncrement;
                int mtg = limits.MovesToGo > 0 ? limits.MovesToGo : 30;

                timeAllocated = (timeRemaining / mtg) + inc - _config.Standard.MoveOverhead;
                // Clamp to at least 10ms
                timeAllocated = Math.Max(10, timeAllocated); 
            }

            if (timeAllocated > 0 && !limits.Infinite && !limits.Ponder)
            {
                _stopTime = _startTime + (long)((timeAllocated / 1000.0) * Stopwatch.Frequency);
            }

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                if (ShouldStop()) break;

                int score = AlphaBeta(depth, -MateValue, MateValue, out var currentBest, limits.SearchMoves);
                
                if (ShouldStop()) break;

                _bestMoveSoFar = currentBest;
                long currentTime = (long)((Stopwatch.GetTimestamp() - _startTime) * 1000.0 / Stopwatch.Frequency);
                
                string scoreStr = Math.Abs(score) > MateValue - 1000 
                    ? $"mate {(score > 0 ? (MateValue - score + 1) / 2 : -(MateValue + score) / 2)}"
                    : $"cp {score}";

                var pvStr = GetPvString();
                onInfo?.Invoke($"depth {depth} multipv 1 score {scoreStr} nodes {_nodes} nps {(_nodes * 1000 / Math.Max(1, currentTime))} hashfull 0 tbhits 0 time {currentTime} pv {pvStr}");
                
                if (Math.Abs(score) > MateValue - 1000 && limits.Mate > 0 && Math.Abs(MateValue - Math.Abs(score)) <= limits.Mate) break;
            }

            var moveStr = _bestMoveSoFar == Move.EmptyMove ? "0000" : _bestMoveSoFar.ToString();
            onBestMove?.Invoke(moveStr);
        }

        private int AlphaBeta(int depth, int alpha, int beta, out Move bestMove, string[]? searchMoves = null)
        {
            bestMove = Move.EmptyMove;
            int ply = _driver.Ply;
            if (ply >= MaxPly - 1) return Evaluation.Evaluate(_game, _config.Evaluation);
            
            _pvLength[ply] = ply;

            if (ShouldStop()) return 0;
            
            _nodes++;
            if (depth == 0) return Evaluation.Evaluate(_game, _config.Evaluation);

            var moves = _game.Pos.GenerateMoves().AsEnumerable();
            
            if (ply == 0 && searchMoves != null && searchMoves.Length > 0)
            {
                moves = moves.Where(m => searchMoves.Contains(m.Move.ToString()));
            }

            var movesList = moves.ToList();
            if (movesList.Count == 0)
            {
                if (ply == 0 && searchMoves != null && searchMoves.Length > 0) return 0;
                if (_game.Pos.InCheck) return -(MateValue - ply);
                return 0;
            }

            foreach (var ext in movesList)
            {
                var m = ext.Move;
                using (var scope = _driver.Push(m))
                {
                    int score = -AlphaBeta(depth - 1, -beta, -alpha, out _);
                    
                    if (ShouldStop()) return 0;

                    if (score >= beta) return beta;
                    if (score > alpha)
                    {
                        alpha = score;
                        bestMove = m;
                        
                        _pvTable[ply, ply] = m;
                        for (int i = ply + 1; i < _pvLength[ply + 1]; i++)
                            _pvTable[ply, i] = _pvTable[ply + 1, i];
                        _pvLength[ply] = _pvLength[ply + 1];
                    }
                }
            }
            return alpha;
        }

        private string GetPvString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _pvLength[0]; i++)
            {
                if (i > 0) sb.Append(" ");
                sb.Append(_pvTable[0, i].ToString());
            }
            return sb.ToString();
        }

        private bool ShouldStop()
        {
            if (_token.IsCancellationRequested) return true;
            if (_nodes >= _nodeLimit) return true;
            if (_isPondering()) return false; 
            if (_stopTime > 0 && Stopwatch.GetTimestamp() > _stopTime) return true;
            return false;
        }
    }
}
