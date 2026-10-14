using AVABlackWidow.Engine;

namespace AVABlackWidow.Tests;

/// <summary>How the computer spider gets on, wave by wave (set WIDOW_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        if (Environment.GetEnvironmentVariable("WIDOW_PROBE") is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "widow-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var waveTimes = new List<string>();
            var last = g.Phase;
            int wave = 1, coins = 0;
            double waveStart = 0;
            int t = 0;
            for (; t < 60 * 600 && g.Wave <= 8; t++)
            {
                int before = g.Coins.Count;
                var c = g.Pilot.Decide(g);
                if (Environment.GetEnvironmentVariable("WIDOW_PROBE") == "still") c = c with { MoveX = 0, MoveY = 0 };
                // Aim as a keyboard player must: eight ways only.
                if (c.Firing)
                {
                    double a = Math.Round(Math.Atan2(c.FireY, c.FireX) / (Math.PI / 4)) * (Math.PI / 4);
                    c = c with { FireX = Math.Cos(a), FireY = Math.Sin(a) };
                }
                g.Tick(c);
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"w{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"w{g.Wave} {g.Cause}") + 1;
                if (g.Wave != wave) { waveTimes.Add($"w{wave}:{(t - waveStart) / 60:0}s"); wave = g.Wave; waveStart = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: wave {g.Wave} after {t / 60:0}s score {g.Score} lives lost {99 - g.Lives} | {string.Join(" ", waveTimes)}");
            output.WriteLine($"   left: to come {g.ToCome}, spider ({g.Spider.X:0},{g.Spider.Y:0}) " + string.Join(" ", g.Bugs.Select(b => $"{b.Kind}({b.X:0},{b.Y:0} r{Field.DistFromCentre(b.X, b.Y):0} v{b.Vx:0},{b.Vy:0})")));
            output.WriteLine("   " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
            _ = coins;
        }
    }
}
