namespace GameTime.Models;

public sealed class Creature
{
    public double X { get; set; }
    public double Y { get; set; }
    public bool Alive { get; set; } = true;
    /// <summary>Seconds since being shot; drives the death flash/shrink animation.</summary>
    public double DeathTimer { get; set; }
    public int ColorIndex { get; init; }
    public double WanderTargetX { get; set; }
    public double WanderTargetY { get; set; }
    public double Phase { get; init; } // animation phase offset
}

/// <summary>
/// Randomly generated maze world with wandering creature targets.
/// Grid cells: 0 = floor, >=1 = wall (value selects wall color variant).
/// World coordinates are in cell units; cell centers are at (col + 0.5, row + 0.5).
/// </summary>
public sealed class MazeWorld
{
    public int Size { get; }
    public int[,] Grid { get; }
    public List<Creature> Creatures { get; } = new();
    public double SpawnX { get; }
    public double SpawnY { get; }

    private readonly Random _random;

    public MazeWorld(int size = 21, int creatureCount = 10, int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        if (size % 2 == 0) size++;
        Size = size;
        Grid = new int[size, size];

        // Fill with walls, carve with recursive backtracker on odd cells.
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                Grid[r, c] = 1 + (r * 31 + c * 17) % 3; // wall color variety

        var stack = new Stack<(int r, int c)>();
        var start = (r: 1, c: 1);
        Grid[start.r, start.c] = 0;
        stack.Push(start);
        var dirs = new (int dr, int dc)[] { (-2, 0), (2, 0), (0, -2), (0, 2) };

        while (stack.Count > 0)
        {
            var (r, c) = stack.Peek();
            var options = dirs
                .Select(d => (nr: r + d.dr, nc: c + d.dc, mr: r + d.dr / 2, mc: c + d.dc / 2))
                .Where(o => o.nr > 0 && o.nr < size - 1 && o.nc > 0 && o.nc < size - 1 && Grid[o.nr, o.nc] != 0)
                .ToList();
            if (options.Count == 0) { stack.Pop(); continue; }
            var pick = options[_random.Next(options.Count)];
            Grid[pick.mr, pick.mc] = 0;
            Grid[pick.nr, pick.nc] = 0;
            stack.Push((pick.nr, pick.nc));
        }

        // Knock out some extra walls to create loops (nicer to watch than a perfect maze).
        int extraOpenings = size / 3;
        for (int i = 0; i < extraOpenings; i++)
        {
            int r = 1 + _random.Next(size - 2), c = 1 + _random.Next(size - 2);
            if (Grid[r, c] != 0 && CountFloorNeighbors(r, c) >= 2)
                Grid[r, c] = 0;
        }

        // Tag a handful of corridor-facing walls as graffiti walls ("Brian was here").
        const int GraffitiWall = 4;
        var wallCandidates = new List<(int r, int c)>();
        for (int r = 1; r < size - 1; r++)
            for (int c = 1; c < size - 1; c++)
                if (Grid[r, c] is >= 1 and < GraffitiWall && CountFloorNeighbors(r, c) >= 1)
                    wallCandidates.Add((r, c));
        int graffitiCount = Math.Min(8, wallCandidates.Count);
        for (int i = 0; i < graffitiCount; i++)
        {
            int idx = _random.Next(wallCandidates.Count);
            var (r, c) = wallCandidates[idx];
            wallCandidates.RemoveAt(idx);
            Grid[r, c] = GraffitiWall;
        }

        SpawnX = 1.5;
        SpawnY = 1.5;

        // Spawn creatures on floor cells away from the spawn point.
        var floorCells = new List<(int r, int c)>();
        for (int r = 1; r < size - 1; r++)
            for (int c = 1; c < size - 1; c++)
                if (Grid[r, c] == 0 && Math.Abs(r - 1) + Math.Abs(c - 1) > 6)
                    floorCells.Add((r, c));

        for (int i = 0; i < creatureCount && floorCells.Count > 0; i++)
        {
            int idx = _random.Next(floorCells.Count);
            var (r, c) = floorCells[idx];
            floorCells.RemoveAt(idx);
            Creatures.Add(new Creature
            {
                X = c + 0.5,
                Y = r + 0.5,
                WanderTargetX = c + 0.5,
                WanderTargetY = r + 0.5,
                ColorIndex = i % 5,
                Phase = _random.NextDouble() * Math.PI * 2
            });
        }
    }

