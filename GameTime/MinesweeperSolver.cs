using GameTime.Models;

namespace GameTime;

public enum MsActionKind { Reveal, Flag, Guess }

public sealed record MsAction(MsActionKind Kind, MsCell Cell);

/// <summary>
/// Minesweeper auto-solver. Applies the two classic deterministic rules:
///  1. If a number's flags equal its value, all other hidden neighbors are safe.
///  2. If a number's hidden neighbors equal its remaining mines, flag them all.
/// When no deterministic move exists it guesses the hidden cell with the
/// lowest estimated mine probability.
/// </summary>
public sealed class MinesweeperSolver
{
    private readonly MinesweeperGame _game;
    private readonly Random _random = new();

    public MinesweeperSolver(MinesweeperGame game) => _game = game;

    public MsAction? ChooseAction()
    {
        if (_game.IsLost || _game.IsWon) return null;

        // First click: center is statistically decent and looks natural.
        if (!_game.MinesPlaced)
            return new MsAction(MsActionKind.Reveal, _game.Cells[_game.Rows / 2, _game.Cols / 2]);

        foreach (var cell in _game.AllCells)
        {
            if (cell.State != CellState.Revealed || cell.AdjacentMines == 0) continue;
            var neighbors = _game.Neighbors(cell).ToList();
            int flags = neighbors.Count(n => n.State == CellState.Flagged);
            var hidden = neighbors.Where(n => n.State == CellState.Hidden).ToList();
            if (hidden.Count == 0) continue;

            if (flags == cell.AdjacentMines)
                return new MsAction(MsActionKind.Reveal, hidden[0]);

            if (hidden.Count == cell.AdjacentMines - flags)
                return new MsAction(MsActionKind.Flag, hidden[0]);
        }

        // No deterministic move: guess the lowest-probability hidden cell.
        var guess = BestGuess();
        return guess is null ? null : new MsAction(MsActionKind.Guess, guess);
    }

    private MsCell? BestGuess()
    {
        var hiddenCells = _game.AllCells.Where(c => c.State == CellState.Hidden).ToList();
        if (hiddenCells.Count == 0) return null;

        int minesLeft = _game.MineCount - _game.FlagCount;
        double baseline = Math.Clamp((double)minesLeft / hiddenCells.Count, 0, 1);

        MsCell? best = null;
        double bestProb = double.MaxValue;
        foreach (var cell in hiddenCells)
        {
            // Estimate: max constraint probability from revealed neighbors, else baseline.
            double prob = baseline;
            bool constrained = false;
            foreach (var n in _game.Neighbors(cell))
            {
                if (n.State != CellState.Revealed || n.AdjacentMines == 0) continue;
                var nn = _game.Neighbors(n).ToList();
                int flags = nn.Count(x => x.State == CellState.Flagged);
                int hid = nn.Count(x => x.State == CellState.Hidden);
                if (hid == 0) continue;
                double p = (double)(n.AdjacentMines - flags) / hid;
                prob = constrained ? Math.Max(prob, p) : p;
                constrained = true;
            }
            // Prefer unconstrained "frontier-free" cells slightly when tied.
            if (prob < bestProb || (Math.Abs(prob - bestProb) < 1e-9 && _random.Next(2) == 0))
            {
                bestProb = prob;
                best = cell;
            }
        }
        return best;
    }
}
