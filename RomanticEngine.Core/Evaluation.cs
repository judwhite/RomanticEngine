using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Types;

namespace RomanticEngine.Core;

public static class Evaluation
{
    // Phase model: 24 in starting position, 0 in K+P endgames.
    private const int MaxPhase = 24;
    private const int PhaseScale = 256;

    // Pawn structure constants (centipawns)
    private const int IsolatedPawnPenalty = 15;
    private const int DoubledPawnPenalty = 10;

    // Rook file bonuses (centipawns)
    private const int HalfOpenFileBonus = 10;
    private const int OpenFileBonus = 20;

    // Bishop pair bonus (centipawns)
    private const int BishopPairBonus = 20;

    // Opening principle: discourage early queen development when minors are undeveloped.
    private const int EarlyQueenPenalty = 20;

    // Center pawn occupancy bonuses (centipawns).
    // Applies when a pawn is on its 4th rank (white: rank4, black: rank5).
    private const int CenterPawnMainBonus = 20; // d4/e4 (or d5/e5 for black)
    private const int CenterPawnSideBonus = 10; // c4/f4 (or c5/f5 for black)

    // Passed pawn bonuses by relative rank (1..6), 0 and 7 unused.
    private static readonly int[] PassedPawnBonusByRelativeRank =
    [
        0, // 0 (unused)
        5, // 1 (starting rank)
        10, // 2
        20, // 3
        35, // 4
        60, // 5
        90, // 6 (very advanced)
        0 // 7 (unused)
    ];

    public static int Evaluate(IGame game, EngineConfig.EvaluationConfig config)
    {
        var pos = game.Pos;

        // Evaluate from White POV (White - Black), then convert to side-to-move POV for negamax.
        int whiteScore = 0;

        if (config.EnableMaterial)
            whiteScore += EvaluateStaticWhite(pos) * config.MaterialWeight;

        if (config.EnableMobility)
            whiteScore += EvaluatePseudoMobilityWhite(pos) * config.MobilityWeight;

        if (config.EnableKingSafety)
            // Keep the existing pawn shield heuristic (but it should be small vs material/PST).
            whiteScore += EvaluatePawnShieldWhite(pos) * config.KingSafetyWeight;

        return pos.SideToMove.IsWhite ? whiteScore : -whiteScore;
    }

