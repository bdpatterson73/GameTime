namespace GameTime.Models;

public enum CellState { Hidden, Revealed, Flagged }

public sealed class MsCell
{
    public int Row { get; init; }
    public int Col { get; init; }
    public bool IsMine { get; set; }
    public int AdjacentMines { get; set; }
    public CellState State { get; set; } = CellState.Hidden;
}

public sealed class MinesweeperGame
{
    public int Rows { get; }
    public int Cols { get; }
    public int MineCount { get; }
    public MsCell[,] Cells { get; }
    public bool IsLost { get; private set; }
    public bool MinesPlaced { get; private set; }
    public MsCell? ExplodedCell { get; private set; }

    private readonly Random _random;

    public MinesweeperGame(int rows = 16, int cols = 16, int mines = 40, int? seed = null)
    {
        Rows = rows;
        Cols = cols;
        MineCount = mines;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        Cells = new MsCell[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                Cells[r, c] = new MsCell { Row = r, Col = c };
    }

    public IEnumerable<MsCell> AllCells
    {
        get
        {
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    yield return Cells[r, c];
        }
    }

    public IEnumerable<MsCell> Neighbors(MsCell cell)
    {
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int r = cell.Row + dr, c = cell.Col + dc;
                if (r >= 0 && r < Rows && c >= 0 && c < Cols)
                    yield return Cells[r, c];
            }
    }

    public bool IsWon => !IsLost && AllCells.All(c => c.IsMine || c.State == CellState.Revealed);

    public int FlagCount => AllCells.Count(c => c.State == CellState.Flagged);
    public int HiddenCount => AllCells.Count(c => c.State == CellState.Hidden);

    /// <summary>Places mines avoiding the first-clicked cell and its neighbors.</summary>
    private void PlaceMines(MsCell safe)
    {
        var exclusion = new HashSet<MsCell>(Neighbors(safe)) { safe };
        var candidates = AllCells.Where(c => !exclusion.Contains(c)).ToList();
        for (int i = 0; i < MineCount && candidates.Count > 0; i++)
        {
            int idx = _random.Next(candidates.Count);
            candidates[idx].IsMine = true;
            candidates.RemoveAt(idx);
        }
        foreach (var cell in AllCells)
            cell.AdjacentMines = Neighbors(cell).Count(n => n.IsMine);
        MinesPlaced = true;
    }

    /// <summary>Reveals a cell (flood-filling zeros). Returns all newly revealed cells.</summary>
    public List<MsCell> Reveal(MsCell cell)
    {
        var revealed = new List<MsCell>();
        if (IsLost || cell.State != CellState.Hidden) return revealed;
        if (!MinesPlaced) PlaceMines(cell);

        if (cell.IsMine)
        {
            cell.State = CellState.Revealed;
            IsLost = true;
            ExplodedCell = cell;
            revealed.Add(cell);
            return revealed;
        }

        var queue = new Queue<MsCell>();
        queue.Enqueue(cell);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (c.State != CellState.Hidden) continue;
            c.State = CellState.Revealed;
            revealed.Add(c);
            if (c.AdjacentMines == 0)
                foreach (var n in Neighbors(c))
                    if (n.State == CellState.Hidden && !n.IsMine)
                        queue.Enqueue(n);
        }
        return revealed;
    }

    public void ToggleFlag(MsCell cell)
    {
        if (IsLost || cell.State == CellState.Revealed) return;
        cell.State = cell.State == CellState.Flagged ? CellState.Hidden : CellState.Flagged;
    }
}
