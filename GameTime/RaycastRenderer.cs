using GameTime.Models;

namespace GameTime;

/// <summary>
/// Wolfenstein-style software renderer: DDA raycast walls, gradient
/// floor/ceiling, z-buffered billboard creature sprites, crosshair,
/// muzzle flash, and a minimap — all drawn into an int BGRA buffer.
/// </summary>
public sealed class RaycastRenderer
{
    public int Width { get; }
    public int Height { get; }
    public int[] Buffer { get; }

    private readonly double[] _zBuffer;
    private const double Fov = Math.PI / 3; // 60°

    private static readonly (byte r, byte g, byte b)[] WallColors =
    {
        (0, 0, 0),          // unused (floor)
        (158, 118, 86),     // brick brown
        (120, 128, 148),    // stone blue-gray
        (118, 140, 110)     // mossy green
    };

    private static readonly (byte r, byte g, byte b)[] CreatureColors =
    {
        (236, 86, 86),      // red
        (118, 200, 255),    // blue
        (140, 230, 120),    // green
        (255, 190, 80),     // orange
        (210, 120, 240)     // purple
    };

    public RaycastRenderer(int width, int height)
    {
        Width = width;
        Height = height;
        Buffer = new int[width * height];
        _zBuffer = new double[width];
    }

    public void Render(MazeWorld world, ShooterBot bot, double time)
    {
        DrawFloorAndCeiling();
        DrawWalls(world, bot);
        DrawCreatures(world, bot, time);
        DrawShotEffects(bot);
        DrawMuzzleFlash(bot);
        DrawCrosshair(bot);
        DrawMinimap(world, bot);
    }

    private static int Pack(byte r, byte g, byte b) => (255 << 24) | (r << 16) | (g << 8) | b;

    private void DrawFloorAndCeiling()
    {
        int half = Height / 2;
        for (int y = 0; y < half; y++)
        {
            double t = (double)y / half;
            int ceil = Pack((byte)(26 + 20 * t), (byte)(30 + 22 * t), (byte)(44 + 26 * t));
            int row = y * Width;
            for (int x = 0; x < Width; x++) Buffer[row + x] = ceil;
        }
        for (int y = half; y < Height; y++)
        {
            double t = (double)(y - half) / (Height - half);
            int floor = Pack((byte)(38 + 34 * t), (byte)(34 + 28 * t), (byte)(30 + 22 * t));
            int row = y * Width;
            for (int x = 0; x < Width; x++) Buffer[row + x] = floor;
        }
    }

    private void DrawWalls(MazeWorld world, ShooterBot bot)
    {
        double dirX = Math.Cos(bot.Angle), dirY = Math.Sin(bot.Angle);
        double planeX = -dirY * Math.Tan(Fov / 2), planeY = dirX * Math.Tan(Fov / 2);

        for (int x = 0; x < Width; x++)
        {
            double camX = 2.0 * x / Width - 1.0;
            double rayX = dirX + planeX * camX;
            double rayY = dirY + planeY * camX;

            int mapX = (int)bot.X, mapY = (int)bot.Y;
            double deltaX = rayX == 0 ? 1e30 : Math.Abs(1 / rayX);
            double deltaY = rayY == 0 ? 1e30 : Math.Abs(1 / rayY);
            int stepX = rayX < 0 ? -1 : 1, stepY = rayY < 0 ? -1 : 1;
            double sideX = rayX < 0 ? (bot.X - mapX) * deltaX : (mapX + 1.0 - bot.X) * deltaX;
            double sideY = rayY < 0 ? (bot.Y - mapY) * deltaY : (mapY + 1.0 - bot.Y) * deltaY;

            int side = 0, wallType = 1;
            for (int i = 0; i < 128; i++)
            {
                if (sideX < sideY) { sideX += deltaX; mapX += stepX; side = 0; }
                else { sideY += deltaY; mapY += stepY; side = 1; }
                if (mapX < 0 || mapX >= world.Size || mapY < 0 || mapY >= world.Size) { wallType = 1; break; }
                if (world.Grid[mapY, mapX] != 0) { wallType = world.Grid[mapY, mapX]; break; }
            }

            double dist = side == 0
                ? sideX - deltaX
                : sideY - deltaY;
            if (dist < 0.01) dist = 0.01;
            _zBuffer[x] = dist;

            int lineH = (int)(Height / dist);
            int drawStart = Math.Max(0, Height / 2 - lineH / 2);
            int drawEnd = Math.Min(Height - 1, Height / 2 + lineH / 2);

            var (r, g, b) = WallColors[Math.Min(wallType, WallColors.Length - 1)];
            double shade = Math.Clamp(1.0 / (1.0 + dist * 0.22), 0.12, 1.0);
            if (side == 1) shade *= 0.72;
            int color = Pack((byte)(r * shade), (byte)(g * shade), (byte)(b * shade));

            for (int y = drawStart; y <= drawEnd; y++)
                Buffer[y * Width + x] = color;
        }
    }

