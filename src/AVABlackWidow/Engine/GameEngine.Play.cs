namespace AVABlackWidow.Engine;

public sealed partial class GameEngine
{
    public const double SpiderSpeed = 230, ShotSpeed = 650, FireDelay = 0.14;
    public const int MaxShots = 4;
    public const double StartSeconds = 2.2, DyingSeconds = 2.4, ClearSeconds = 3.2;
    public const double HatchSeconds = 6, CoinDrift = 34, BlastRadius = 115;
    /// <summary>The bounty: the first coin of a wave is worth this, each one after it this much more, up to the cap.</summary>
    public const int CoinStep = 100, CoinCap = 1500;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 40000;
    /// <summary>A wave that drags on brings out a slayer.</summary>
    public const double SlayerAfter = 28;

    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Spider Spider { get; } = new();
    public List<Bug> Bugs { get; } = [];
    public List<Shot> Shots { get; } = [];
    public List<Coin> Coins { get; } = [];
    public SpiderPilot Pilot { get; } = new();
    /// <summary>What the next bounty coin is worth.</summary>
    public int Bounty { get; private set; } = CoinStep;
    /// <summary>Bugs still to come this wave.</summary>
    public int ToCome { get; private set; }
    /// <summary>What caught the last spider.</summary>
    public string Cause { get; private set; } = "";
    /// <summary>For tests: no bugs arrive unless a test puts them there.</summary>
    internal bool Calm { get; set; }

    /// <summary>How much faster everything is than in the first wave.</summary>
    public double Pace => Math.Min(2.0, 1 + 0.08 * (Wave - 1));

