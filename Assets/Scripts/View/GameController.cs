using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GridChallenge.Core;
using GridChallenge.Input;

namespace GridChallenge.View
{

    public class GameController : MonoBehaviour
    {
        [Header("Grid Configuration")]
        [SerializeField] private int gridWidth = 4;
        [SerializeField] private int gridHeight = 4;
        [SerializeField] private int initialMoves = 30;
        [SerializeField] private int targetScore = 2048;
        [SerializeField] private int obstacleCount = 1;

        [Header("Visual Settings")]
        [SerializeField] private float slideDuration = 0.12f;
        [SerializeField] private Vector2 cellSize = new Vector2(80, 80);
        [SerializeField] private Vector2 cellSpacing = new Vector2(10, 10);

        [Header("Prefabs")]
        [SerializeField] private TileView tilePrefab;
        [SerializeField] private GameObject cellSlotPrefab;

        [Header("UI References")]
        [SerializeField] private RectTransform boardContainer;
        [SerializeField] private SwipeDetector swipeDetector;
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI movesText;
        [SerializeField] private TextMeshProUGUI comboText;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button hammerButton;
        [SerializeField] private TextMeshProUGUI hammerButtonText;
        [SerializeField] private Button restartButton;
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private TextMeshProUGUI gameOverTitle;

        private GridBoard _board;
        private readonly Dictionary<int, TileView> _activeTiles = new Dictionary<int, TileView>();
        private bool _isBusyAnimating = false;
        private bool _isHammerModeActive = false;

        private void Start()
        {
            if (restartButton) restartButton.onClick.AddListener(StartNewGame);
            if (undoButton) undoButton.onClick.AddListener(OnUndoClicked);
            if (hammerButton) hammerButton.onClick.AddListener(OnToggleHammerPowerUp);

            StartNewGame();
        }

        public void StartNewGame()
        {
            _board = new GridBoard(gridWidth, gridHeight, initialMoves, targetScore);
            _board.Initialize(initialTileCount: 2, obstacleCount: obstacleCount);

            ClearVisualTiles();
            CreateCellBackgrounds();
            SyncVisualTilesFromBoard();
            UpdateHUD();

            if (gameOverPanel) gameOverPanel.SetActive(false);
            _isHammerModeActive = false;
            UpdateHammerButtonUI();

            if (swipeDetector != null)
            {
                swipeDetector.OnSwipe -= HandleSwipe;
                swipeDetector.OnSwipe += HandleSwipe;
            }
        }

        private void HandleSwipe(Direction direction)
        {
            if (_isBusyAnimating || _board == null || _board.IsGameOver) return;
            if (_isHammerModeActive)
            {
                CancelHammerMode();
                return;
            }

            if (_board.TryMove(direction, out List<TileMove> moves, out TileData? spawnedTile))
            {
                StartCoroutine(AnimateTurn(moves, spawnedTile));
            }
        }

        private IEnumerator AnimateTurn(List<TileMove> moves, TileData? spawnedTile)
        {
            _isBusyAnimating = true;

            var mergeTargets = new List<TileMove>();
            foreach (var move in moves)
            {
                if (_activeTiles.TryGetValue(move.TileId, out TileView view))
                {
                    Vector2 targetPos = GetCellAnchoredPosition(move.ToX, move.ToY);
                    view.MoveTo(targetPos, new Vector2Int(move.ToX, move.ToY), slideDuration);
                }

                if (move.Merged)
                {
                    mergeTargets.Add(move);
                }
            }

            yield return new WaitForSeconds(slideDuration);

            foreach (var merge in mergeTargets)
            {
                if (_activeTiles.TryGetValue(merge.TileId, out TileView sourceView))
                {
                    _activeTiles.Remove(merge.TileId);
                    Destroy(sourceView.gameObject);
                }

                if (_activeTiles.TryGetValue(merge.TargetTileId, out TileView targetView))
                {
                    targetView.UpdateValue(merge.ResultValue);
                }
            }

            if (spawnedTile.HasValue)
            {
                var data = spawnedTile.Value;
                for (int x = 0; x < _board.Width; x++)
                {
                    for (int y = 0; y < _board.Height; y++)
                    {
                        if (_board.Grid[x, y].Id == data.Id)
                        {
                            CreateTileView(data, x, y);
                            break;
                        }
                    }
                }
            }

            UpdateHUD();
            _isBusyAnimating = false;

            if (_board.IsGameOver || _board.IsGameWon)
            {
                ShowGameOverModal(_board.IsGameWon);
            }
        }

        public void OnUndoClicked()
        {
            if (_isBusyAnimating || _board == null || !_board.CanUndo) return;

            if (_board.Undo())
            {
                ClearVisualTiles();
                SyncVisualTilesFromBoard();
                UpdateHUD();
                if (gameOverPanel) gameOverPanel.SetActive(false);
            }
        }