    private int CountFloorNeighbors(int r, int c)
    {
        int n = 0;
        if (r > 0 && Grid[r - 1, c] == 0) n++;
        if (r < Size - 1 && Grid[r + 1, c] == 0) n++;
        if (c > 0 && Grid[r, c - 1] == 0) n++;
        if (c < Size - 1 && Grid[r, c + 1] == 0) n++;
        return n;
    }

    public bool IsWall(double x, double y)
    {
        int c = (int)x, r = (int)y;
        if (r < 0 || r >= Size || c < 0 || c >= Size) return true;
        return Grid[r, c] != 0;
    }

    public int AliveCount => Creatures.Count(c => c.Alive);

    /// <summary>Grid-stepping line-of-sight check between two world points.</summary>
    public bool HasLineOfSight(double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 1e-9) return true;
        int steps = (int)(dist * 8) + 1;
        for (int i = 1; i < steps; i++)
        {
            double t = (double)i / steps;
            if (IsWall(x0 + dx * t, y0 + dy * t)) return false;
        }
        return true;
    }

    /// <summary>Advances creature wandering. Creatures drift between adjacent floor cells.</summary>
    public void UpdateCreatures(double dt)
    {
        foreach (var cr in Creatures)
        {
            if (!cr.Alive) { cr.DeathTimer += dt; continue; }

            double dx = cr.WanderTargetX - cr.X, dy = cr.WanderTargetY - cr.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            const double speed = 0.7;
            if (dist < 0.05)
            {
                // Pick a random adjacent floor cell.
                int r = (int)cr.Y, c = (int)cr.X;
                var options = new List<(int r, int c)>();
                foreach (var (dr, dc) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1), (0, 0) })
                    if (Grid[r + dr, c + dc] == 0)
                        options.Add((r + dr, c + dc));
                var pick = options[_random.Next(options.Count)];
                cr.WanderTargetX = pick.c + 0.5;
                cr.WanderTargetY = pick.r + 0.5;
            }
            else
            {
                cr.X += dx / dist * speed * dt;
                cr.Y += dy / dist * speed * dt;
            }
        }
    }

    /// <summary>BFS shortest path between cells; returns cell centers, excluding the start cell.</summary>
    public List<(double x, double y)> FindPath(int fromR, int fromC, int toR, int toC)
    {
        var prev = new Dictionary<(int, int), (int, int)>();
        var visited = new HashSet<(int, int)> { (fromR, fromC) };
        var queue = new Queue<(int r, int c)>();
        queue.Enqueue((fromR, fromC));
        bool found = fromR == toR && fromC == toC;

        while (queue.Count > 0 && !found)
        {
            var (r, c) = queue.Dequeue();
            foreach (var (dr, dc) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                int nr = r + dr, nc = c + dc;
                if (nr < 0 || nr >= Size || nc < 0 || nc >= Size) continue;
                if (Grid[nr, nc] != 0 || !visited.Add((nr, nc))) continue;
                prev[(nr, nc)] = (r, c);
                if (nr == toR && nc == toC) { found = true; break; }
                queue.Enqueue((nr, nc));
            }
        }

        var path = new List<(double x, double y)>();
        if (!found) return path;
        var cur = (toR, toC);
        while (cur != (fromR, fromC))
        {
            path.Add((cur.Item2 + 0.5, cur.Item1 + 0.5));
            if (!prev.TryGetValue(cur, out cur)) break;
        }
        path.Reverse();
        return path;
    }
}