    /// <summary>
    /// "Static" eval in book terms: material + square tables + pawn structure + rook files + bishop pair.
    /// Returned from White POV (White - Black).
    /// </summary>
    private static int EvaluateStaticWhite(IPosition pos)
    {
        int score = 0;

        int[] whitePawnFileCount = new int[8];
        int[] blackPawnFileCount = new int[8];

        int[] whitePawnSquares = new int[8];
        int[] blackPawnSquares = new int[8];
        int whitePawnCount = 0;
        int blackPawnCount = 0;

        int[] whiteRookSquares = new int[2];
        int[] blackRookSquares = new int[2];
        int whiteRookCount = 0;
        int blackRookCount = 0;

        int whiteBishops = 0;
        int blackBishops = 0;

        int whiteKingSquare = -1;
        int blackKingSquare = -1;

        int whiteQueenSquare = -1;
        int blackQueenSquare = -1;

        int whiteMinorDeveloped = 0;
        int blackMinorDeveloped = 0;

        int phase = 0;

        for (int sq = 0; sq < 64; sq++)
        {
            var pc = pos.GetPiece(new Square(sq));
            var type = pc.Type();

            if (type == PieceTypes.NoPieceType)
                continue;

            bool isWhite = pc.ColorOf().IsWhite;

            if (type == PieceTypes.King)
            {
                if (isWhite)
                    whiteKingSquare = sq;
                else
                    blackKingSquare = sq;
                continue;
            }

            if (type == PieceTypes.Queen)
            {
                if (isWhite)
                    whiteQueenSquare = sq;
                else
                    blackQueenSquare = sq;
            }

            // Material base.
            int baseVal = PieceValue(type);

            if (baseVal != 0)
                score += isWhite ? baseVal : -baseVal;

            // Phase for king-table blending (ignore pawns/kings).
            phase += PhaseWeight(type);

            // Piece-square tables for non-kings.
            int pst = PieceSquareValueNonKing(type, sq, isWhite);

            if (pst != 0)
                score += isWhite ? pst : -pst;

            // Collect structure/activity data.
            int file = sq & 7;

            if (type == PieceTypes.Pawn)
            {
                // Center pawn occupancy bonus (White - Black).
                // relativeRank: 0..7 from the pawn's perspective.
                int rank = sq >> 3;
                int relRank = isWhite ? rank : 7 - rank;
                int centerBonus = GetCenterPawnBonus(file, relRank);
                if (centerBonus != 0)
                {
                    if (isWhite)
                        score += centerBonus;
                    else
                        score -= centerBonus;
                }

                if (isWhite)
                {
                    whitePawnFileCount[file]++;
                    if (whitePawnCount < 8)
                    {
                        whitePawnSquares[whitePawnCount] = sq;
                        whitePawnCount++;
                    }
                }
                else
                {
                    blackPawnFileCount[file]++;
                    if (blackPawnCount < 8)
                    {
                        blackPawnSquares[blackPawnCount] = sq;
                        blackPawnCount++;
                    }
                }
            }
            else if (type == PieceTypes.Rook)
            {
                if (isWhite)
                {
                    if (whiteRookCount < 2)
                    {
                        whiteRookSquares[whiteRookCount] = sq;
                        whiteRookCount++;
                    }
                }
                else
                {
                    if (blackRookCount < 2)
                    {
                        blackRookSquares[blackRookCount] = sq;
                        blackRookCount++;
                    }
                }
            }
            else if (type == PieceTypes.Bishop)
            {
                if (isWhite)
                    whiteBishops++;
                else
                    blackBishops++;
            }

            // Development tracking (opening-only use later).
            if (type == PieceTypes.Knight || type == PieceTypes.Bishop)
            {
                bool isStarting = IsMinorOnStartingSquare(type, sq, isWhite);

                if (!isStarting)
                {
                    if (isWhite)
                        whiteMinorDeveloped++;
                    else
                        blackMinorDeveloped++;
                }
            }
        }

        int endgameFactor = EndgameFactorFromPhase(phase);

        // King PST (blended opening/endgame).
        if (whiteKingSquare >= 0)
            score += PieceSquareValueKing(whiteKingSquare, isWhite: true, endgameFactor);

        if (blackKingSquare >= 0)
            score -= PieceSquareValueKing(blackKingSquare, isWhite: false, endgameFactor);

        // Bishop pair.
        if (whiteBishops >= 2)
            score += BishopPairBonus;

        if (blackBishops >= 2)
            score -= BishopPairBonus;

        // Rooks on half-open/open files.
        score += EvaluateRookFileBonuses(whiteRookSquares, whiteRookCount, whitePawnFileCount, blackPawnFileCount);
        score -= EvaluateRookFileBonuses(blackRookSquares, blackRookCount, blackPawnFileCount, whitePawnFileCount);

        // Pawn structure: doubled + isolated + passed.
        score += EvaluatePawnStructureWhite(
            whitePawnSquares,
            whitePawnCount,
            blackPawnSquares,
            blackPawnCount,
            whitePawnFileCount,
            blackPawnFileCount);

        // Early queen development penalty (opening only).
        // Apply only when we are clearly still in the opening: lots of pieces remain.
        if (phase >= 18)
        {
            score += EvaluateEarlyQueenPenalty(whiteQueenSquare, whiteMinorDeveloped, isWhite: true);
            score -= EvaluateEarlyQueenPenalty(blackQueenSquare, blackMinorDeveloped, isWhite: false);
        }

        return score;
    }

    private static int EvaluateRookFileBonuses(
        int[] rookSquares,
        int rookCount,
        int[] ownPawnFiles,
        int[] enemyPawnFiles)
    {
        int score = 0;

        for (int i = 0; i < rookCount; i++)
        {
            int sq = rookSquares[i];
            int file = sq & 7;

            bool hasOwnPawn = ownPawnFiles[file] > 0;
            bool hasEnemyPawn = enemyPawnFiles[file] > 0;

            if (!hasOwnPawn)
                score += !hasEnemyPawn ? OpenFileBonus : HalfOpenFileBonus;
        }

        return score;
    }

