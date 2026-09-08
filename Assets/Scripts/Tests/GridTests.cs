using NUnit.Framework;
using GridChallenge.Core;
using System.Collections.Generic;

namespace GridChallenge.Tests
{
    public class GridTests
    {
        [Test]
        public void Grid_Initialize_CreatesRequestedDimensionsAndInitialTiles()
        {
            var board = new GridBoard(4, 4, initialMoves: 20, targetScore: 2048, seed: 42);
            board.Initialize(initialTileCount: 2, obstacleCount: 1);

            Assert.AreEqual(4, board.Width);
            Assert.AreEqual(4, board.Height);
            Assert.AreEqual(20, board.MovesRemaining);
            Assert.AreEqual(0, board.Score);
            Assert.IsFalse(board.IsGameOver);

            int tiles = 0;
            int obstacles = 0;
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    if (board.Grid[x, y].IsObstacle) obstacles++;
                    else if (!board.Grid[x, y].IsEmpty) tiles++;
                }
            }

            Assert.AreEqual(1, obstacles);
            Assert.AreEqual(2, tiles);
        }

        [Test]
        public void Grid_SlideAndMerge_ComputesCorrectValueAndScore()
        {
            var board = new GridBoard(4, 4, initialMoves: 10, targetScore: 2048, seed: 1);
            board.Initialize(initialTileCount: 0, obstacleCount: 0);

            // Row 0: [2, 2, 0, 0] at y = 0
            board.Grid[0, 0] = new TileData(1, 2);
            board.Grid[1, 0] = new TileData(2, 2);

            bool moved = board.TryMove(Direction.Left, out List<TileMove> moves, out TileData? spawned);

            Assert.IsTrue(moved);
            Assert.AreEqual(4, board.Grid[0, 0].Value);
            Assert.AreEqual(4, board.Score);
            Assert.AreEqual(9, board.MovesRemaining);
            Assert.IsTrue(board.CanUndo);
        }

        [Test]
        public void Grid_Obstacle_BlocksSliding()
        {
            var board = new GridBoard(4, 4, initialMoves: 10, seed: 1);
            board.Initialize(initialTileCount: 0, obstacleCount: 0);

            // [2, Obstacle, 0, 0] at y = 0
            board.Grid[0, 0] = new TileData(1, 2);
            board.Grid[1, 0] = new TileData(2, 0, TileType.Obstacle);

            bool moved = board.TryMove(Direction.Right, out _, out _);

            // Tile at 0,0 cannot pass obstacle at 1,0 when sliding right
            Assert.IsFalse(moved);
            Assert.AreEqual(2, board.Grid[0, 0].Value);
        }

        [Test]
        public void Grid_Undo_RestoresExactPreviousStateAndScore()
        {
            var board = new GridBoard(4, 4, initialMoves: 10, seed: 1);
            board.Initialize(initialTileCount: 0, obstacleCount: 0);

            board.Grid[0, 0] = new TileData(1, 2);
            board.Grid[1, 0] = new TileData(2, 2);

            board.TryMove(Direction.Left, out _, out _);
            Assert.AreEqual(4, board.Score);
            Assert.AreEqual(9, board.MovesRemaining);

            bool undone = board.Undo();
            Assert.IsTrue(undone);
            Assert.AreEqual(0, board.Score);
            Assert.AreEqual(10, board.MovesRemaining);
            Assert.AreEqual(2, board.Grid[0, 0].Value);
            Assert.AreEqual(2, board.Grid[1, 0].Value);
            Assert.IsFalse(board.CanUndo);
        }

        [Test]
        public void Grid_BreakTile_RemovesTileAndEnablesUndo()
        {
            var board = new GridBoard(4, 4, initialMoves: 10, seed: 1);
            board.Initialize(initialTileCount: 0, obstacleCount: 0);

            board.Grid[2, 2] = new TileData(5, 8);
            bool broken = board.BreakTile(2, 2, out int brokenId);

            Assert.IsTrue(broken);
            Assert.AreEqual(5, brokenId);
            Assert.IsTrue(board.Grid[2, 2].IsEmpty);

            board.Undo();
            Assert.AreEqual(8, board.Grid[2, 2].Value);
        }
    }
}