    private void DrawCreatures(MazeWorld world, ShooterBot bot, double time)
    {
        double dirX = Math.Cos(bot.Angle), dirY = Math.Sin(bot.Angle);
        double planeX = -dirY * Math.Tan(Fov / 2), planeY = dirX * Math.Tan(Fov / 2);
        double invDet = 1.0 / (planeX * dirY - dirX * planeY);

        foreach (var cr in world.Creatures
            .Where(c => c.Alive || c.DeathTimer < 0.5)
            .OrderByDescending(c => (c.X - bot.X) * (c.X - bot.X) + (c.Y - bot.Y) * (c.Y - bot.Y)))
        {
            double relX = cr.X - bot.X, relY = cr.Y - bot.Y;
            double transX = invDet * (dirY * relX - dirX * relY);   // horizontal
            double transY = invDet * (-planeY * relX + planeX * relY); // depth
            if (transY <= 0.1) continue;

            int screenX = (int)(Width / 2.0 * (1 + transX / transY));
            double bob = cr.Alive ? Math.Sin(time * 3 + cr.Phase) * 0.06 : 0;
            double deathScale = cr.Alive ? 1.0 : Math.Max(0, 1.0 - cr.DeathTimer * 2);
            int size = (int)(Height / transY * 0.55 * deathScale);
            if (size <= 1) continue;

            int top = Height / 2 + (int)(Height / transY * (0.18 + bob)) - size;
            var (r, g, b) = CreatureColors[cr.ColorIndex];
            bool flash = !cr.Alive && cr.DeathTimer < 0.15;
            double shade = Math.Clamp(1.0 / (1.0 + transY * 0.18), 0.25, 1.0);

            for (int sx = screenX - size / 2; sx < screenX + size / 2; sx++)
            {
                if (sx < 0 || sx >= Width || transY >= _zBuffer[sx]) continue;
                double nx = (sx - screenX) / (size / 2.0); // -1..1
                for (int sy = top; sy < top + size; sy++)
                {
                    if (sy < 0 || sy >= Height) continue;
                    double ny = (sy - top) / (double)size * 2 - 1; // -1..1

                    // Ghost-blob body: dome top, wavy skirt bottom.
                    double body = nx * nx + ny * ny;
                    bool inBody = body < 0.82;
                    if (ny > 0.25)
                        inBody = Math.Abs(nx) < 0.9 && ny < 0.8 + 0.1 * Math.Sin(nx * 9 + time * 6 + cr.Phase);
                    if (!inBody) continue;

                    byte pr = r, pg = g, pb = b;
                    // Eyes
                    double ex = Math.Abs(nx) - 0.32, ey = ny + 0.25;
                    if (ex * ex * 5 + ey * ey * 5 < 0.1) { pr = pg = pb = 245; }
                    double px = Math.Abs(nx) - 0.30, py = ny + 0.22;
                    if (px * px * 28 + py * py * 28 < 0.1) { pr = pg = pb = 20; }

                    if (flash) { pr = pg = pb = 255; }
                    Buffer[sy * Width + sx] = Pack(
                        (byte)(pr * shade), (byte)(pg * shade), (byte)(pb * shade));
                }
            }
        }
    }

    private void DrawMuzzleFlash(ShooterBot bot)
    {
        if (bot.MuzzleFlash <= 0) return;
        double strength = Math.Clamp(bot.MuzzleFlash / 0.16, 0, 1);

        // Brief whole-screen brightening so every shot registers visually.
        double tint = strength * 0.28;
        for (int i = 0; i < Buffer.Length; i++)
        {
            int old = Buffer[i];
            byte r = (byte)((old >> 16) & 0xFF), g = (byte)((old >> 8) & 0xFF), b = (byte)(old & 0xFF);
            Buffer[i] = Pack(
                (byte)Math.Min(255, r + 255 * tint),
                (byte)Math.Min(255, g + 210 * tint),
                (byte)Math.Min(255, b + 110 * tint));
        }

        int cx = Width / 2, cy = Height - Height / 8;
        int radius = (int)(Height * 0.34 * strength);
        for (int y = Math.Max(0, cy - radius); y < Math.Min(Height, cy + radius); y++)
        {
            for (int x = Math.Max(0, cx - radius); x < Math.Min(Width, cx + radius); x++)
            {
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                if (d >= 1) continue;
                double a = (1 - d) * strength;
                int old = Buffer[y * Width + x];
                byte or_ = (byte)((old >> 16) & 0xFF), og = (byte)((old >> 8) & 0xFF), ob = (byte)(old & 0xFF);
                Buffer[y * Width + x] = Pack(
                    (byte)Math.Min(255, or_ + 255 * a),
                    (byte)Math.Min(255, og + 220 * a),
                    (byte)Math.Min(255, ob + 120 * a));
            }
        }
    }

