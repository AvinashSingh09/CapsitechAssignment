using System;
using System.Collections.Generic;

namespace GridChallenge.Core
{
    public enum Direction { Up, Down, Left, Right }

    public enum TileType { Normal, Obstacle }

    public struct TileData : IEquatable<TileData>
    {
        public int Id;
        public int Value;
        public TileType Type;

        public bool IsEmpty => Value == 0 && Type == TileType.Normal;
        public bool IsObstacle => Type == TileType.Obstacle;

        public TileData(int id, int value, TileType type = TileType.Normal)
        {
            Id = id;
            Value = value;
            Type = type;
        }

        public bool Equals(TileData other) => Id == other.Id && Value == other.Value && Type == other.Type;
    }

    public struct TileMove
    {
        public int FromX, FromY;
        public int ToX, ToY;
        public int TileId;
        public bool Merged;
        public int TargetTileId;
        public int ResultValue;
    }

    public struct BoardSnapshot
    {
        public TileData[] Tiles;
        public int Score;
        public int MovesRemaining;
        public int NextTileId;
    }


    public class GridBoard
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int TargetScore;

        public TileData[,] Grid { get; private set; }
        public int Score { get; private set; }
        public int MovesRemaining { get; private set; }
        public int ComboCount { get; private set; }
        public bool IsGameOver { get; private set; }
        public bool IsGameWon { get; private set; }

        private int _nextTileId = 1;
        private readonly Random _random;

        private readonly BoardSnapshot[] _undoRingBuffer;
        private int _undoHead = 0;
        private int _undoCount = 0;
        private const int MaxUndoCapacity = 32;

        public bool CanUndo => _undoCount > 0 && !IsGameOver;

        public GridBoard(int width = 4, int height = 4, int initialMoves = 30, int targetScore = 2048, int? seed = null)
        {
            Width = width;
            Height = height;
            MovesRemaining = initialMoves;
            TargetScore = targetScore;
            Grid = new TileData[Width, Height];
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
            _undoRingBuffer = new BoardSnapshot[MaxUndoCapacity];
            for (int i = 0; i < MaxUndoCapacity; i++)
            {
                _undoRingBuffer[i].Tiles = new TileData[Width * Height];
            }
        }

        public void Initialize(int initialTileCount = 2, int obstacleCount = 1)
        {
            Array.Clear(Grid, 0, Grid.Length);
            Score = 0;
            ComboCount = 0;
            IsGameOver = false;
            IsGameWon = false;
            _nextTileId = 1;
            _undoHead = 0;
            _undoCount = 0;

            for (int i = 0; i < obstacleCount; i++)
            {
                var empty = GetEmptyCells();
                if (empty.Count == 0) break;
                var cell = empty[_random.Next(empty.Count)];
                Grid[cell.x, cell.y] = new TileData(_nextTileId++, 0, TileType.Obstacle);
            }

            for (int i = 0; i < initialTileCount; i++)
            {
                SpawnRandomTile();
            }
        }