    private static int EvaluatePawnStructureWhite(
        int[] whitePawnSquares,
        int whitePawnCount,
        int[] blackPawnSquares,
        int blackPawnCount,
        int[] whitePawnFileCount,
        int[] blackPawnFileCount)
    {
        int score = 0;

        // Doubled pawns.
        for (int file = 0; file < 8; file++)
        {
            int w = whitePawnFileCount[file];
            int b = blackPawnFileCount[file];

            if (w > 1)
                score -= (w - 1) * DoubledPawnPenalty;

            if (b > 1)
                score += (b - 1) * DoubledPawnPenalty;
        }

        // Isolated + passed.
        for (int i = 0; i < whitePawnCount; i++)
        {
            int sq = whitePawnSquares[i];

            if (IsIsolatedPawn(sq, whitePawnFileCount))
                score -= IsolatedPawnPenalty;

            if (IsPassedPawn(sq, isWhitePawn: true, blackPawnSquares, blackPawnCount))
            {
                int rel = RelativeRank(sq, isWhite: true);
                score += PassedPawnBonusByRelativeRank[rel];
            }
        }

        for (int i = 0; i < blackPawnCount; i++)
        {
            int sq = blackPawnSquares[i];

            if (IsIsolatedPawn(sq, blackPawnFileCount))
                score += IsolatedPawnPenalty;

            if (IsPassedPawn(sq, isWhitePawn: false, whitePawnSquares, whitePawnCount))
            {
                int rel = RelativeRank(sq, isWhite: false);
                score -= PassedPawnBonusByRelativeRank[rel];
            }
        }

        return score;
    }

    private static int EvaluateEarlyQueenPenalty(int queenSquare, int minorDeveloped, bool isWhite)
    {
        if (queenSquare < 0)
            return 0;

        int startSq = isWhite ? 3 : 59; // d1 or d8 in A1=0 mapping
        if (queenSquare == startSq)
            return 0;

        if (minorDeveloped >= 2)
            return 0;

        return -EarlyQueenPenalty;
    }

    private static bool IsIsolatedPawn(int pawnSquare, int[] pawnFileCount)
    {
        int file = pawnSquare & 7;

        bool leftHas = file > 0 && pawnFileCount[file - 1] > 0;
        bool rightHas = file < 7 && pawnFileCount[file + 1] > 0;

        return !leftHas && !rightHas;
    }

    private static bool IsPassedPawn(int pawnSquare, bool isWhitePawn, int[] enemyPawnSquares, int enemyPawnCount)
    {
        int pawnFile = pawnSquare & 7;
        int pawnRank = pawnSquare >> 3;

        for (int i = 0; i < enemyPawnCount; i++)
        {
            int ep = enemyPawnSquares[i];
            int epFile = ep & 7;
            int epRank = ep >> 3;

            if (epFile < pawnFile - 1 || epFile > pawnFile + 1)
                continue;

            if (isWhitePawn)
            {
                if (epRank > pawnRank)
                    return false;
            }
            else
            {
                if (epRank < pawnRank)
                    return false;
            }
        }

        return true;
    }

    private static int GetCenterPawnBonus(int file, int relativeRank)
    {
        if (relativeRank != 3)
            return 0;

        if (file == 3 || file == 4)
            return CenterPawnMainBonus;

        if (file == 2 || file == 5)
            return CenterPawnSideBonus;

        return 0;
    }

    private static int RelativeRank(int square, bool isWhite)
    {
        int rank = square >> 3;

        if (isWhite)
            return rank;

        return 7 - rank;
    }

    private static bool IsMinorOnStartingSquare(PieceTypes type, int sq, bool isWhite)
    {
        if (type != PieceTypes.Knight && type != PieceTypes.Bishop)
            return false;

        // White: Nb1=1, Ng1=6, Bc1=2, Bf1=5
        // Black: Nb8=57, Ng8=62, Bc8=58, Bf8=61
        if (isWhite)
        {
            if (type == PieceTypes.Knight)
                return sq == 1 || sq == 6;

            return sq == 2 || sq == 5;
        }

        if (type == PieceTypes.Knight)
        {
            return sq == 57 || sq == 62;
        }

        return sq == 58 || sq == 61;
    }

