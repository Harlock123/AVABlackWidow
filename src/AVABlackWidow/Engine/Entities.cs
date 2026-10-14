namespace AVABlackWidow.Engine;

/// <summary>
/// Twin-stick controls: one direction to walk (each part −1 to 1), another to shoot. No shooting
/// when the fire direction is (near) zero.
/// </summary>
public readonly record struct Controls(double MoveX = 0, double MoveY = 0, double FireX = 0, double FireY = 0)
{
    public bool Firing => FireX * FireX + FireY * FireY > 0.09;
}

/// <summary>The screen and the web strung across it.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The bottom of the score bar.</summary>
    public const double Top = 64;
    /// <summary>The web's centre and its outer edge.</summary>
    public const double CX = Width / 2, CY = (Top + Height) / 2 + 2, R = 338;
    public const int Spokes = 16, Rings = 7;

    public static double DistFromCentre(double x, double y) => Math.Sqrt((x - CX) * (x - CX) + (y - CY) * (y - CY));
}

public sealed class Spider
{
    public const double Radius = 11;
    public double X = Field.CX, Y = Field.CY;
    /// <summary>Which way it faces, for the legs, and how far its legs have walked.</summary>
    public double Facing = -Math.PI / 2, Walk;
    public bool Alive = true;
}

public enum BugKind { Gnat, Beetle, Hornet, Egg, Bomber, Slayer }

public sealed class Bug
{
    public BugKind Kind;
    public double X, Y, Vx, Vy, Age;
    public int Hp = 1;
    /// <summary>A gnat's and a bomber's next waypoint, a hornet's time to its next egg, an egg's time to hatch.</summary>
    public double TargetX, TargetY, Timer;
    public int EggsLaid;
    public bool Dead;
    /// <summary>Shown bright for a moment after a hit that didn't kill.</summary>
    public double Flash;
    /// <summary>A beetle's spoke: the angle it crawls in along.</summary>
    public double Spoke;
    /// <summary>A beetle that has reached the hub and now goes straight for the spider.</summary>
    public bool Hunting;

    public double Radius => Kind switch
    {
        BugKind.Gnat => 9, BugKind.Beetle => 13, BugKind.Hornet => 11, BugKind.Egg => 6, BugKind.Bomber => 12, _ => 13,
    };

    public int Points => Kind switch
    {
        BugKind.Gnat => 100, BugKind.Beetle => 250, BugKind.Hornet => 300, BugKind.Egg => 50, BugKind.Bomber => 150, _ => 500,
    };
}

public sealed class Shot
{
    public double X, Y, Vx, Vy;
    public bool Dead;
}

/// <summary>A bounty coin a dead bug leaves behind: it drifts out to the edge of the web and is lost there.</summary>
public sealed class Coin
{
    public double X, Y, Vx, Vy, Age;
    public bool Dead;
}

public enum EffectKind { Burst, Score, Blast }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.6;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