        public void OnToggleHammerPowerUp()
        {
            if (_isBusyAnimating || _board == null || _board.IsGameOver) return;
            _isHammerModeActive = !_isHammerModeActive;
            UpdateHammerButtonUI();
        }

        public void OnTileClicked(TileView clickedTile)
        {
            if (!_isHammerModeActive || _board == null || _board.IsGameOver) return;

            Vector2Int pos = clickedTile.GridPosition;
            if (_board.BreakTile(pos.x, pos.y, out int brokenId))
            {
                if (_activeTiles.TryGetValue(brokenId, out TileView view))
                {
                    _activeTiles.Remove(brokenId);
                    Destroy(view.gameObject);
                }
                CancelHammerMode();
                UpdateHUD();
            }
        }

        private void CancelHammerMode()
        {
            _isHammerModeActive = false;
            UpdateHammerButtonUI();
        }

        private void UpdateHammerButtonUI()
        {
            if (hammerButtonText)
            {
                hammerButtonText.text = _isHammerModeActive ? "Click Tile!" : "Hammer";
            }
        }

        private void UpdateHUD()
        {
            if (scoreText) scoreText.text = $"Score: {_board.Score}";
            if (movesText) movesText.text = $"Moves: {_board.MovesRemaining}";
            if (undoButton) undoButton.interactable = _board.CanUndo;

            if (comboText)
            {
                if (_board.ComboCount > 1)
                {
                    comboText.gameObject.SetActive(true);
                    comboText.text = $"Combo x{_board.ComboCount}!";
                }
                else
                {
                    comboText.gameObject.SetActive(false);
                }
            }
        }

        private void ShowGameOverModal(bool won)
        {
            if (gameOverPanel)
            {
                gameOverPanel.SetActive(true);
                if (gameOverTitle)
                {
                    gameOverTitle.text = won ? "VICTORY!" : "GAME OVER";
                    gameOverTitle.color = won ? new Color(0.93f, 0.76f, 0.18f) : new Color(0.9f, 0.3f, 0.3f);
                }
            }
        }

        private void SyncVisualTilesFromBoard()
        {
            for (int x = 0; x < _board.Width; x++)
            {
                for (int y = 0; y < _board.Height; y++)
                {
                    var data = _board.Grid[x, y];
                    if (!data.IsEmpty)
                    {
                        CreateTileView(data, x, y);
                    }
                }
            }
        }

        private void CreateTileView(TileData data, int x, int y)
        {
            if (boardContainer == null) return;

            TileView view;
            if (tilePrefab != null)
            {
                view = Instantiate(tilePrefab, boardContainer);
            }
            else
            {
                var go = new GameObject($"Tile_{data.Id}", typeof(RectTransform), typeof(Image), typeof(TileView), typeof(Button));
                go.transform.SetParent(boardContainer, false);
                view = go.GetComponent<TileView>();
            }

            Vector2 pos = GetCellAnchoredPosition(x, y);
            view.Setup(data, new Vector2Int(x, y), pos, cellSize);

            var btn = view.GetComponent<Button>();
            if (btn != null)
            {
                btn.transition = Selectable.Transition.None;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnTileClicked(view));
            }

            _activeTiles[data.Id] = view;
        }

        private void CreateCellBackgrounds()
        {
            if (boardContainer == null) return;

            for (int i = boardContainer.childCount - 1; i >= 0; i--)
            {
                var child = boardContainer.GetChild(i);
                if (child.name.StartsWith("Slot_"))
                {
                    Destroy(child.gameObject);
                }
            }

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    GameObject slot;
                    if (cellSlotPrefab != null)
                    {
                        slot = Instantiate(cellSlotPrefab, boardContainer);
                        slot.name = $"Slot_{x}_{y}";
                    }
                    else
                    {
                        slot = new GameObject($"Slot_{x}_{y}", typeof(RectTransform), typeof(Image));
                        slot.transform.SetParent(boardContainer, false);
                        slot.GetComponent<Image>().color = new Color(0.80f, 0.75f, 0.71f, 0.6f);
                    }

                    var rect = slot.GetComponent<RectTransform>();
                    rect.sizeDelta = cellSize;
                    rect.anchoredPosition = GetCellAnchoredPosition(x, y);
                }
            }
        }

        private Vector2 GetCellAnchoredPosition(int x, int y)
        {
            float totalW = gridWidth * cellSize.x + (gridWidth - 1) * cellSpacing.x;
            float totalH = gridHeight * cellSize.y + (gridHeight - 1) * cellSpacing.y;

            float originX = -totalW * 0.5f + cellSize.x * 0.5f;
            float originY = -totalH * 0.5f + cellSize.y * 0.5f;

            return new Vector2(originX + x * (cellSize.x + cellSpacing.x), originY + y * (cellSize.y + cellSpacing.y));
        }

        private void ClearVisualTiles()
        {
            foreach (var kvp in _activeTiles)
            {
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            }
            _activeTiles.Clear();
        }
    }
}
