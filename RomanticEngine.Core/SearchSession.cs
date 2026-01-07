using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.Types;
using System.Diagnostics;
using System.Text;
using Rudzoft.ChessLib.MoveGeneration;

namespace RomanticEngine.Core;

public sealed class SearchSession : IDisposable
{
    private readonly Action<string> _onScore;
    private readonly Action<string> _onBestMove;
    private readonly Action<string> _onInfo;
    private readonly IGame _game;
    private readonly PositionDriver _driver;
    private readonly CancellationTokenSource _cts;

    private readonly Lock _lock = new();

    private bool _bestMoveSent;
    private volatile bool _isPondering;

    public long SessionId { get; }

    private readonly EngineConfig _config;

    public SearchSession(
        long sessionId,
        string fen,
        EngineConfig config,
        SearchLimits limits,
        Action<string> onScore,
        Action<string> onBestMove,
        Action<string> onInfo)
    {
        SessionId = sessionId;
        _config = config;
        _isPondering = limits.Ponder;
        _onScore = onScore;
        _onBestMove = onBestMove;
        _onInfo = onInfo;

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
        _isPondering = false;
    }

    private void SearchTask(SearchLimits limits)
    {
        try
        {
            var worker = new InternalSearchWorker(_game, _driver, _config, () => _isPondering, _cts.Token);
            worker.Start(limits, EmitScore, EmitBestMove);
        }
        catch (OperationCanceledException)
        {
            EmitBestMove("0000");
        }
        catch (Exception ex)
        {
            EmitInfo($"Exception in search: {ex.Message}");
            EmitBestMove("0000");
        }
        finally
        {
            // Defensive: guarantee exactly one bestmove eventually (unless still pondering).
            EmitBestMove("0000");
        }
    }

    private void EmitScore(string message)
    {
        _onScore.Invoke(message);
    }

    private void EmitInfo(string message)
    {
        _onInfo.Invoke(message);
    }

