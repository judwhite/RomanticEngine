using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Types;

namespace RomanticEngine.Core;

public static class Evaluation
{
    public static int Evaluate(IGame game, EngineConfig.EvaluationConfig config)
    {
        var pos = game.Pos;

        // Evaluate from White's POV (White - Black), then convert to side-to-move POV for negamax.
        int whiteScore = 0;

        if (config.EnableMaterial)
            whiteScore += EvaluateMaterialWhite(pos) * config.MaterialWeight;

        if (config.EnableRMobility)
            whiteScore += EvaluatePseudoMobilityWhite(pos) * config.MobilityWeight;

        if (config.EnableKingSafety)
            whiteScore += EvaluatePawnShieldWhite(pos) * config.KingSafetyWeight;

        return pos.SideToMove.IsWhite ? whiteScore : -whiteScore;
    }

    private static int EvaluateMaterialWhite(IPosition pos)
    {
        int score = 0;

        for (int i = 0; i < 64; i++)
        {
            var pc = pos.GetPiece(new Square(i));
            var type = pc.Type();

            if (type is PieceTypes.NoPieceType or PieceTypes.King)
                continue;

            int val = PieceValue(type);
            if (val == 0)
                continue;

            score += pc.ColorOf().IsWhite ? val : -val;
        }

        return score;
    }

    /// <summary>
    /// Pseudo-mobility (White pseudo moves - Black pseudo moves).
    /// Intentionally does NOT generate legal moves.
    /// </summary>
    private static int EvaluatePseudoMobilityWhite(IPosition pos)
    {
        int score = 0;

        for (int i = 0; i < 64; i++)
        {
            var pc = pos.GetPiece(new Square(i));
            var type = pc.Type();

            if (type == PieceTypes.NoPieceType)
                continue;

            bool isWhite = pc.ColorOf().IsWhite;

            int mob = type switch
            {
                PieceTypes.Pawn => CountPawnPseudoMoves(pos, i, isWhite),
                PieceTypes.Knight => CountJumpPseudoMoves(pos, i, isWhite, KnightDeltas),
                PieceTypes.Bishop => CountSlidingPseudoMoves(pos, i, isWhite, BishopDirs),
                PieceTypes.Rook => CountSlidingPseudoMoves(pos, i, isWhite, RookDirs),
                PieceTypes.Queen => CountSlidingPseudoMoves(pos, i, isWhite, QueenDirs),
                PieceTypes.King => CountJumpPseudoMoves(pos, i, isWhite, KingDeltas),
                _ => 0
            };

            if (mob == 0)
                continue;

            score += isWhite ? mob : -mob;
        }

        return score;
    }

    /// <summary>
    /// Pawn shield (White shield - Black shield).
    /// Shield is the count of friendly pawns on the three squares directly in front of the king.
    /// </summary>
    private static int EvaluatePawnShieldWhite(IPosition pos)
    {
        int whiteKing = -1;
        int blackKing = -1;

        for (int i = 0; i < 64; i++)
        {
            var pc = pos.GetPiece(new Square(i));
            if (pc.Type() != PieceTypes.King)
                continue;

            if (pc.ColorOf().IsWhite)
                whiteKing = i;
            else
                blackKing = i;
        }

        // Defensive: invalid positions should not crash evaluation.
        if (whiteKing < 0 || blackKing < 0)
            return 0;

        int whiteShield = CountPawnShield(pos, whiteKing, isWhite: true);
        int blackShield = CountPawnShield(pos, blackKing, isWhite: false);

        return whiteShield - blackShield;
    }

    private static int CountPawnShield(IPosition pos, int kingIndex, bool isWhite)
    {
        int file = kingIndex & 7;
        int rank = kingIndex >> 3;

        int pawnRank = rank + (isWhite ? 1 : -1);
        if (pawnRank < 0 || pawnRank > 7)
            return 0;

        int count = 0;

        for (int df = -1; df <= 1; df++)
        {
            int f = file + df;
            if (f < 0 || f > 7)
            {
                continue;
            }

            int idx = (pawnRank << 3) + f;
            var pc = pos.GetPiece(new Square(idx));

            if (pc.Type() == PieceTypes.Pawn && pc.ColorOf().IsWhite == isWhite)
            {
                count++;
            }
        }

        return count;
    }