    /// <summary>Projects a world point to screen space; returns false if behind the camera.</summary>
    private bool ProjectPoint(ShooterBot bot, double wx, double wy, out int sx, out int sy, out double depth)
    {
        double dirX = Math.Cos(bot.Angle), dirY = Math.Sin(bot.Angle);
        double planeX = -dirY * Math.Tan(Fov / 2), planeY = dirX * Math.Tan(Fov / 2);
        double invDet = 1.0 / (planeX * dirY - dirX * planeY);
        double relX = wx - bot.X, relY = wy - bot.Y;
        double transX = invDet * (dirY * relX - dirX * relY);
        double transY = invDet * (-planeY * relX + planeX * relY);
        sx = 0; sy = 0; depth = transY;
        if (transY <= 0.1) return false;
        sx = (int)(Width / 2.0 * (1 + transX / transY));
        int size = (int)(Height / transY * 0.55);
        sy = Height / 2 + (int)(Height / transY * 0.18) - size / 2;
        return true;
    }

    private void DrawShotEffects(ShooterBot bot)
    {
        foreach (var shot in bot.Shots)
        {
            if (!ProjectPoint(bot, shot.X, shot.Y, out int sx, out int sy, out double depth)) continue;

            // Tracer: bright beam from the muzzle to the target, fading fast.
            if (shot.Age < 0.14)
            {
                double fade = 1.0 - shot.Age / 0.14;
                int beam = Pack((byte)(255 * fade), (byte)(240 * fade), (byte)(140 * fade));
                int x0 = Width / 2, y0 = Height - Height / 10;
                for (int off = -1; off <= 1; off++)
                    DrawLine(x0 + off, y0, sx + off, sy, beam);
                DrawLine(x0, y0 + 1, sx, sy + 1, beam);
            }

            // Explosion on hit: expanding ring of colored particles + hot core.
            if (shot.Hit && shot.Age < 0.7)
            {
                double t = shot.Age / 0.7;
                double scale = Math.Clamp(Height / depth * 0.55, 8, Height);
                var (r, g, b) = CreatureColors[shot.ColorIndex];

                // Hot white core that fades quickly.
                if (shot.Age < 0.2)
                {
                    int core = (int)(scale * 0.5 * (1 - shot.Age / 0.2));
                    FillDot(sx, sy, core, Pack(255, 250, 220));
                }

                // 14 particles flying outward, shrinking as they age.
                for (int i = 0; i < 14; i++)
                {
                    double ang = i * Math.PI * 2 / 14 + shot.ColorIndex;
                    double dist = t * scale * 1.4;
                    int px = sx + (int)(Math.Cos(ang) * dist);
                    int py = sy + (int)(Math.Sin(ang) * dist * 0.8) - (int)(t * t * scale * 0.3);
                    int psize = Math.Max(1, (int)(scale * 0.12 * (1 - t)));
                    double fade = 1 - t * 0.6;
                    FillDot(px, py, psize, Pack((byte)(r * fade), (byte)(g * fade), (byte)(b * fade)));
                }
            }
        }
    }

    private void DrawLine(int x0, int y0, int x1, int y1, int color)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int guard = Width + Height;
        while (guard-- > 0)
        {
            SetPixel(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    private void DrawCrosshair(ShooterBot bot)
    {
        int cx = Width / 2, cy = Height / 2;
        int color = bot.State == BotState.Patrolling ? Pack(220, 220, 220) : Pack(255, 80, 80);
        int gap = 4, len = 10;
        for (int i = gap; i < gap + len; i++)
        {
            SetPixel(cx + i, cy, color); SetPixel(cx - i, cy, color);
            SetPixel(cx, cy + i, color); SetPixel(cx, cy - i, color);
            SetPixel(cx + i, cy + 1, color); SetPixel(cx - i, cy + 1, color);
            SetPixel(cx + 1, cy + i, color); SetPixel(cx + 1, cy - i, color);
        }
    }

    private void DrawMinimap(MazeWorld world, ShooterBot bot)
    {
        int scale = Math.Max(2, Width / 6 / world.Size);
        int mapSize = world.Size * scale;
        int ox = Width - mapSize - 12, oy = 12;

        for (int r = 0; r < world.Size; r++)
        {
            for (int c = 0; c < world.Size; c++)
            {
                int color = world.Grid[r, c] != 0 ? Pack(70, 74, 92) : Pack(24, 26, 34);
                for (int y = 0; y < scale; y++)
                    for (int x = 0; x < scale; x++)
                        SetPixel(ox + c * scale + x, oy + r * scale + y, color);
            }
        }

        foreach (var cr in world.Creatures.Where(c => c.Alive))
        {
            var (r, g, b) = CreatureColors[cr.ColorIndex];
            FillDot(ox + (int)(cr.X * scale), oy + (int)(cr.Y * scale), Math.Max(1, scale / 2), Pack(r, g, b));
        }

        // Bot dot + facing tick
        int bx = ox + (int)(bot.X * scale), by = oy + (int)(bot.Y * scale);
        FillDot(bx, by, Math.Max(2, scale / 2 + 1), Pack(255, 255, 255));
        SetPixel(bx + (int)(Math.Cos(bot.Angle) * scale), by + (int)(Math.Sin(bot.Angle) * scale), Pack(255, 255, 0));
    }

    private void FillDot(int cx, int cy, int radius, int color)
    {
        for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
                if (x * x + y * y <= radius * radius)
                    SetPixel(cx + x, cy + y, color);
    }

    private void SetPixel(int x, int y, int color)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
            Buffer[y * Width + x] = color;
    }
}