        public bool TryMove(Direction direction, out List<TileMove> moves, out TileData? spawnedTile)
        {
            moves = new List<TileMove>();
            spawnedTile = null;

            if (IsGameOver || MovesRemaining <= 0) return false;

            SaveSnapshot();

            int dx = 0, dy = 0;
            switch (direction)
            {
                case Direction.Up:    dy = 1; break;
                case Direction.Down:  dy = -1; break;
                case Direction.Left:  dx = -1; break;
                case Direction.Right: dx = 1; break;
            }

            bool[,] mergedThisTurn = new bool[Width, Height];
            bool boardChanged = false;
            int turnMerges = 0;
            int turnScoreGained = 0;

            int startX = dx > 0 ? Width - 1 : 0;
            int endX   = dx > 0 ? -1 : Width;
            int stepX  = dx > 0 ? -1 : 1;

            int startY = dy > 0 ? Height - 1 : 0;
            int endY   = dy > 0 ? -1 : Height;
            int stepY  = dy > 0 ? -1 : 1;

            for (int x = startX; x != endX; x += stepX)
            {
                for (int y = startY; y != endY; y += stepY)
                {
                    TileData current = Grid[x, y];
                    if (current.IsEmpty || current.IsObstacle) continue;

                    int curX = x;
                    int curY = y;
                    int nextX = curX + dx;
                    int nextY = curY + dy;

                    while (IsInBounds(nextX, nextY) && Grid[nextX, nextY].IsEmpty)
                    {
                        curX = nextX;
                        curY = nextY;
                        nextX += dx;
                        nextY += dy;
                    }

                    if (IsInBounds(nextX, nextY) &&
                        !mergedThisTurn[nextX, nextY] &&
                        !Grid[nextX, nextY].IsObstacle &&
                        Grid[nextX, nextY].Value == current.Value)
                    {
                        // Merge!
                        int mergedVal = current.Value * 2;
                        turnMerges++;
                        turnScoreGained += mergedVal;

                        TileData targetTile = Grid[nextX, nextY];
                        Grid[x, y] = default;
                        Grid[nextX, nextY] = new TileData(targetTile.Id, mergedVal, TileType.Normal);
                        mergedThisTurn[nextX, nextY] = true;
                        boardChanged = true;

                        moves.Add(new TileMove
                        {
                            FromX = x,
                            FromY = y,
                            ToX = nextX,
                            ToY = nextY,
                            TileId = current.Id,
                            Merged = true,
                            TargetTileId = targetTile.Id,
                            ResultValue = mergedVal
                        });

                        if (mergedVal >= TargetScore)
                        {
                            IsGameWon = true;
                        }
                    }
                    else if (curX != x || curY != y)
                    {
                        Grid[curX, curY] = current;
                        Grid[x, y] = default;
                        boardChanged = true;

                        moves.Add(new TileMove
                        {
                            FromX = x,
                            FromY = y,
                            ToX = curX,
                            ToY = curY,
                            TileId = current.Id,
                            Merged = false,
                            ResultValue = current.Value
                        });
                    }
                }
            }

            if (!boardChanged)
            {
                PopDiscardSnapshot();
                return false;
            }

            ComboCount = turnMerges;
            int comboMultiplier = turnMerges > 1 ? turnMerges : 1;
            Score += turnScoreGained * comboMultiplier;

            MovesRemaining--;

            spawnedTile = SpawnRandomTile();

            CheckGameStatus();

            return true;
        }

        public bool BreakTile(int x, int y, out int brokenTileId)
        {
            brokenTileId = -1;
            if (!IsInBounds(x, y) || Grid[x, y].IsEmpty) return false;

            SaveSnapshot();
            brokenTileId = Grid[x, y].Id;
            Grid[x, y] = default;
            CheckGameStatus();
            return true;
        }

        public bool Undo()
        {
            if (!CanUndo) return false;

            _undoHead = (_undoHead - 1 + MaxUndoCapacity) % MaxUndoCapacity;
            _undoCount--;

            ref BoardSnapshot snap = ref _undoRingBuffer[_undoHead];
            Score = snap.Score;
            MovesRemaining = snap.MovesRemaining;
            _nextTileId = snap.NextTileId;
            ComboCount = 0;
            IsGameOver = false;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    Grid[x, y] = snap.Tiles[y * Width + x];
                }
            }

            return true;
        }

        public TileData? SpawnRandomTile()
        {
            var empty = GetEmptyCells();
            if (empty.Count == 0) return null;

            var cell = empty[_random.Next(empty.Count)];
            int value = _random.Next(10) == 0 ? 4 : 2;
            var tile = new TileData(_nextTileId++, value, TileType.Normal);
            Grid[cell.x, cell.y] = tile;
            return tile;
        }

        private void SaveSnapshot()
        {
            ref BoardSnapshot snap = ref _undoRingBuffer[_undoHead];
            snap.Score = Score;
            snap.MovesRemaining = MovesRemaining;
            snap.NextTileId = _nextTileId;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    snap.Tiles[y * Width + x] = Grid[x, y];
                }
            }

            _undoHead = (_undoHead + 1) % MaxUndoCapacity;
            if (_undoCount < MaxUndoCapacity) _undoCount++;
        }

        private void PopDiscardSnapshot()
        {
            if (_undoCount > 0)
            {
                _undoHead = (_undoHead - 1 + MaxUndoCapacity) % MaxUndoCapacity;
                _undoCount--;
            }
        }

        private void CheckGameStatus()
        {
            if (MovesRemaining <= 0)
            {
                IsGameOver = true;
                return;
            }

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (Grid[x, y].IsEmpty) return;
                }
            }

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    TileData cur = Grid[x, y];
                    if (cur.IsObstacle) continue;

                    if (x + 1 < Width && !Grid[x + 1, y].IsObstacle && Grid[x + 1, y].Value == cur.Value)
                        return;
                    if (y + 1 < Height && !Grid[x, y + 1].IsObstacle && Grid[x, y + 1].Value == cur.Value)
                        return;
                }
            }

            IsGameOver = true;
        }

        public List<(int x, int y)> GetEmptyCells()
        {
            var list = new List<(int x, int y)>();
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (Grid[x, y].IsEmpty) list.Add((x, y));
                }
            }
            return list;
        }

        public bool IsInBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
    }
}
