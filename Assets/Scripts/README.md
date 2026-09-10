# Grid Puzzle Architecture & Code Deep-Dive Guide

This guide breaks down every script, data structure, algorithm, and design pattern in the project. It is written to give you a thorough, end-to-end understanding so you can confidently answer any technical or architectural question.

---

## Table of Contents
1. [Architectural Overview (MVP Pattern)](#1-architectural-overview-mvp-pattern)
2. [Script 1: GridBoard.cs (Core Simulation Model)](#2-script-1-gridboardcs-core-simulation-model)
3. [Script 2: SwipeDetector.cs (Input Pipeline)](#3-script-2-swipedetectorcs-input-pipeline)
4. [Script 3: TileView.cs (Visual Representation)](#4-script-3-tileviewcs-visual-representation)
5. [Script 4: GameController.cs (Presenter & Orchestrator)](#5-script-4-gamecontrollercs-presenter--orchestrator)
6. [Interview Q&A Prep (Common Technical Questions)](#6-interview-qa-prep-common-technical-questions)

---

## 1. Architectural Overview (MVP Pattern)

The project follows a strict **Model-View-Presenter (MVP)** decoupled architecture:

```
+-------------------------------------------------------------------+
|                        PRESENTATION LAYER                         |
|                                                                   |
|   [SwipeDetector]  ----(OnSwipe Event)---->  [GameController]     |
|   (Captures touch/keyboard)                  (Presenter)          |
|                                                   |               |
|                                        Controls   |   Instantiates|
|                                        Animations v   & Positions v
|                                             [TileView]    [HUD / UI]
+---------------------------------------------------|---------------+
                                                    |
                                    Calls Model     | Returns Move Deltas
                                    (TryMove / Undo)| (Tiles, Scores, Merges)
                                                    v
+-------------------------------------------------------------------+
|                     CORE DOMAIN LAYER (Pure C#)                   |
|                                                                   |
|   [GridBoard]                                                     |
|   - Zero UnityEngine imports (Runs in pure .NET / Unit tests)     |
|   - N x M 2D Matrix of TileData structs                           |
|   - Deterministic sliding, merging, & obstacle collisions         |
|   - Pre-allocated Ring-Buffer Undo (Zero GC allocations)          |
+-------------------------------------------------------------------+
```

### Why is Decoupling So Important?
1. **Mathematical Determinism**: Pure logic has zero dependencies on Unity frames, rendering, or physics. The simulation always produces identical results given identical inputs.
2. **Instant Testability**: Can be executed and tested in pure C# console or NUnit without launching Unity Play Mode or loading scene assets.
3. **Maintainability & Portability**: If you ever swap rendering (e.g. from UI Canvas to 2D Sprites, 3D meshes, or even a server-side multiplayer engine), the core `GridBoard` code remains 100% unchanged.

---

## 2. Script 1: `GridBoard.cs` (Core Simulation Model)

**Location**: `Assets/Scripts/Core/GridBoard.cs`  
**Namespace**: `GridChallenge.Core`  
**Role**: Pure C# domain model for the $N \times M$ puzzle simulation.

### 2.1. Key Data Structures

#### `TileData` (Struct)
```csharp
public struct TileData : IEquatable<TileData>
{
    public int Id;          // Unique identifier for persistent visual tracking
    public int Value;       // Number value (2, 4, 8, 16...)
    public TileType Type;   // Normal or Obstacle
}
```
- **Why a `struct` instead of a `class`?**
  - Structs are value types stored directly inline in the 2D array memory block.
  - Improves CPU cache locality and generates zero Garbage Collection (GC) pressure when copied or queried.

#### `TileMove` (Struct)
```csharp
public struct TileMove
{
    public int FromX, FromY;    // Starting cell coordinates
    public int ToX, ToY;        // Ending cell coordinates
    public int TileId;          // Which tile moved
    public bool Merged;         // Did it merge with another tile?
    public int TargetTileId;    // The tile it merged into
    public int ResultValue;     // Value after merge (e.g. 4)
}
```
- **Why is this returned?**
  - It forms a **Delta Payload**. Instead of forcing the view to re-scan the entire board, `GridBoard.TryMove()` tells the presenter exactly which tiles moved and merged so animations can be played smoothly.

#### `BoardSnapshot` (Struct)
```csharp
public struct BoardSnapshot
{
    public TileData[] Tiles;    // Flattened 1D array of all cells
    public int Score;           // Score at that point in time
    public int MovesRemaining;  // Remaining moves counter
    public int NextTileId;      // Id generator state
}
```

---

### 2.2. Core Algorithms & Logic

#### 1. Directional Slide & Merge (`TryMove`)
The algorithm processes the board in a single pass ($O(N \times M)$ runtime):
- **Traversal Order Matters**:
  - If swiping **Right** ($dx = +1$), we must scan from right to left ($x = Width - 1$ down to $0$). Otherwise, inner tiles would collide with unshifted outer tiles!
  - If swiping **Up** ($dy = +1$), we scan from top to bottom ($y = Height - 1$ down to $0$).
- **Obstacle Check**:
  - Obstacles (`Type == TileType.Obstacle`) never move and cannot merge.
  - A sliding tile stops immediately when it hits an obstacle or another tile.
- **Single-Merge Guard (`mergedThisTurn[,]`)**:
  - In games like 2048, a row like `[2, 2, 4, 0]` swiped left should become `[4, 4, 0, 0]`, NOT `[8, 0, 0, 0]`.
  - The 2D boolean array `mergedThisTurn[x, y]` guarantees each cell can only participate in one merge per swipe.

#### 2. Deterministic History & Zero-Allocation Undo
- **The Problem**: Storing a full history of game states usually requires `new List<BoardSnapshot>()` and cloning arrays, causing heap allocations and garbage collection spikes.
- **The Solution**: A **Circular Ring Buffer**:
  ```csharp
  private readonly BoardSnapshot[] _undoRingBuffer; // Fixed size: 32
  private int _undoHead = 0;
  private int _undoCount = 0;
  ```
  - During constructor initialization, all 32 snapshot tile arrays are pre-allocated (`new TileData[Width * Height]`).
  - Saving a snapshot simply overwrites slot `_undoHead` in-place and advances `_undoHead = (_undoHead + 1) % 32`.
  - **GC Allocations per Move**: `0 bytes`.
  - **GC Allocations per Undo**: `0 bytes`.
  - Memory leak risk: **Zero**.

#### 3. Combos & Multipliers (Gameplay Hook)
- Counts how many distinct merges occurred in the single swipe (`turnMerges`).
- If `turnMerges > 1`, applies a combo multiplier:
  ```csharp
  int comboMultiplier = turnMerges > 1 ? turnMerges : 1;
  Score += turnScoreGained * comboMultiplier;
  ```

#### 4. Hammer Power-Up (`BreakTile`)
- Takes grid coordinates `(x, y)`.
- If a tile or obstacle exists there, saves a snapshot (so the hammer action is undoable!), clears the cell, and returns the broken tile ID to the view.

---

## 3. Script 2: `SwipeDetector.cs` (Input Pipeline)

**Location**: `Assets/Scripts/Input/SwipeDetector.cs`  
**Namespace**: `GridChallenge.Input`  
**Role**: Captures player gestures and discretizes continuous finger/pointer movement into 4 discrete directions (`Up`, `Down`, `Left`, `Right`).

### 3.1. How It Works
- Implements Unity EventSystem interfaces:
  - `IPointerDownHandler`: Records start touch/click position (`eventData.position`) and timestamp (`Time.unscaledTime`).
  - `IDragHandler`: Continuously tracks drag vector `delta = current - start`. If `delta.magnitude >= minSwipeDistance (40px)`, immediately triggers the swipe and sets `_isSwiping = false` to prevent multiple swipes in a single continuous stroke.
  - `IPointerUpHandler`: Fallback trigger if the drag was fast and released within `maxSwipeTime (1.0s)`.

### 3.2. Discrete Direction Quantization
```csharp
private void ResolveSwipe(Vector2 delta)
{
    if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
    {
        OnSwipe?.Invoke(delta.x > 0 ? Direction.Right : Direction.Left);
    }
    else
    {
        OnSwipe?.Invoke(delta.y > 0 ? Direction.Up : Direction.Down);
    }
}
```
- Compares absolute horizontal vs vertical displacement:
  - If $|dx| > |dy|$: either `Right` (if $dx > 0$) or `Left` (if $dx < 0$).
  - If $|dy| > |dx|$: either `Up` (if $dy > 0$) or `Down` (if $dy < 0$).
- Cleanly eliminates diagonal ambiguity.

### 3.3. Cross-Platform Desktop Fallback
- In `Update()`, checks keyboard WASD / Arrow keys using `#if ENABLE_INPUT_SYSTEM` (supporting Unity's New Input System) with legacy fallback, enabling seamless testing in the Unity Editor.

---

## 4. Script 3: `TileView.cs` (Visual Representation)

**Location**: `Assets/Scripts/View/TileView.cs`  
**Namespace**: `GridChallenge.View`  
**Role**: Controls the visual GameObject of an individual tile, including animations and color themes.

### 4.1. Key Properties
- `TileId`: Matches `TileData.Id` in `GridBoard`. Allows `GameController` to locate and move this specific view when `GridBoard` reports that tile moved.
- `Value`: Current numerical value displayed.
- `GridPosition`: Current logical coordinate `(x, y)`.

### 4.2. Animations
All animations use lightweight Coroutines without third-party dependencies:
1. **Slide Animation (`AnimateMove`)**:
   - Linearly interpolates anchored position over `slideDuration (0.12s)` using an **Ease-Out Quad** curve:
     ```csharp
     float t = elapsed / duration;
     t = t * (2f - t); // Ease-Out Quad formula
     _rectTransform.anchoredPosition = Vector2.LerpUnclamped(start, targetPos, t);
     ```
2. **Spawn Animation (`AnimatePop`)**:
   - Scales the tile from 0 to 1 with a smooth sine pop: `Mathf.Sin(t * Mathf.PI * 0.5f)`.
3. **Merge Punch Animation (`AnimatePunch`)**:
   - Scales up to $1.2\times$ and returns to $1.0\times$ over $0.15\text{s}$ when two tiles merge.

### 4.3. Color Palette System
- Matches numbers ($2, 4, 8, 16, \dots, 2048$) to curated warm modern colors.
- If `IsObstacle == true`, overrides color to dark slate (`#474751`) and displays label `"BLOCK"`.

---

## 5. Script 4: `GameController.cs` (Presenter & Orchestrator)

**Location**: `Assets/Scripts/View/GameController.cs`  
**Namespace**: `GridChallenge.View`  
**Role**: The central MVP Presenter bridging the pure C# `GridBoard` model, `SwipeDetector`, and the visual `TileView`s / UI HUD.

### 5.1. Initialization (`StartNewGame`)
1. Instantiates `_board = new GridBoard(gridWidth, gridHeight, initialMoves, targetScore)`.
2. Calls `_board.Initialize()` to place obstacles and initial random tiles.
3. Clears any previous visual tiles (`ClearVisualTiles`).
4. Generates cell background slots (`CreateCellBackgrounds`).
5. Instantiates visual `TileView`s matching `_board.Grid` state (`SyncVisualTilesFromBoard`).
6. Updates HUD labels (Score, Moves, Undo button state).

### 5.2. Turn Execution Flow (`HandleSwipe` -> `AnimateTurn`)
1. **Input Guard**: If `_isBusyAnimating == true` or game is over, ignores swipe input. Prevents race conditions and state desync while animations play.
2. **Execute Simulation**: Calls `_board.TryMove(direction, out moves, out spawnedTile)`.
3. **Sequence Animations**:
   - **Step A (Move)**: For each item in `moves`, finds the visual `TileView` by `move.TileId` and calls `view.MoveTo()`.
   - **Wait**: `yield return new WaitForSeconds(slideDuration)`.
   - **Step B (Merge)**: For merged tiles, destroys the consumed tile view and calls `targetView.UpdateValue(resultValue)` with punch animation.
   - **Step C (Spawn)**: Instantiates a new `TileView` for `spawnedTile` with pop animation.
4. **Update HUD**: Refreshes Score, Moves Remaining, and Combo banner.
5. **Game Over Check**: Shows Victory or Game Over modal if conditions are met.

### 5.3. Deterministic Undo Handling (`OnUndoClicked`)
1. User clicks `Undo` button.
2. Checks `_board.CanUndo`.
3. Calls `_board.Undo()`.
4. Destroys current visual tiles and re-syncs directly from `_board.Grid`.
5. Updates Score and Moves to their previous values.

### 5.4. Hammer Power-Up Handling
1. Clicking `Hammer` button toggles `_isHammerModeActive`.
2. The button label changes to `"Click Tile!"`.
3. When player clicks any tile in the board, `OnTileClicked(TileView)` fires.
4. Calls `_board.BreakTile(pos.x, pos.y, out brokenId)`.
5. Visual tile is destroyed immediately, hammer mode turns off, and the HUD updates.

---

## 6. Interview Q&A Prep (Common Technical Questions)

### Q1: "Why did you separate `GridBoard` from MonoBehaviour?"
> **Answer**:  
> *"Separating core simulation into pure C# enforces Single Responsibility Principle (SRP) and Model-View-Presenter (MVP). It makes the game logic 100% deterministic, immune to framerate drops, and testable outside of Unity without scene dependencies. The rendering layer simply visualizes deltas that the core simulation generates."*

### Q2: "How does your Undo system handle memory safety and avoid GC spikes?"
> **Answer**:  
> *"Instead of creating new snapshot objects on the heap during every move, I implemented a 32-slot circular ring buffer. All snapshot arrays are pre-allocated at initialization. Taking a snapshot is an in-place array memory copy, which costs 0 heap allocations per move and 0 heap allocations on undo, eliminating memory leaks and GC pauses entirely."*

### Q3: "What is the time complexity of the swipe resolution algorithm?"
> **Answer**:  
> *"It runs in $O(N \times M)$ time complexity, where $N$ is width and $M$ is height. Each cell is inspected once along the direction axis, sliding up to $N$ steps. For a 4x4 grid (16 cells), this takes less than a microsecond."*

### Q4: "How do you ensure tiles slide correctly without colliding with tiles behind them?"
> **Answer**:  
> *"The traversal order dynamically changes based on the swipe direction. When swiping Right, we traverse columns from right to left (outermost first). When swiping Up, we traverse rows from top to bottom. This guarantees that destination cells are cleared before inner tiles calculate their slide path."*

### Q5: "How does the gesture detection prevent diagonal ambiguity?"
> **Answer**:  
> *"In `SwipeDetector.cs`, once the drag vector exceeds the minimum 40-pixel threshold, we compare `Mathf.Abs(delta.x)` against `Mathf.Abs(delta.y)`. If the horizontal magnitude is greater, it snaps strictly to Left or Right; otherwise, it snaps strictly to Up or Down. We also lock `_isSwiping` immediately after triggering to ensure one swipe gesture produces exactly one discrete command."*

### Q6: "What is your candidate initiative hook?"
> **Answer**:  
> *"I implemented three complementary mechanics:  
> 1. **Obstacle Tiles (`BLOCK`)**: Fixed blockers that disrupt straight slides and require routing around.  
> 2. **Synergistic Combos**: Merging more than one pair in a single swipe applies a scaling multiplier to the turn score with animated banner feedback.  
> 3. **Hammer Power-Up**: Allows players to destroy any tile or obstacle, fully integrated into the deterministic Undo system."*
