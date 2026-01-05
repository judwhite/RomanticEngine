using Rudzoft.ChessLib.Factories;
using Rudzoft.ChessLib.MoveGeneration;
using RomanticEngine.Core;

namespace RomanticEngine.Tests;

public class PositionDriverTests
{
    [Fact]
    public void Test_PositionDriver_ScopedPush_RestoresState()
    {
        var game = GameFactory.Create();
        var driver = new PositionDriver(game);
        driver.SetPosition("startpos");

        var initialKey = game.Pos.State.Key;

        var moves = game.Pos.GenerateMoves();
        foreach (var ext in moves)
        {
            var m = ext.Move;
            using (var _ = driver.Push(m))
            {
                Assert.NotEqual(initialKey, game.Pos.State.Key);

                // Recurse one level
                var moves2 = game.Pos.GenerateMoves();
                if (moves2.Length > 0)
                {
                    var m2 = moves2[0].Move;
                    using (var __ = driver.Push(m2))
                    {
                         // depth 2
                    }
                }
            }
            Assert.Equal(initialKey, game.Pos.State.Key);
        }
    }

    [Fact]
    public void Test_PositionDriver_Special_Moves_RoundTrip()
    {
        var fens = new[]
        {
            "r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1", // Castling
            "rnbqkbnr/pppp1ppp/8/4pP2/8/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2", // En-passant
            "8/4P3/8/8/8/8/8/4k2K w - - 0 1" // Promotion
        };

        foreach (var fen in fens)
        {
            var game = GameFactory.Create();
            var driver = new PositionDriver(game);
            driver.SetPosition(fen);
            var initialKey = game.Pos.State.Key;

            var moves = game.Pos.GenerateMoves();
            foreach (var ext in moves)
            {
                var m = ext.Move;
                using (var scope = driver.Push(m))
                {
                    // pushed
                }
                Assert.Equal(initialKey, game.Pos.State.Key);
            }
        }
    }

    [Fact]
    public void Test_PositionDriver_Random_Walk_RoundTrip()
    {
        var game = GameFactory.Create();
        var driver = new PositionDriver(game);
        driver.SetPosition("startpos");

        var random = new System.Random(42);

        for (int i = 0; i < 20; i++)
        {
            var initialKey = game.Pos.State.Key;
            var moves = game.Pos.GenerateMoves();
            if (moves.Length == 0) break;

            var m = moves[random.Next(moves.Length)].Move;

            using (var _ = driver.Push(m))
            {
                // depth 1
                var moves2 = game.Pos.GenerateMoves();
                if (moves2.Length > 0)
                {
                    var m2 = moves2[random.Next(moves2.Length)].Move;
                    using (var __ = driver.Push(m2))
                    {
                        // depth 2
                    }
                }
            }

            Assert.Equal(initialKey, game.Pos.State.Key);

            // Apply one permanently to move forward
            driver.PushPermanent(m);
        }
    }
}