    private int _nextExtra;
    private double _spawnIn, _fireWait, _waveTime;
    private bool _slayerOut;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        _nextExtra = FirstExtraLife;
        Wave = 1;
        Clear();
        SetPhase(Phase.WaveStart);
    }

    private void Clear()
    {
        Bugs.Clear();
        Shots.Clear();
        Coins.Clear();
        Spider.X = Field.CX;
        Spider.Y = Field.CY;
        Spider.Alive = true;
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        Clear();
        ToCome = Math.Min(50, 10 + 3 * wave);
        Bounty = CoinStep;
        _spawnIn = 0.4;
        _waveTime = 0;
        _slayerOut = false;
        SetPhase(Phase.WaveStart);
        Say($"WAVE {wave}", WaveNote(wave), StartSeconds);
        Play(Sfx.WaveStart);
    }

    /// <summary>What's new in a wave.</summary>
    private static string? WaveNote(int wave) => wave switch
    {
        1 => "GNATS - SHOOT THEM, THEN GRAB THEIR BOUNTY",
        2 => "BEETLES - THREE HITS EACH",
        3 => "HORNETS - THEY LAY EGGS: SHOOT OR EAT THEM",
        4 => "BOMBERS - SHOOT ONE AMONG THE OTHERS",
        5 => "SLAYERS - THEY HUNT YOU",
        _ => null,
    };

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        foreach (var co in Coins) co.Age += dt;
        switch (Phase)
        {
            case Phase.WaveStart:
                Walk(c, dt);
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                _waveTime += dt;
                Walk(c, dt);
                Fire(c, dt);
                MoveShots(dt);
                Arrivals(dt);
                MoveBugs(dt);
                MoveCoins(dt);
                Touch();
                if (Phase == Phase.Play && ToCome == 0 && Bugs.Count == 0 && !Calm) WaveDone();
                break;
            case Phase.Dying:
                MoveCoins(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.WaveClear:
                Walk(c, dt);
                if (PhaseTime > ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        Bugs.RemoveAll(b => b.Dead);
        Shots.RemoveAll(s => s.Dead);
        Coins.RemoveAll(co => co.Dead);
    }

    private void Walk(Controls c, double dt)
    {
        var s = Spider;
        double mx = c.MoveX, my = c.MoveY, len = Math.Sqrt(mx * mx + my * my);
        if (len > 1) { mx /= len; my /= len; }
        if (len > 0.1)
        {
            s.X += mx * SpiderSpeed * dt;
            s.Y += my * SpiderSpeed * dt;
            s.Facing = Math.Atan2(my, mx);
            s.Walk += SpiderSpeed * Math.Min(1, len) * dt;
        }
        // It can't leave the web.
        double r = Field.DistFromCentre(s.X, s.Y), max = Field.R - Spider.Radius - 4;
        if (r > max)
        {
            s.X = Field.CX + (s.X - Field.CX) / r * max;
            s.Y = Field.CY + (s.Y - Field.CY) / r * max;
        }
    }

    private void Fire(Controls c, double dt)
    {
        _fireWait -= dt;
        if (!c.Firing || _fireWait > 0 || Shots.Count >= MaxShots) return;
        double len = Math.Sqrt(c.FireX * c.FireX + c.FireY * c.FireY);
        double dx = c.FireX / len, dy = c.FireY / len;
        _fireWait = FireDelay;
        Shots.Add(new Shot { X = Spider.X + dx * 12, Y = Spider.Y + dy * 12, Vx = dx * ShotSpeed, Vy = dy * ShotSpeed });
        Play(Sfx.Fire);
    }

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
        {
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            if (Field.DistFromCentre(s.X, s.Y) > Field.R) { s.Dead = true; continue; }
            foreach (var b in Bugs)
            {
                if (b.Dead) continue;
                double r = b.Radius + 4;
                if ((b.X - s.X) * (b.X - s.X) + (b.Y - s.Y) * (b.Y - s.Y) > r * r) continue;
                s.Dead = true;
                HitBug(b, s);
                break;
            }
        }
    }

    // ───────────────────────────── the bugs ─────────────────────────────

    private void Arrivals(double dt)
    {
        if (Calm) return;
        // A slayer comes out for a wave that drags on, and from wave five as part of the wave.
        if (!_slayerOut && _waveTime > SlayerAfter && Bugs.All(b => b.Kind != BugKind.Slayer))
        {
            _slayerOut = true;
            Arrive(BugKind.Slayer);
        }
        if (ToCome <= 0) return;
        _spawnIn -= dt;
        int crowd = Math.Min(18, 8 + Wave);
        if (_spawnIn > 0 || Bugs.Count(b => b.Kind != BugKind.Egg) >= crowd) return;
        _spawnIn = Math.Max(0.4, 1.1 - 0.07 * Wave) * (0.7 + _rng.NextDouble() * 0.6);
        // Sometimes two at once, from opposite sides.
        int n = ToCome >= 2 && _rng.NextDouble() < Math.Min(0.5, 0.1 + 0.05 * Wave) ? 2 : 1;
        for (int i = 0; i < n; i++)
        {
            ToCome--;
            Arrive(Pick());
        }
    }

    /// <summary>What comes next, from the kinds this wave has reached.</summary>
    private BugKind Pick()
    {
        int roll = _rng.Next(100);
        return Wave switch
        {
            1 => BugKind.Gnat,
            2 => roll < 70 ? BugKind.Gnat : BugKind.Beetle,
            3 => roll < 50 ? BugKind.Gnat : roll < 75 ? BugKind.Beetle : BugKind.Hornet,
            4 => roll < 40 ? BugKind.Gnat : roll < 60 ? BugKind.Beetle : roll < 80 ? BugKind.Hornet : BugKind.Bomber,
            _ => roll < 32 ? BugKind.Gnat : roll < 52 ? BugKind.Beetle : roll < 72 ? BugKind.Hornet : roll < 92 ? BugKind.Bomber : BugKind.Slayer,
        };
    }

    /// <summary>A bug comes on at the edge of the web, on a spoke.</summary>
    private Bug Arrive(BugKind kind)
    {
        int spoke = _rng.Next(Field.Spokes);
        double a = spoke * Math.PI * 2 / Field.Spokes;
        var b = new Bug
        {
            Kind = kind, X = Field.CX + Math.Cos(a) * (Field.R - 4), Y = Field.CY + Math.Sin(a) * (Field.R - 4),
            Hp = kind switch { BugKind.Beetle => 3, BugKind.Slayer => 3, _ => 1 },
            Spoke = a, Timer = 2 + _rng.NextDouble() * 2,
        };
        NewWaypoint(b);
        Bugs.Add(b);
        if (kind == BugKind.Slayer) Play(Sfx.Slayer);
        return b;
    }

    /// <summary>Somewhere near the spider, but not too near: gnats and bombers mill about rather than charge.</summary>
    private void NewWaypoint(Bug b)
    {
        // Now and then one goes straight at the spider, and more often in later waves.
        bool charge = _rng.NextDouble() < Math.Min(0.55, 0.2 + 0.04 * Wave);
        double a = _rng.NextDouble() * Math.PI * 2, d = charge ? 0 : 40 + _rng.NextDouble() * 150;
        b.TargetX = Spider.X + Math.Cos(a) * d;
        b.TargetY = Spider.Y + Math.Sin(a) * d;
        double r = Field.DistFromCentre(b.TargetX, b.TargetY);
        if (r > Field.R - 30)
        {
            b.TargetX = Field.CX + (b.TargetX - Field.CX) / r * (Field.R - 30);
            b.TargetY = Field.CY + (b.TargetY - Field.CY) / r * (Field.R - 30);
        }
    }

    private void MoveBugs(double dt)
    {
        var s = Spider;
        foreach (var b in Bugs.ToList())
        {
            if (b.Dead) continue;
            b.Age += dt;
            b.Flash = Math.Max(0, b.Flash - dt);
            switch (b.Kind)
            {
                case BugKind.Gnat:
                case BugKind.Bomber:
                {
                    double speed = (b.Kind == BugKind.Gnat ? 90 : 60) * Pace;
                    double dx = b.TargetX - b.X, dy = b.TargetY - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    b.Timer -= dt;
                    if (d < 12 || b.Timer <= 0) { NewWaypoint(b); b.Timer = 1 + _rng.NextDouble() * 1.5; }
                    else Head(b, dx / d * speed, dy / d * speed, b.Kind == BugKind.Gnat ? 5 : 3, dt);
                    // Gnats buzz.
                    if (b.Kind == BugKind.Gnat) { b.X += Math.Sin(b.Age * 17) * 22 * dt; b.Y += Math.Cos(b.Age * 13) * 22 * dt; }
                    break;
                }
                case BugKind.Beetle:
                {
                    // In along its spoke to the middle, then straight for the spider.
                    double speed = 45 * Pace;
                    if (Field.DistFromCentre(b.X, b.Y) < 90) b.Hunting = true;
                    double tx = b.Hunting ? s.X : Field.CX, ty = b.Hunting ? s.Y : Field.CY;
                    double dx = tx - b.X, dy = ty - b.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    Head(b, dx / d * speed, dy / d * speed, 4, dt);
                    break;
                }
                case BugKind.Hornet:
                {
                    // Swoops round the spider in wide curves, stopping now and then to lay an egg.
                    double speed = 165 * Pace;
                    double dx = s.X - b.X, dy = s.Y - b.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    double swirl = d > 120 ? 0.9 : 1.6;
                    double ax = dx / d * Math.Cos(swirl) - dy / d * Math.Sin(swirl), ay = dx / d * Math.Sin(swirl) + dy / d * Math.Cos(swirl);
                    if (b.Age % 7 > 4.5) { ax = dx / d; ay = dy / d; } // a dive at the spider now and then
                    Head(b, ax * speed, ay * speed, 3, dt);
                    b.Timer -= dt;
                    if (b.Timer <= 0 && b.EggsLaid < 3 && Field.DistFromCentre(b.X, b.Y) < Field.R - 40)
                    {
                        b.Timer = 3 + _rng.NextDouble() * 2;
                        b.EggsLaid++;
                        Bugs.Add(new Bug { Kind = BugKind.Egg, X = b.X, Y = b.Y, Timer = HatchSeconds });
                        Play(Sfx.Lay);
                    }
                    break;
                }
                case BugKind.Egg:
                    b.Timer -= dt;
                    if (b.Timer <= 0)
                    {
                        b.Kind = BugKind.Gnat;
                        b.Timer = 1;
                        NewWaypoint(b);
                        Play(Sfx.Hatch);
                    }
                    break;
                case BugKind.Slayer:
                {
                    double speed = 135 * Pace;
                    double dx = s.X - b.X, dy = s.Y - b.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    Head(b, dx / d * speed, dy / d * speed, 2.2, dt);
                    break;
                }
            }
            if (b.Kind != BugKind.Egg)
            {
                b.X += b.Vx * dt;
                b.Y += b.Vy * dt;
                // Bugs keep to the web too.
                double r = Field.DistFromCentre(b.X, b.Y);
                if (r > Field.R - 2)
                {
                    b.X = Field.CX + (b.X - Field.CX) / r * (Field.R - 2);
                    b.Y = Field.CY + (b.Y - Field.CY) / r * (Field.R - 2);
                }
            }
        }
    }

    /// <summary>Turns a bug's velocity towards the one it wants, at the given rate.</summary>
    private static void Head(Bug b, double vx, double vy, double rate, double dt)
    {
        double k = Math.Min(1, rate * dt);
        b.Vx += (vx - b.Vx) * k;
        b.Vy += (vy - b.Vy) * k;
    }

    private void HitBug(Bug b, Shot s)
    {
        b.Hp--;
        if (b.Hp > 0)
        {
            b.Flash = 0.15;
            // A beetle is knocked back out along its way.
            double sp = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
            double knock = b.Kind == BugKind.Beetle ? 24 : 10;
            b.X += s.Vx / sp * knock;
            b.Y += s.Vy / sp * knock;
            Play(Sfx.Thud);
            return;
        }
        Kill(b, chain: false);
    }

    private void Kill(Bug b, bool chain)
    {
        if (b.Dead) return;
        b.Dead = true;
        // Caught in a bomber's blast, a bug is worth double.
        int points = b.Points * (chain ? 2 : 1);
        AddScore(points, b.X, b.Y);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = b.X, Y = b.Y, Colour = Colour(b.Kind), Life = 0.55 });
        if (b.Kind != BugKind.Egg) DropCoin(b.X, b.Y);
        if (b.Kind == BugKind.Bomber)
        {
            Effects.Add(new Effect { Kind = EffectKind.Blast, X = b.X, Y = b.Y, Life = 0.5 });
            Play(Sfx.Blast);
            foreach (var other in Bugs.ToList())
                if (!other.Dead && (other.X - b.X) * (other.X - b.X) + (other.Y - b.Y) * (other.Y - b.Y) < BlastRadius * BlastRadius)
                    Kill(other, chain: true);
        }
        else Play(Sfx.Hit);
    }

    private void DropCoin(double x, double y)
    {
        double r = Math.Max(1, Field.DistFromCentre(x, y));
        Coins.Add(new Coin { X = x, Y = y, Vx = (x - Field.CX) / r * CoinDrift, Vy = (y - Field.CY) / r * CoinDrift });
    }

    private void MoveCoins(double dt)
    {
        foreach (var co in Coins)
        {
            co.X += co.Vx * dt;
            co.Y += co.Vy * dt;
            if (Field.DistFromCentre(co.X, co.Y) > Field.R - 6) co.Dead = true;
        }
    }

    /// <summary>What the spider runs into: bugs catch it, eggs and coins are its to take.</summary>
    private void Touch()
    {
        var s = Spider;
        foreach (var co in Coins)
        {
            if (co.Dead || (co.X - s.X) * (co.X - s.X) + (co.Y - s.Y) * (co.Y - s.Y) > 18 * 18) continue;
            co.Dead = true;
            AddScore(Bounty, co.X, co.Y, 0xFFFFE14A);
            Bounty = Math.Min(CoinCap, Bounty + CoinStep);
            Play(Sfx.Coin);
        }
        foreach (var b in Bugs)
        {
            if (b.Dead) continue;
            double r = b.Radius + Spider.Radius - 3;
            if ((b.X - s.X) * (b.X - s.X) + (b.Y - s.Y) * (b.Y - s.Y) > r * r) continue;
            if (b.Kind == BugKind.Egg)
            {
                b.Dead = true;
                AddScore(100, b.X, b.Y);
                Play(Sfx.Eat);
                continue;
            }
            Caught(b);
            return;
        }
    }

    private void Caught(Bug b)
    {
        Cause = b.Kind switch
        {
            BugKind.Gnat => "STUNG BY A GNAT", BugKind.Beetle => "CRUSHED BY A BEETLE", BugKind.Hornet => "STUNG BY A HORNET",
            BugKind.Bomber => "BLOWN UP", _ => "CAUGHT BY THE SLAYER",
        };
        Lives--;
        Spider.Alive = false;
        // A caught spider loses its run of bounty.
        Bounty = CoinStep;
        Shots.Clear();
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = Spider.X, Y = Spider.Y, Colour = 0xFFFF4AA0, Life = 1.2 });
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = Spider.X, Y = Spider.Y, Life = 0.7 });
        Play(Sfx.Death);
        Music(false);
        SetPhase(Phase.Dying);
        Say(Cause, Lives > 0 ? $"{Lives} {(Lives == 1 ? "SPIDER" : "SPIDERS")} LEFT" : null, DyingSeconds);
    }

    /// <summary>The next spider, in the middle of the web; the bugs close by are swept away first.</summary>
    private void Respawn()
    {
        Spider.X = Field.CX;
        Spider.Y = Field.CY;
        Spider.Alive = true;
        // The bugs swept away come back later in the wave.
        foreach (var b in Bugs.Where(b => b.Kind != BugKind.Egg && Field.DistFromCentre(b.X, b.Y) < 170))
        {
            b.Dead = true;
            ToCome++;
        }
        Bugs.RemoveAll(b => b.Dead);
        _spawnIn = 1.5;
        SetPhase(Phase.Play);
        if (State == GameState.Playing) Music(true);
    }

    private void WaveDone()
    {
        int bonus = 1000 * Wave;
        AddScore(bonus, Field.CX, Field.CY - 60, 0xFFFFE14A);
        Say($"WAVE {Wave} CLEARED", $"BONUS {bonus}", ClearSeconds);
        Play(Sfx.WaveClear);
        Music(false);
        Coins.Clear();
        SetPhase(Phase.WaveClear);
    }

    private void AddScore(int points, double x, double y, uint colour = 0xFFF2F2F2)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Life = 0.9, Colour = colour });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    public static uint Colour(BugKind k) => k switch
    {
        BugKind.Gnat => 0xFF7CFF7Au, BugKind.Beetle => 0xFFFFD84Au, BugKind.Hornet => 0xFFFF9A3Au, BugKind.Egg => 0xFFF2F2F2u,
        BugKind.Bomber => 0xFFFF4A4Au, _ => 0xFFC070FFu,
    };

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a wave.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal Bug Put(BugKind kind, double x, double y)
    {
        var b = new Bug { Kind = kind, X = x, Y = y, TargetX = x, TargetY = y, Timer = kind == BugKind.Egg ? HatchSeconds : 99, Hp = kind switch { BugKind.Beetle => 3, BugKind.Slayer => 3, _ => 1 } };
        Bugs.Add(b);
        return b;
    }

    internal Coin PutCoin(double x, double y)
    {
        var co = new Coin { X = x, Y = y };
        Coins.Add(co);
        return co;
    }

    internal void MoveSpider(double x, double y)
    {
        Spider.X = x;
        Spider.Y = y;
    }

    internal void SetToCome(int n) => ToCome = n;
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
    internal void SetWaveTime(double t) => _waveTime = t;
}
