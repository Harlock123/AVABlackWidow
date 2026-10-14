using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVABlackWidow.Engine;
using AVABlackWidow.Views;

namespace AVABlackWidow.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set WIDOW_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avablackwidow-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("WIDOW_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVABlackWidow-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer spider play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 120)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("RLR", 61250, 9);
        g.HighScores.Insert("AVA", 44800, 7);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Defending_the_web()
    {
        var (w, c, g) = Open(3);
        g.StartGame();
        g.SetLives(30);
        g.Idle(40);
        c.Advance(0.5);
        Shot(w, c, "wave-start");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Bugs.Count >= 3 && g.Shots.Count >= 2 && g.Coins.Count >= 1));
        Shot(w, c, "gnats");
        At(g, 2);
        Assert.True(PlayUntil(g, c, g => g.Bugs.Count(b => b.Kind == BugKind.Beetle) >= 2 && g.Bugs.Count >= 4 && g.Coins.Count >= 1));
        Shot(w, c, "beetles");
        At(g, 3);
        Assert.True(PlayUntil(g, c, g => g.Bugs.Count(b => b.Kind == BugKind.Egg) >= 2 && g.Bugs.Any(b => b.Kind == BugKind.Hornet)));
        Shot(w, c, "hornets-and-eggs");
        At(g, 4);
        Assert.True(PlayUntil(g, c, g => g.Effects.Any(e => e.Kind == EffectKind.Blast && e.Age is > 0.1 and < 0.2) && g.Effects.Count(e => e.Kind == EffectKind.Burst && e.Age < 0.2) >= 3 && g.Spider.Alive, 400));
        Shot(w, c, "bomber-blast");
        At(g, 6);
        Assert.True(PlayUntil(g, c, g => g.Bugs.Any(b => b.Kind == BugKind.Slayer && Field.DistFromCentre(b.X, b.Y) < Field.R - 60) && g.Bugs.Count >= 4));
        Shot(w, c, "slayer");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.WaveClear && g.PhaseTime > 0.4));
        Shot(w, c, "wave-clear");
    }

    /// <summary>Straight into the play of a wave, with spiders to spare.</summary>
    private static void At(GameEngine g, int wave)
    {
        g.Go(wave);
        g.SetLives(30);
    }

    [AvaloniaFact]
    public void Caught()
    {
        var (w, c, g) = Open(4);
        g.StartGame();
        g.Idle((int)(60 * GameEngine.StartSeconds) + 60 * 3);
        // Stand still and let them come.
        for (int t = 0; t < 60 * 60 && g.Phase != Phase.Dying; t++)
        {
            g.Tick(default);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        g.Idle(18);
        c.Advance(0.3);
        Shot(w, c, "caught");
    }
}
