using Rudzoft.ChessLib.Extensions;
using Rudzoft.ChessLib.Types;

namespace RomanticEngine.Core;

public static class MoveExtensions
{
    extension(Move move)
    {
        public string ToUci()
        {
            if (move.IsNullMove())
                return "0000";

            var (from, to, type) = move;

            Span<char> destination = stackalloc char[5];

            destination[0] = from.FileChar;
            destination[1] = from.RankChar;
            destination[2] = to.FileChar;
            destination[3] = to.RankChar;

            if (type != MoveTypes.Promotion)
                return new string(destination[..4]);

            destination[4] = move.PromotedPieceType().GetPromotionChar();
            return new string(destination);
        }

        public bool IsUciEqual(string uciMove)
        {
            if (move.ToUci() == uciMove)
                return true;

            if (!move.IsCastleMove())
                return false;

            var (from, to) = move;
            if (from == Squares.e1)
            {
                if ((to == Squares.g1 || to == Squares.h1) && uciMove is "e1g1" or "e1h1" ||
                    (to == Squares.c1 || to == Squares.a1) && uciMove is "e1c1" or "e1a1")
                    return true;
            }
            else if (from == Squares.e8)
            {
                if ((to == Squares.g8 || to == Squares.h8) && uciMove is "e8g8" or "e8h8" ||
                    (to == Squares.c8 || to == Squares.a8) && uciMove is "e8c8" or "e8a8")
                    return true;
            }

            return false;
        }
    }
}
