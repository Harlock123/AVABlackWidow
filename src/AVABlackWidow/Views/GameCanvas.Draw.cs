using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVABlackWidow.Engine;
using Effect = AVABlackWidow.Engine.Effect;

namespace AVABlackWidow.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVABlackWidow/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFEAF4FF, Yellow = 0xFFFFE14A, Red = 0xFFFF5A4A, Dim = 0xFF7F8CA8, WebBlue = 0xFF3A6CFF, Widow = 0xFFFF4AA0;

    /// <summary>Text with a soft glow behind it, like the beam on a vector screen.</summary>
    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool glow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        var pos = new Point(x, at.Y - ft.Height / 2);
        if (glow)
        {
            var halo = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour & 0x30FFFFFF));
            foreach (var (ox, oy) in new[] { (-1.5, 0.0), (1.5, 0.0), (0.0, -1.5), (0.0, 1.5) }) dc.DrawText(halo, new Point(pos.X + ox, pos.Y + oy));
        }
        dc.DrawText(ft, pos);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    /// <summary>A line as the beam draws it: a wide faint halo, a narrower one, and the bright core.</summary>
    private static void Beam(DrawingContext dc, Point a, Point b, uint colour, double bright = 1, double core = 1.4)
    {
        uint Alpha(double f) => (uint)Math.Clamp(255 * f * bright, 0, 255) << 24 | (colour & 0x00FFFFFF);
        dc.DrawLine(Pen(Alpha(0.12), core * 5), a, b);
        dc.DrawLine(Pen(Alpha(0.3), core * 2.5), a, b);
        dc.DrawLine(Pen(Alpha(1), core), a, b);
    }

    private static void Outline(DrawingContext dc, IReadOnlyList<Point> pts, uint colour, double bright = 1)
    {
        for (int i = 0; i < pts.Count; i++) Beam(dc, pts[i], pts[(i + 1) % pts.Count], colour, bright);
    }

    /// <summary>A loop of points round a centre: an ellipse turned to an angle.</summary>
    private static Point[] Oval(double x, double y, double rx, double ry, double turn, int n = 12)
    {
        double c = Math.Cos(turn), s = Math.Sin(turn);
        return Enumerable.Range(0, n).Select(i =>
        {
            double a = i * Math.PI * 2 / n, px = Math.Cos(a) * rx, py = Math.Sin(a) * ry;
            return new Point(x + px * c - py * s, y + px * s + py * c);
        }).ToArray();
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF02030A), new Rect(0, 0, Field.Width, Field.Height));
        DrawWeb(dc);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        if (inGame)
        {
            foreach (var co in g.Coins) DrawCoin(dc, co);
            foreach (var b in g.Bugs) DrawBug(dc, b);
            foreach (var s in g.Shots) DrawShot(dc, s);
            if (g.Spider.Alive) DrawSpider(dc, g.Spider.X, g.Spider.Y, g.Spider.Facing, g.Spider.Walk, 1.3);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 18), 26, Yellow, center: true);
    }

    // ───────────────────────────── the web ─────────────────────────────

    /// <summary>The point where a ring crosses a spoke: the threads sag a little between spokes.</summary>
    private static Point WebPoint(int spoke, double ring)
    {
        double a = spoke * Math.PI * 2 / Field.Spokes;
        double r = Field.R * ring;
        return new Point(Field.CX + Math.Cos(a) * r, Field.CY + Math.Sin(a) * r);
    }

    private void DrawWeb(DrawingContext dc)
    {
        double shimmer = 0.45 + 0.08 * Math.Sin(_clock * 1.3);
        var c = new Point(Field.CX, Field.CY);
        for (int i = 0; i < Field.Spokes; i++) Beam(dc, c, WebPoint(i, 1), WebBlue, shimmer, 1.1);
        for (int k = 1; k <= Field.Rings; k++)
        {
            double ring = (double)k / Field.Rings;
            for (int i = 0; i < Field.Spokes; i++)
            {
                Beam(dc, WebPoint(i, ring), WebPoint((i + 1) % Field.Spokes, ring), WebBlue, shimmer * (k == Field.Rings ? 1.3 : 1), 1.1);
            }
        }
    }

    // ───────────────────────────── the spider ─────────────────────────────

    private void DrawSpider(DrawingContext dc, double x, double y, double facing, double walk, double scale)
    {
        double cos = Math.Cos(facing), sin = Math.Sin(facing);
        Point P(double fx, double sx) => new(x + (fx * cos - sx * sin) * scale, y + (fx * sin + sx * cos) * scale);
        // Eight legs, four a side, each a bent line; they step in two alternating sets.
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 4; i++)
            {
                double step = Math.Sin(walk * 0.09 + (i % 2 == 0 ? 0 : Math.PI) + (side > 0 ? Math.PI : 0)) * 3;
                double along = 6 - i * 4;
                var hip = P(along * 0.6, side * 4);
                var knee = P(along + 2 + step, side * 13);
                var foot = P(along * 1.6 - 4 + step * 1.5, side * 19);
                Beam(dc, hip, knee, Widow, 0.9, 1.2);
                Beam(dc, knee, foot, Widow, 0.9, 1.2);
            }
        // The abdomen with its hourglass, and the head.
        Outline(dc, Oval(x - cos * 6 * scale, y - sin * 6 * scale, 9 * scale, 7 * scale, facing), Widow);
        var h0 = P(-10, -3); var h1 = P(-2, 3); var h2 = P(-10, 3); var h3 = P(-2, -3);
        Beam(dc, h0, h1, Red, 1, 1.6);
        Beam(dc, h2, h3, Red, 1, 1.6);
        Outline(dc, Oval(x + cos * 6 * scale, y + sin * 6 * scale, 5 * scale, 4.5 * scale, facing, 8), Widow);
    }

    // ───────────────────────────── the bugs ─────────────────────────────

    private void DrawBug(DrawingContext dc, Bug b)
    {
        uint colour = b.Flash > 0 ? 0xFFFFFFFF : GameEngine.Colour(b.Kind);
        double heading = Math.Atan2(b.Vy, b.Vx);
        double cos = Math.Cos(heading), sin = Math.Sin(heading);
        Point P(double fx, double sx) => new(b.X + fx * cos - sx * sin, b.Y + fx * sin + sx * cos);
        switch (b.Kind)
        {
            case BugKind.Gnat:
            {
                Outline(dc, Oval(b.X, b.Y, 6, 3.2, heading, 8), colour);
                double flap = Math.Sin(_clock * 40 + b.X) * 4;
                Beam(dc, P(0, 0), P(-3 + flap, -8), colour, 0.8);
                Beam(dc, P(0, 0), P(-3 + flap, 8), colour, 0.8);
                Beam(dc, P(6, 0), P(10, 0), colour, 0.8);
                break;
            }
            case BugKind.Beetle:
            {
                Outline(dc, Oval(b.X, b.Y, 11, 8, heading, 12), colour);
                Beam(dc, P(-11, 0), P(5, 0), colour, 0.8);
                Outline(dc, Oval(b.X + cos * 12, b.Y + sin * 12, 4, 4, heading, 6), colour);
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < 3; i++)
                    {
                        double step = Math.Sin(b.Age * 10 + i + side) * 2;
                        Beam(dc, P(4 - i * 5, side * 7), P(6 - i * 6 + step, side * 13), colour, 0.7);
                    }
                // A pip for each hit it can still take.
                for (int i = 0; i < b.Hp; i++) dc.DrawEllipse(Brush(colour), null, P(-4 + i * 5, 0), 1.6, 1.6);
                break;
            }
            case BugKind.Hornet:
            {
                Outline(dc, Oval(b.X, b.Y, 11, 4.5, heading, 10), colour);
                for (int i = -1; i <= 1; i++) Beam(dc, P(-2 + i * 4, -4), P(-2 + i * 4, 4), 0xFFFFF0C0, 0.7);
                double flap = Math.Sin(_clock * 46 + b.Y) * 5;
                Beam(dc, P(2, -3), P(-4 + flap, -12), White, 0.7);
                Beam(dc, P(2, 3), P(-4 + flap, 12), White, 0.7);
                Beam(dc, P(-11, 0), P(-15, 0), colour, 1, 1.8);
                break;
            }
            case BugKind.Egg:
            {
                double ripe = 1 - Math.Clamp(b.Timer / GameEngine.HatchSeconds, 0, 1);
                double wobble = ripe > 0.7 ? Math.Sin(_clock * 30) * 0.4 : 0;
                Outline(dc, Oval(b.X, b.Y, 6, 4.5, wobble + 0.5, 10), ripe > 0.7 && (int)(_clock * 8) % 2 == 0 ? 0xFF7CFF7A : colour, 0.9);
                break;
            }
            case BugKind.Bomber:
            {
                Outline(dc, Oval(b.X, b.Y, 11, 11, 0, 14), colour);
                Beam(dc, P(-6, -6), P(6, 6), colour, 0.7);
                Beam(dc, P(-6, 6), P(6, -6), colour, 0.7);
                // A fizzing fuse on top.
                var fuse = new Point(b.X, b.Y - 13);
                Beam(dc, new Point(b.X, b.Y - 10), fuse, Yellow, 1);
                if ((int)(_clock * 14) % 2 == 0) dc.DrawEllipse(Brush(0xFFFFF0A0), null, new Point(fuse.X + 2, fuse.Y - 2), 2.5, 2.5);
                break;
            }
            case BugKind.Slayer:
            {
                // A spinning star of blades.
                int n = 8;
                var pts = Enumerable.Range(0, n * 2).Select(i =>
                {
                    double a = i * Math.PI / n + b.Age * 6, r = i % 2 == 0 ? 15 : 6;
                    return new Point(b.X + Math.Cos(a) * r, b.Y + Math.Sin(a) * r);
                }).ToArray();
                Outline(dc, pts, colour);
                dc.DrawEllipse(Brush(Red), null, new Point(b.X, b.Y), 2.5, 2.5);
                break;
            }
        }
    }

    private static void DrawShot(DrawingContext dc, Shot s)
    {
        var tail = new Point(s.X - s.Vx * 0.018, s.Y - s.Vy * 0.018);
        Beam(dc, tail, new Point(s.X, s.Y), 0xFFFFFFFF, 1, 1.6);
    }

    private void DrawCoin(DrawingContext dc, Coin co)
    {
        double near = Field.DistFromCentre(co.X, co.Y) / Field.R;
        bool blink = near > 0.85 && (int)(_clock * 8) % 2 == 0;
        if (blink) return;
        Outline(dc, Oval(co.X, co.Y, 9, 9, 0, 12), Yellow, 0.9);
        Text(dc, "$", new Point(co.X, co.Y), 20, Yellow, center: true, glow: false);
    }

    private static void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
                for (int i = 0; i < 10; i++)
                {
                    double a = i * Math.PI / 5 + Hash(i, (int)e.X, 3) % 10 * 0.05, r0 = 4 + 30 * t, r1 = r0 + 7 * (1 - t);
                    Beam(dc, new Point(c.X + Math.Cos(a) * r0, c.Y + Math.Sin(a) * r0), new Point(c.X + Math.Cos(a) * r1, c.Y + Math.Sin(a) * r1), e.Colour, 1 - t);
                }
                break;
            case EffectKind.Blast:
            {
                double r = GameEngine.BlastRadius * Math.Sqrt(t);
                Outline(dc, Oval(c.X, c.Y, r, r, 0, 24), 0xFFFFB040, 1 - t);
                Outline(dc, Oval(c.X, c.Y, r * 0.6, r * 0.6, 0.3, 18), 0xFFFFFFFF, (1 - t) * 0.6);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 18 - t * 26), 24, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF02030A), new Rect(0, 0, Field.Width, Field.Top));
        Beam(dc, new Point(0, Field.Top - 1), new Point(Field.Width, Field.Top - 1), WebBlue, 0.5);
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 44), 34, White);
        Text(dc, "HI", new Point(190, 16), 22, Dim);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(190, 44), 28, Dim);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused;
        if (!inGame) return;
        Text(dc, $"WAVE {g.Wave}", new Point(Field.Width / 2, 16), 24, Yellow, center: true);
        Text(dc, $"BUGS TO COME {g.ToCome + g.Bugs.Count(b => b.Kind != BugKind.Egg)}", new Point(Field.Width / 2, 44), 22, Dim, center: true);
        Text(dc, "BOUNTY", new Point(720, 16), 22, Dim);
        Text(dc, $"${g.Bounty}", new Point(720, 44), 28, Yellow);
        Text(dc, "SPIDERS", new Point(880, 16), 22, Dim);
        for (int i = 0; i < Math.Min(5, g.Lives - (g.Spider.Alive ? 1 : 0)); i++)
            DrawSpider(dc, 892 + i * 26, 44, -Math.PI / 2, 0, 0.5);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2;
        Text(dc, msg, new Point(cx, 170), 64, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, 224), 30, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xA002030A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "AVA BLACK WIDOW", new Point(cx, 140), 110, Widow, center: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 206), 28, Dim, center: true);
        DrawSpider(dc, cx, 262, -Math.PI / 2, _clock * 120, 2.2);
        string[] lines =
        [
            "DEFEND YOUR WEB: WALK WITH ONE STICK, SHOOT WITH THE OTHER",
            "EVERY BUG YOU SHOOT LEAVES BOUNTY - GRAB IT BEFORE IT DRIFTS OFF",
            "EAT THE HORNETS' EGGS BEFORE THEY HATCH",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 330 + i * 34), 27, White, center: true);
        // The bugs, and what they are worth.
        (BugKind Kind, string Name, string Points)[] table =
        [
            (BugKind.Gnat, "GNAT", "100"), (BugKind.Beetle, "BEETLE", "250"), (BugKind.Hornet, "HORNET", "300"),
            (BugKind.Egg, "EGG", "100"), (BugKind.Bomber, "BOMBER", "150"), (BugKind.Slayer, "SLAYER", "500"),
        ];
        for (int i = 0; i < table.Length; i++)
        {
            double x = 147 + i * 146;
            var bug = new Bug { Kind = table[i].Kind, X = x, Y = 470, Vx = 1, Age = _clock, Hp = table[i].Kind == BugKind.Beetle ? 3 : 1, Timer = GameEngine.HatchSeconds };
            DrawBug(dc, bug);
            Text(dc, table[i].Name, new Point(x, 504), 22, GameEngine.Colour(table[i].Kind), center: true);
            Text(dc, table[i].Points, new Point(x, 528), 24, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 600), 48, Yellow, center: true);
        Text(dc, "W A S D: WALK    ARROWS: SHOOT (EIGHT WAYS)    GAMEPAD: TWIN STICKS", new Point(cx, 664), 25, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC002030A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "TOP SPIDERS", new Point(cx, 130), 72, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   WAVE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,3}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC002030A), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