    private static int EndgameFactorFromPhase(int phase)
    {
        if (phase < 0)
            phase = 0;

        if (phase > MaxPhase)
            phase = MaxPhase;

        return (MaxPhase - phase) * PhaseScale / MaxPhase;
    }

    private static int PhaseWeight(PieceTypes type)
    {
        return type switch
        {
            PieceTypes.Queen => 4,
            PieceTypes.Rook => 2,
            PieceTypes.Bishop => 1,
            PieceTypes.Knight => 1,
            _ => 0
        };
    }

    internal static int PieceValue(PieceTypes type)
    {
        return type switch
        {
            PieceTypes.Pawn => 100,
            PieceTypes.Knight => 315,
            PieceTypes.Bishop => 335,
            PieceTypes.Rook => 500,
            PieceTypes.Queen => 950,
            _ => 0
        };
    }

    // Used by search ordering; this is intentionally "opening-ish" and does not depend on phase.
    internal static int PieceSquareDeltaOpening(PieceTypes type, int fromSquare, int toSquare, bool isWhitePiece)
    {
        if (type == PieceTypes.King)
            return 0;

        int from = GetPstIndex(fromSquare, isWhitePiece);
        int to = GetPstIndex(toSquare, isWhitePiece);

        int fromVal = PieceSquareValueNonKingByIndex(type, from);
        int toVal = PieceSquareValueNonKingByIndex(type, to);

        return toVal - fromVal;
    }

    private static int PieceSquareValueNonKing(PieceTypes type, int square, bool isWhite)
    {
        int idx = GetPstIndex(square, isWhite);
        return PieceSquareValueNonKingByIndex(type, idx);
    }

    private static int PieceSquareValueNonKingByIndex(PieceTypes type, int idx)
    {
        return type switch
        {
            PieceTypes.Pawn => PawnPst[idx],
            PieceTypes.Knight => KnightPst[idx],
            PieceTypes.Bishop => BishopPst[idx],
            PieceTypes.Rook => RookPst[idx],
            PieceTypes.Queen => QueenPst[idx],
            _ => 0
        };
    }

    private static int PieceSquareValueKing(int square, bool isWhite, int endgameFactor)
    {
        int idx = GetPstIndex(square, isWhite);

        int open = KingOpenPst[idx];
        int endg = KingEndPst[idx];

        int openFactor = PhaseScale - endgameFactor;

        return (open * openFactor + endg * endgameFactor) / PhaseScale;
    }

    private static int GetPstIndex(int square, bool isWhite)
    {
        int file = square & 7;
        int rank = square >> 3;

        if (!isWhite)
            rank = 7 - rank;

        return (rank << 3) + file;
    }

    /// <summary>
    /// Pseudo-mobility (White pseudo moves - Black pseudo moves).
    /// Modified to avoid massively rewarding early queen mobility.
    /// - No king mobility
    /// - No pawn mobility
    /// - Queen mobility heavily downweighted
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
                PieceTypes.Knight => CountJumpPseudoMoves(pos, i, isWhite, KnightDeltas),
                PieceTypes.Bishop => CountSlidingPseudoMoves(pos, i, isWhite, BishopDirs),
                PieceTypes.Rook => CountSlidingPseudoMoves(pos, i, isWhite, RookDirs),
                PieceTypes.Queen => CountSlidingPseudoMoves(pos, i, isWhite, QueenDirs) / 4,
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

        // invalid positions should not crash evaluation.
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
                continue;

            int idx = (pawnRank << 3) + f;
            var pc = pos.GetPiece(new Square(idx));

