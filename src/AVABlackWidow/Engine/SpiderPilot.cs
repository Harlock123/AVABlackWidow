namespace AVABlackWidow.Engine;

/// <summary>
/// The computer at the sticks, for the demonstration (and the tests). It backs away from what is
/// close, the slayer most of all, eats eggs and grabs coins when the coast is clear, keeps off the
/// edge of the web, and shoots at the most pressing bug, leading it.
/// </summary>
public sealed class SpiderPilot
{
    /// <summary>The bug it is shooting at, for the tests and for tuning.</summary>
    public Bug? Target { get; private set; }

    public Controls Decide(GameEngine g)
    {
        var s = g.Spider;
        if (!s.Alive || g.Phase == Phase.Dying) return default;

        double mx = 0, my = 0, danger = 0;
        foreach (var b in g.Bugs)
        {
            if (b.Dead || b.Kind == BugKind.Egg) continue;
            // Where it will be in a moment.
            double bx = b.X + b.Vx * 0.3, by = b.Y + b.Vy * 0.3;
            double dx = s.X - bx, dy = s.Y - by, d = Math.Max(8, Math.Sqrt(dx * dx + dy * dy));
            double reach = b.Kind == BugKind.Slayer ? 220 : 150;
            if (d > reach) continue;
            double weight = b.Kind switch { BugKind.Slayer => 3, BugKind.Hornet => 2, BugKind.Beetle or BugKind.Bomber => 1.5, _ => 1 };
            double push = weight * (reach - d) / reach;
            mx += dx / d * push;
            my += dy / d * push;
            danger += push;
        }

        // When nothing presses, go for coins and eggs.
        if (danger < 0.6)
        {
            double best = double.MaxValue, gx = 0, gy = 0;
            foreach (var co in g.Coins)
            {
                double d = Dist(s.X, s.Y, co.X, co.Y);
                if (d < best && Field.DistFromCentre(co.X, co.Y) < Field.R - 50) { best = d; gx = co.X; gy = co.Y; }
            }
            foreach (var b in g.Bugs.Where(b => b.Kind == BugKind.Egg && !b.Dead))
            {
                double d = Dist(s.X, s.Y, b.X, b.Y) * 0.8;
                if (d < best) { best = d; gx = b.X; gy = b.Y; }
            }
            if (best < double.MaxValue)
            {
                double d = Math.Max(1, best);
                mx += (gx - s.X) / d * 1.2;
                my += (gy - s.Y) / d * 1.2;
            }
            else
            {
                // Back towards the middle, where there is room.
                mx += (Field.CX - s.X) / 300;
                my += (Field.CY - s.Y) / 300;
            }
        }
        // Keep off the rim, where bugs arrive.
        double r = Field.DistFromCentre(s.X, s.Y);
        if (r > Field.R - 90)
        {
            mx += (Field.CX - s.X) / r * 1.5;
            my += (Field.CY - s.Y) / r * 1.5;
        }

        // Shoot at the most pressing bug: the slayer first, then whatever is nearest.
        Target = g.Bugs.Where(b => !b.Dead)
            .OrderBy(b => Dist(s.X, s.Y, b.X, b.Y) * (b.Kind == BugKind.Slayer ? 0.4 : b.Kind == BugKind.Egg ? 1.6 : 1))
            .FirstOrDefault();
        double fx = 0, fy = 0;
        if (Target is { } t)
        {
            double dx = t.X - s.X, dy = t.Y - s.Y;
            double time = Math.Sqrt(dx * dx + dy * dy) / GameEngine.ShotSpeed;
            dx += t.Vx * time;
            dy += t.Vy * time;
            double d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            fx = dx / d;
            fy = dy / d;
        }
        double len = Math.Sqrt(mx * mx + my * my);
        if (len > 1) { mx /= len; my /= len; }
        else if (len < 0.08) mx = my = 0;
        return new Controls(mx, my, fx, fy);
    }

    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
}