    private void EmitBestMove(string moveStr)
    {
        lock (_lock)
        {
            if (_isPondering || _bestMoveSent)
                return;

            _bestMoveSent = true;
        }

        _onBestMove.Invoke(moveStr);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private sealed class InternalSearchWorker(
        IGame game,
        PositionDriver driver,
        EngineConfig config,
        Func<bool> isPondering,
        CancellationToken token)
    {
        private long _nodes;
        private long _startTime;
        private long _stopTime;
        private long _nodeLimit;

        private Move _bestMoveSoFar = Move.EmptyMove;

        private const int MateValue = 30000;
        private const int MaxPly = 128;

        private const int QueenValue = 900;
        private const int DeltaPruneMargin = 900;

        private readonly Move[,] _pvTable = new Move[MaxPly, MaxPly];
        private readonly int[] _pvLength = new int[MaxPly];

        private HashSet<string>? _rootSearchMoveSet;

        public void Start(SearchLimits limits, Action<string> onInfo, Action<string> onBestMove)
        {
            _nodes = 0;
            _startTime = Stopwatch.GetTimestamp();

            _nodeLimit = limits.Nodes > 0 ? limits.Nodes : long.MaxValue;

            _rootSearchMoveSet = limits.SearchMoves is { Length: > 0 } ? [..limits.SearchMoves] : null;

            long timeAllocated = 0;

            if (limits.MoveTime > 0)
            {
                timeAllocated = limits.MoveTime - config.Standard.MoveOverhead;
            }
            else if (limits.WhiteTime > 0 || limits.BlackTime > 0)
            {
                int timeRemaining = game.Pos.SideToMove.IsWhite ? limits.WhiteTime : limits.BlackTime;
                int inc = game.Pos.SideToMove.IsWhite ? limits.WhiteIncrement : limits.BlackIncrement;
                int mtg = limits.MovesToGo > 0 ? limits.MovesToGo : 30;

                timeAllocated = timeRemaining / mtg + inc - config.Standard.MoveOverhead;

                timeAllocated = Math.Max(10, timeAllocated);
            }

            if (timeAllocated > 0 && limits is { Infinite: false, Ponder: false })
                _stopTime = _startTime + (long)(timeAllocated / 1000.0 * Stopwatch.Frequency);
            else
                _stopTime = 0;

            int maxDepth;

            if (limits.Infinite)
                maxDepth = int.MaxValue;
            else if (limits.Depth > 0)
                maxDepth = limits.Depth;
            else
                maxDepth = 64;

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                if (ShouldStop())
                    break;

                int score = AlphaBeta(depth, -MateValue, MateValue, out var currentBest);

                if (ShouldStop())
                    break;

                if (currentBest != Move.EmptyMove)
                    _bestMoveSoFar = currentBest;

                long currentTimeMs = ElapsedMs();
                string scoreStr = FormatScore(score);
                string pvStr = GetPvString();
                long nps = _nodes * 1000 / Math.Max(1, currentTimeMs);

                onInfo.Invoke(
                    $"depth {depth} multipv 1 score {scoreStr} nodes {_nodes} nps {nps} hashfull 0 tbhits 0 time {currentTimeMs} pv {pvStr}"
                );

                if (limits.Mate > 0)
                {
                    int mateMoves = MateMovesFromScore(score);
                    if (mateMoves != 0 && Math.Abs(mateMoves) <= limits.Mate)
                        break;
                }
            }

            while (isPondering() && !token.IsCancellationRequested)
            {
                Thread.Sleep(5);
            }

            onBestMove.Invoke(_bestMoveSoFar.ToUci());
        }

        private int AlphaBeta(int depth, int alpha, int beta, out Move bestMove)
        {
            bestMove = Move.EmptyMove;

            int ply = driver.Ply;

            if (ply >= MaxPly - 1)
                return Evaluation.Evaluate(game, config.Evaluation);

            if (ShouldStop())
                return 0;

            _pvLength[ply] = ply;

            if (depth <= 0)
                return Quiescence(alpha, beta);

            _nodes++;

            var moves = game.Pos.GenerateMoves();

            if (moves.Length == 0)
            {
                if (game.Pos.InCheck)
                    return -(MateValue - ply);

                return 0;
            }

            // Root filtering for "searchmoves"
            bool filterRoot = ply == 0 && _rootSearchMoveSet is { Count: > 0 };

            // Simple in-place move ordering: prefer captures/promotions.
            for (int i = 0; i < moves.Length; i++)
            {
                int bestIndex = i;
                int bestScore = ScoreMoveForOrdering(moves[i].Move);

                for (int j = i + 1; j < moves.Length; j++)
                {
                    int s = ScoreMoveForOrdering(moves[j].Move);
                    if (s > bestScore)
                    {
                        bestScore = s;
                        bestIndex = j;
                    }
                }

                if (bestIndex != i)
                    (moves[i], moves[bestIndex]) = (moves[bestIndex], moves[i]);

                var m = moves[i].Move;

                if (filterRoot)
                {
                    string uci = m.ToUci();
                    if (!_rootSearchMoveSet!.Contains(uci))
                        continue;
                }

                using (_ = driver.Push(m))
                {
                    int score = -AlphaBeta(depth - 1, -beta, -alpha, out _);

                    if (ShouldStop())
                        return 0;

                    if (score >= beta)
                        return beta;

                    if (score > alpha)
                    {
                        alpha = score;
                        bestMove = m;

                        _pvTable[ply, ply] = m;

                        for (int k = ply + 1; k < _pvLength[ply + 1]; k++)
                            _pvTable[ply, k] = _pvTable[ply + 1, k];

                        _pvLength[ply] = _pvLength[ply + 1];
                    }
                }
            }

            // If root filtering excluded all moves, bestMove stays empty.
            return alpha;
        }

        private int Quiescence(int alpha, int beta)
        {
            int ply = driver.Ply;

            if (ply >= MaxPly - 1)
                return Evaluation.Evaluate(game, config.Evaluation);

            if (ShouldStop())
                return 0;

            _nodes++;

            bool inCheck = game.Pos.InCheck;

            int standPat = Evaluation.Evaluate(game, config.Evaluation);

            if (!inCheck)
            {
                if (standPat >= beta)
                    return beta;

                if (standPat > alpha)
                    alpha = standPat;

                // Delta pruning: if even winning a queen can't raise above alpha, stop.
                if (standPat + DeltaPruneMargin < alpha)
                    return alpha;
            }

            var moves = game.Pos.GenerateMoves();

            if (moves.Length == 0)
            {
                if (inCheck)
                    return -(MateValue - ply);

                return standPat;
            }

            // If not in check, only search "noisy" moves (captures/promotions).
            for (int i = 0; i < moves.Length; i++)
            {
                int bestIndex = i;
                int bestScore = ScoreNoisyMoveForOrdering(moves[i].Move);

                for (int j = i + 1; j < moves.Length; j++)
                {
                    int s = ScoreNoisyMoveForOrdering(moves[j].Move);
                    if (s > bestScore)
                    {
                        bestScore = s;
                        bestIndex = j;
                    }
                }

                if (bestIndex != i)
                    (moves[i], moves[bestIndex]) = (moves[bestIndex], moves[i]);

                var m = moves[i].Move;

                if (!inCheck)
                {
                    if (!IsNoisyMove(m))
                        continue;

                    // Optional extra delta pruning on a per-move basis (cheap and conservative).
                    int captured = CapturedPieceValue(m);
                    if (standPat + captured + QueenValue < alpha)
                        continue;
                }

                using (_ = driver.Push(m))
                {
                    int score = -Quiescence(-beta, -alpha);

                    if (ShouldStop())
                        return 0;

                    if (score >= beta)
                        return beta;

                    if (score > alpha)
                        alpha = score;
                }
            }

            if (!inCheck)
                return alpha;

            // In check: alpha may remain unchanged. That is okay; it represents the best evasion line found.
            return alpha;
        }

        private bool IsNoisyMove(Move m)
        {
            var (_, to, type) = m;

            if (type is MoveTypes.Enpassant or MoveTypes.Promotion)
                return true;
            if (type == MoveTypes.Castling)
                return false;

            var captured = game.Pos.GetPiece(to);
            return captured.Type() != PieceTypes.NoPieceType;
        }

        private int CapturedPieceValue(Move m)
        {
            var (_, to, type) = m;

            if (type == MoveTypes.Enpassant)
                return 100;
            if (type == MoveTypes.Castling)
                return 0;

            var captured = game.Pos.GetPiece(to);
            return Evaluation.PieceValue(captured.Type());
        }

        private int ScoreMoveForOrdering(Move m)
        {
            var (from, to, type) = m;

            if (type == MoveTypes.Promotion)
                return 1_000_000;

            if (type == MoveTypes.Enpassant)
                return 900_000;

            // Castling is usually good in the opening; make it come early among quiet moves.
            if (type == MoveTypes.Castling)
                return 50_000;

            var captured = game.Pos.GetPiece(to);
            if (captured.Type() != PieceTypes.NoPieceType)
            {
                int capturedVal = Evaluation.PieceValue(captured.Type());
                var attacker = game.Pos.GetPiece(from);
                int attackerVal = Evaluation.PieceValue(attacker.Type());

                // Keep captures ahead of quiet moves.
                return 500_000 + capturedVal * 1000 - attackerVal;
            }

            // Quiet move ordering: use PST delta as a cheap static heuristic.
            var mover = game.Pos.GetPiece(from);
            bool isWhite = mover.ColorOf().IsWhite;

            int fromSq = from.AsInt();
            int toSq = to.AsInt();

            int pstDelta = Evaluation.PieceSquareDeltaOpening(mover.Type(), fromSq, toSq, isWhite);

            return pstDelta;
        }

        private int ScoreNoisyMoveForOrdering(Move m)
        {
            // For quiescence: we only care about ordering among noisy moves.
            // Use MVV-LVA style ordering + promotion boost.
            var (from, _, type) = m;

            if (type == MoveTypes.Promotion)
                return 2_000_000;

            int capturedVal = CapturedPieceValue(m);
            if (capturedVal == 0)
                return -1;

            var attacker = game.Pos.GetPiece(from);
            int attackerVal = Evaluation.PieceValue(attacker.Type());

            return capturedVal * 1000 - attackerVal;
        }

        private string GetPvString()
        {
            int len = _pvLength[0];

            if (len <= 0)
                return "";

            var sb = new StringBuilder(len * 5);

            for (int i = 0; i < len; i++)
            {
                if (i > 0)
                    sb.Append(' ');
                sb.Append(_pvTable[0, i].ToUci());
            }

            return sb.ToString();
        }

        private static string FormatScore(int score)
        {
            int mateMoves = MateMovesFromScore(score);
            return mateMoves != 0 ? $"mate {mateMoves}" : $"cp {score}";
        }

        private static int MateMovesFromScore(int score)
        {
            // Detect mate scores in the usual band.
            // Score conventions:
            //   winning mate is close to +MateValue
            //   losing mate is close to -MateValue
            int abs = Math.Abs(score);
            if (abs < MateValue - MaxPly)
                return 0;

            int pliesToMate = MateValue - abs;
            int movesToMate = (pliesToMate + 1) / 2;

            return score < 0 ? -movesToMate : movesToMate;
        }

        private long ElapsedMs()
        {
            return (long)((Stopwatch.GetTimestamp() - _startTime) * 1000.0 / Stopwatch.Frequency);
        }

        private bool ShouldStop()
        {
            if (token.IsCancellationRequested || _nodes >= _nodeLimit)
                return true;

            if (isPondering())
                return false;

            return _stopTime > 0 && Stopwatch.GetTimestamp() > _stopTime;
        }
    }
}
