using GameTime.Models;

namespace GameTime;

public enum BotState { Patrolling, Aiming, Firing }

/// <summary>A fired shot's visual effect: tracer toward a world point, and an explosion on hit.</summary>
public sealed class ShotEffect
{
    public double X { get; init; }
    public double Y { get; init; }
    public bool Hit { get; init; }
    public int ColorIndex { get; init; }
    public double Age { get; set; }
}

/// <summary>
/// Autonomous maze-roaming shooter. Patrols via BFS paths to random cells,
/// and when a living creature is visible within its view cone it stops,
/// turns to aim, and fires (with a small miss chance).
/// </summary>
public sealed class ShooterBot
{
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Angle { get; private set; }
    public BotState State { get; private set; } = BotState.Patrolling;
    public Creature? Target { get; private set; }

    /// <summary>Seconds remaining of muzzle-flash visibility.</summary>
    public double MuzzleFlash { get; private set; }
    public List<ShotEffect> Shots { get; } = new();
    public int ShotsFired { get; private set; }
    public int Kills { get; private set; }

    public event Action? Fired;
    public event Action<Creature>? CreatureKilled;

    private readonly MazeWorld _world;
    private readonly Random _random = new();
    private readonly List<(double x, double y)> _path = new();
    private double _fireCooldown;
    private const double MoveSpeed = 1.9;       // cells/sec (scaled externally)
    private const double TurnSpeed = 2.6;       // rad/sec
    private const double AimTurnSpeed = 4.5;
    private const double ViewCone = 0.42;       // rad half-angle for detection
    private const double FireRange = 7.0;
    private const double MissChance = 0.12;

    public ShooterBot(MazeWorld world)
    {
        _world = world;
        X = world.SpawnX;
        Y = world.SpawnY;
        Angle = 0;
    }

    public void Update(double dt, double speedScale)
    {
        if (MuzzleFlash > 0) MuzzleFlash -= dt;
        if (_fireCooldown > 0) _fireCooldown -= dt;
        foreach (var s in Shots) s.Age += dt;
        Shots.RemoveAll(s => s.Age > 0.8);

        switch (State)
        {
            case BotState.Patrolling:
                AcquireTarget();
                if (Target is not null) { State = BotState.Aiming; break; }
                Patrol(dt * speedScale);
                break;

            case BotState.Aiming:
                if (Target is null || !Target.Alive || !CanSee(Target))
                {
                    Target = null;
                    State = BotState.Patrolling;
                    break;
                }
                if (TurnToward(TargetAngle(Target), dt * speedScale * AimTurnSpeed))
                    State = BotState.Firing;
                break;

            case BotState.Firing:
                if (Target is null || !Target.Alive || !CanSee(Target))
                {
                    Target = null;
                    State = BotState.Patrolling;
                    break;
                }
                TurnToward(TargetAngle(Target), dt * speedScale * AimTurnSpeed);
                if (_fireCooldown <= 0)
                {
                    Fire();
                }
                break;
        }
    }

    private void Fire()
    {
        ShotsFired++;
        MuzzleFlash = 0.16;
        _fireCooldown = 0.55;
        Fired?.Invoke();

        if (Target is null) return;
        bool hit = _random.NextDouble() >= MissChance;
        Shots.Add(new ShotEffect { X = Target.X, Y = Target.Y, Hit = hit, ColorIndex = Target.ColorIndex });
        if (hit)
        {
            Target.Alive = false;
            Kills++;
            CreatureKilled?.Invoke(Target);
            Target = null;
            State = BotState.Patrolling;
        }
    }

    private double TargetAngle(Creature c) => Math.Atan2(c.Y - Y, c.X - X);

    /// <summary>Rotates toward the desired angle; true when close enough.</summary>
    private bool TurnToward(double desired, double maxDelta)
    {
        double diff = NormalizeAngle(desired - Angle);
        if (Math.Abs(diff) <= maxDelta)
        {
            Angle = desired;
            return Math.Abs(diff) < 0.04;
        }
        Angle = NormalizeAngle(Angle + Math.Sign(diff) * maxDelta);
        return false;
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= Math.PI * 2;
        while (a < -Math.PI) a += Math.PI * 2;
        return a;
    }

    private bool CanSee(Creature c)
    {
        double dx = c.X - X, dy = c.Y - Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > FireRange) return false;
        return _world.HasLineOfSight(X, Y, c.X, c.Y);
    }

    private void AcquireTarget()
    {
        Creature? best = null;
        double bestDist = double.MaxValue;
        foreach (var c in _world.Creatures)
        {
            if (!c.Alive) continue;
            double dx = c.X - X, dy = c.Y - Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > FireRange || dist >= bestDist) continue;
            double angDiff = Math.Abs(NormalizeAngle(Math.Atan2(dy, dx) - Angle));
            if (angDiff > ViewCone && dist > 1.2) continue; // very close creatures are noticed regardless
            if (!_world.HasLineOfSight(X, Y, c.X, c.Y)) continue;
            best = c;
            bestDist = dist;
        }
        Target = best;
    }

    private void Patrol(double dt)
    {
        if (_path.Count == 0)
        {
            PlanNewPath();
            if (_path.Count == 0) return;
        }

        var (tx, ty) = _path[0];
        double dx = tx - X, dy = ty - Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 0.08)
        {
            _path.RemoveAt(0);
            return;
        }

        // Turn toward the waypoint, move only when roughly facing it.
        double desired = Math.Atan2(dy, dx);
        TurnToward(desired, dt * TurnSpeed);
        if (Math.Abs(NormalizeAngle(desired - Angle)) < 0.5)
        {
            double step = Math.Min(MoveSpeed * dt, dist);
            double nx = X + dx / dist * step;
            double ny = Y + dy / dist * step;
            if (!_world.IsWall(nx, ny)) { X = nx; Y = ny; }
            else _path.Clear();
        }
    }

    private void PlanNewPath()
    {
        // Head toward the nearest living creature's area; fall back to a random floor cell.
        var alive = _world.Creatures.Where(c => c.Alive).ToList();
        int toR, toC;
        if (alive.Count > 0)
        {
            var nearest = alive.OrderBy(c => Math.Abs(c.X - X) + Math.Abs(c.Y - Y)).First();
            toR = (int)nearest.Y;
            toC = (int)nearest.X;
        }
        else
        {
            do
            {
                toR = 1 + _random.Next(_world.Size - 2);
                toC = 1 + _random.Next(_world.Size - 2);
            } while (_world.Grid[toR, toC] != 0);
        }

        _path.Clear();
        _path.AddRange(_world.FindPath((int)Y, (int)X, toR, toC));
    }
}