    internal static int PieceValue(PieceTypes type)
    {
        return type switch
        {
            PieceTypes.Pawn => 100,
            PieceTypes.Knight => 320,
            PieceTypes.Bishop => 330,
            PieceTypes.Rook => 500,
            PieceTypes.Queen => 900,
            _ => 0
        };
    }

    private static readonly (int df, int dr)[] KnightDeltas =
    [
        (1, 2), (2, 1), (2, -1), (1, -2),
        (-1, -2), (-2, -1), (-2, 1), (-1, 2)
    ];

    private static readonly (int df, int dr)[] KingDeltas =
    [
        (1, 1), (1, 0), (1, -1),
        (0, 1), (0, -1),
        (-1, 1), (-1, 0), (-1, -1)
    ];

    private static readonly (int df, int dr)[] BishopDirs =
    [
        (1, 1), (1, -1), (-1, 1), (-1, -1)
    ];

    private static readonly (int df, int dr)[] RookDirs =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1)
    ];

    private static readonly (int df, int dr)[] QueenDirs =
    [
        (1, 1), (1, -1), (-1, 1), (-1, -1),
        (1, 0), (-1, 0), (0, 1), (0, -1)
    ];

    private static int CountJumpPseudoMoves(IPosition pos, int fromIndex, bool isWhite, (int df, int dr)[] deltas)
    {
        int file = fromIndex & 7;
        int rank = fromIndex >> 3;

        int count = 0;

        foreach (var (df, dr) in deltas)
        {
            int f = file + df;
            int r = rank + dr;

            if ((uint)f > 7 || (uint)r > 7)
                continue;

            int idx = (r << 3) + f;
            var target = pos.GetPiece(new Square(idx));

            if (target.Type() == PieceTypes.NoPieceType || target.ColorOf().IsWhite != isWhite)
                count++;
        }

        return count;
    }

    private static int CountSlidingPseudoMoves(IPosition pos, int fromIndex, bool isWhite, (int df, int dr)[] dirs)
    {
        int file = fromIndex & 7;
        int rank = fromIndex >> 3;

        int count = 0;

        foreach (var (df, dr) in dirs)
        {
            int f = file + df;
            int r = rank + dr;

            while ((uint)f <= 7 && (uint)r <= 7)
            {
                int idx = (r << 3) + f;
                var target = pos.GetPiece(new Square(idx));

                if (target.Type() == PieceTypes.NoPieceType)
                {
                    count++;
                }
                else
                {
                    if (target.ColorOf().IsWhite != isWhite)
                        count++;
                    break;
                }

                f += df;
                r += dr;
            }
        }

        return count;
    }

    private static int CountPawnPseudoMoves(IPosition pos, int fromIndex, bool isWhite)
    {
        int file = fromIndex & 7;
        int rank = fromIndex >> 3;

        int dir = isWhite ? 1 : -1;
        int nextRank = rank + dir;

        if ((uint)nextRank > 7)
            return 0;

        int count = 0;

        int oneIdx = (nextRank << 3) + file;

        if (pos.GetPiece(new Square(oneIdx)).Type() == PieceTypes.NoPieceType)
        {
            count++;

            int startRank = isWhite ? 1 : 6;

            if (rank == startRank)
            {
                int twoRank = rank + 2 * dir;
                int twoIdx = (twoRank << 3) + file;

                if (pos.GetPiece(new Square(twoIdx)).Type() == PieceTypes.NoPieceType)
                    count++;
            }
        }

        if (file > 0)
        {
            int capIdx = (nextRank << 3) + file - 1;
            var pc = pos.GetPiece(new Square(capIdx));

            if (pc.Type() != PieceTypes.NoPieceType && pc.ColorOf().IsWhite != isWhite)
                count++;
        }

        if (file < 7)
        {
            int capIdx = (nextRank << 3) + file + 1;
            var pc = pos.GetPiece(new Square(capIdx));

            if (pc.Type() != PieceTypes.NoPieceType && pc.ColorOf().IsWhite != isWhite)
                count++;
        }

        return count;
    }
}