            if (pc.Type() == PieceTypes.Pawn && pc.ColorOf().IsWhite == isWhite)
                count++;
        }

        return count;
    }

    private static readonly (int df, int dr)[] KnightDeltas =
    [
        (1, 2), (2, 1), (2, -1), (1, -2),
        (-1, -2), (-2, -1), (-2, 1), (-1, 2)
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

    // PSTs are from White POV, A1..H8 (rank1..rank8).
    private static readonly int[] PawnPst =
    [
        // Rank 1
        0, 0, 0, 0, 0, 0, 0, 0,
        // Rank 2 (do NOT reward staying home)
        0, 0, 0, 0, 0, 0, 0, 0,
        // Rank 3
        5, 5, 10, 15, 15, 10, 5, 5,
        // Rank 4 (center squares are valuable)
        0, 0, 5, 20, 20, 5, 0, 0,
        // Rank 5
        5, 5, 10, 25, 25, 10, 5, 5,
        // Rank 6
        10, 10, 20, 30, 30, 20, 10, 10,
        // Rank 7
        40, 40, 40, 40, 40, 40, 40, 40,
        // Rank 8
        0, 0, 0, 0, 0, 0, 0, 0
    ];

    private static readonly int[] KnightPst =
    [
        -25,-20,-15,-15,-15,-15,-20,-25,
        -20,-10,  0,  0,  0,  0,-10,-20,
        -15,  0,  5,  8,  8,  5,  0,-15,
        -15,  3,  8, 10, 10,  8,  3,-15,
        -15,  0,  8, 10, 10,  8,  0,-15,
        -15,  3,  5,  8,  8,  5,  3,-15,
        -20,-10,  0,  3,  3,  0,-10,-20,
        -25,-20,-15,-15,-15,-15,-20,-25
    ];

    private static readonly int[] BishopPst =
    [
        -20, -10, -10, -10, -10, -10, -10, -20,
        -10, 0, 0, 0, 0, 0, 0, -10,
        -10, 0, 5, 10, 10, 5, 0, -10,
        -10, 5, 5, 10, 10, 5, 5, -10,
        -10, 0, 10, 10, 10, 10, 0, -10,
        -10, 10, 10, 10, 10, 10, 10, -10,
        -10, 5, 0, 0, 0, 0, 5, -10,
        -20, -10, -10, -10, -10, -10, -10, -20
    ];

    private static readonly int[] RookPst =
    [
        0, 0, 0, 0, 0, 0, 0, 0,
        5, 10, 10, 10, 10, 10, 10, 5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        -5, 0, 0, 0, 0, 0, 0, -5,
        0, 0, 0, 5, 5, 0, 0, 0
    ];

    private static readonly int[] QueenPst =
    [
        -20, -10, -10, -5, -5, -10, -10, -20,
        -10, 0, 0, 0, 0, 0, 0, -10,
        -10, 0, 5, 5, 5, 5, 0, -10,
        -5, 0, 5, 5, 5, 5, 0, -5,
        0, 0, 5, 5, 5, 5, 0, -5,
        -10, 5, 5, 5, 5, 5, 0, -10,
        -10, 0, 5, 0, 0, 0, 0, -10,
        -20, -10, -10, -5, -5, -10, -10, -20
    ];

    private static readonly int[] KingOpenPst =
    [
        -30, -40, -40, -50, -50, -40, -40, -30,
        -30, -40, -40, -50, -50, -40, -40, -30,
        -30, -40, -40, -50, -50, -40, -40, -30,
        -30, -40, -40, -50, -50, -40, -40, -30,
        -20, -30, -30, -40, -40, -30, -30, -20,
        -10, -20, -20, -20, -20, -20, -20, -10,
        20, 20, 0, 0, 0, 0, 20, 20,
        20, 30, 10, 0, 0, 10, 30, 20
    ];

    private static readonly int[] KingEndPst =
    [
        -50, -40, -30, -20, -20, -30, -40, -50,
        -30, -20, -10, 0, 0, -10, -20, -30,
        -30, -10, 20, 30, 30, 20, -10, -30,
        -30, -10, 30, 40, 40, 30, -10, -30,
        -30, -10, 30, 40, 40, 30, -10, -30,
        -30, -10, 20, 30, 30, 20, -10, -30,
        -30, -30, 0, 0, 0, 0, -30, -30,
        -50, -30, -30, -30, -30, -30, -30, -50
    ];
}
