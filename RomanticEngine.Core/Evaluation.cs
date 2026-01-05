using System;
using System.Linq;
using Rudzoft.ChessLib;
using Rudzoft.ChessLib.Types;
using Rudzoft.ChessLib.Enums;
using Rudzoft.ChessLib.MoveGeneration;

namespace RomanticEngine.Core;

public static class Evaluation
{
    // Weights (configurable later)
    public static int MaterialWeight = 1;
    public static int MobilityWeight = 10; // 10cp per move? R-Mobility is the goal, so maybe higher.
    public static int KingSafetyWeight = 20;

    public static int Evaluate(IGame game)
    {
        var pos = game.Pos;
        int score = 0;

        if (Configuration.Evaluation.EnableMaterial)
        {
            score += EvaluateMaterial(pos) * Configuration.Evaluation.MaterialWeight;
        }

        if (Configuration.Evaluation.EnableRMobility)
        {
            int myMobility = GetRMobility(game);
            score += myMobility * Configuration.Evaluation.MobilityWeight;
        }

        if (Configuration.Evaluation.EnableKingSafety)
        {
            score += EvaluateKingSafety(pos) * Configuration.Evaluation.KingSafetyWeight;
        }

        return score;
    }

    private static int EvaluateMaterial(IPosition pos)
    {
        int score = 0;
        
        for (int i = 0; i < 64; i++)
        {
            var sq = new Square(i);
            var pc = pos.GetPiece(sq);
            
            // Check emptiness via Type() == PieceTypes.NoPiece or similar?
            // Or just check if pc == Piece.EmptyPiece (if exists).
            // Let's try Loop over PieceTypes if unsure.
            // But GetPiece returns a Piece.
            
            var type = pc.Type(); 
            if (type == PieceTypes.NoPieceType) continue; 

            int val = 0;
            
            switch (type) 
            {
                case PieceTypes.Pawn: val = 100; break;
                case PieceTypes.Knight: val = 320; break;
                case PieceTypes.Bishop: val = 330; break;
                case PieceTypes.Rook: val = 500; break;
                case PieceTypes.Queen: val = 900; break;
            }

            if (pc.ColorOf() == pos.SideToMove)
                score += val;
            else
                score -= val;
        }

        return score;
    }

    private static int GetRMobility(IGame game)
    {
        // R-Mobility = Count legal moves + (InCheck ? 0 : 0.5)
        // We scale 0.5 to integer (e.g. 1 unit = 0.5, or just add bonus)
        
        var moves = game.Pos.GenerateMoves();
        int count = moves.Length;
        int bonus = game.Pos.InCheck ? 0 : 1; 
        
        return count * 2 + bonus;
    }

    private static int EvaluateKingSafety(IPosition pos)
    {
        int score = 0;
        var ksq = pos.GetKingSquare(pos.SideToMove);
        var side = pos.SideToMove;
        
        if ((side.IsWhite && ksq.Rank == Rank.Rank1) || (!side.IsWhite && ksq.Rank == Rank.Rank8))
        {
            int forward = side.IsWhite ? 8 : -8;
            int[] offsets = { forward - 1, forward, forward + 1 };
            
            foreach (var offset in offsets)
            {
                int targetSqVal = ksq.AsInt() + offset;
                if (targetSqVal >= 0 && targetSqVal < 64)
                {
                    var targetSq = new Square(targetSqVal);
                    // Check if target square is adjacent file (using AsInt or cast)
                    int fileDiff = Math.Abs(targetSq.File.AsInt() - ksq.File.AsInt());
                    if (fileDiff <= 1)
                    {
                        var pc = pos.GetPiece(targetSq);
                        if (pc.Type() == PieceTypes.Pawn && pc.ColorOf() == side)
                        {
                            score += 10; 
                        }
                    }
                }
            }
        }
        
        return score; 
    }
}
