using AVABlackWidow.Engine;
using Engine = AVABlackWidow.Engine;

namespace AVABlackWidow.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avablackwidow-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in the play of a wave, with no bugs arriving unless a test puts them there.</summary>
    private static GameEngine Game(int wave = 1)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        return g;
    }

    private static Engine.Controls Walk(double x, double y) => new(x, y);
    private static Engine.Controls Shoot(double x, double y) => new(0, 0, x, y);

    // ───────────────────────────── the spider ─────────────────────────────

    [Fact]
    public void The_spider_walks_any_way_but_cannot_leave_the_web()
    {
        var g = Game();
        g.Run(30, Walk(1, 0));
        Assert.Equal(Field.CX + GameEngine.SpiderSpeed / 2, g.Spider.X, 0);
        Assert.Equal(0, g.Spider.Facing, 3);
        g.Run(600, Walk(0.7, 0.7));
        Assert.True(Field.DistFromCentre(g.Spider.X, g.Spider.Y) < Field.R - Spider.Radius);
    }

    [Fact]
    public void It_shoots_the_way_the_other_stick_points_a_few_at_a_time()
    {
        var g = Game();
        g.Run(1, Shoot(0, -1));
        var s = Assert.Single(g.Shots);
        Assert.Equal(0, s.Vx, 6);
        Assert.Equal(-GameEngine.ShotSpeed, s.Vy, 6);
        g.Run(60, Shoot(-1, 1));
        Assert.InRange(g.Shots.Count, 2, GameEngine.MaxShots);
        // Shots die at the edge of the web.
        g.Idle(60);
        Assert.Empty(g.Shots);
    }

    // ───────────────────────────── the bugs ─────────────────────────────

    [Fact]
    public void A_shot_gnat_scores_and_leaves_a_bounty_coin_that_drifts_out_and_is_lost()
    {
        var g = Game();
        g.Put(BugKind.Gnat, Field.CX + 150, Field.CY);
        g.Run(4, Shoot(1, 0));
        g.Idle(20);
        Assert.Empty(g.Bugs);
        Assert.Equal(100, g.Score);
        var coin = Assert.Single(g.Coins);
        double r = Field.DistFromCentre(coin.X, coin.Y);
        g.Idle(60);
        Assert.True(Field.DistFromCentre(coin.X, coin.Y) > r);
        g.Idle(60 * 8);
        Assert.Empty(g.Coins);
    }

    [Fact]
    public void Coins_in_a_row_are_worth_more_each_time_up_to_the_cap_and_a_caught_spider_starts_again()
    {
        var g = Game();
        int expected = 0;
        for (int i = 0; i < 20; i++)
        {
            g.PutCoin(g.Spider.X + 5, g.Spider.Y);
            g.Idle(1);
            expected += Math.Min(GameEngine.CoinCap, GameEngine.CoinStep * (i + 1));
        }
        Assert.Equal(expected, g.Score);
        Assert.Equal(GameEngine.CoinCap, g.Bounty);
        g.Put(BugKind.Gnat, g.Spider.X + 4, g.Spider.Y);
        g.Idle(1);
        Assert.Equal(GameEngine.CoinStep, g.Bounty);
    }

    [Fact]
    public void A_beetle_takes_three_hits_and_each_knocks_it_back()
    {
        var g = Game();
        var b = g.Put(BugKind.Beetle, Field.CX + 200, Field.CY);
        double x = b.X;
        g.Run(1, Shoot(1, 0));
        g.Idle(25);
        Assert.False(b.Dead);
        Assert.Equal(2, b.Hp);
        Assert.True(b.X > x);
        g.Run(1, Shoot(1, 0));
        g.Idle(25);
        g.Run(1, Shoot(1, 0));
        g.Idle(25);
        Assert.True(b.Dead);
        Assert.Equal(250, g.Score);
    }

    [Fact]
    public void A_beetle_crawls_in_to_the_hub_and_then_comes_for_the_spider()
    {
        var g = Game();
        g.MoveSpider(Field.CX, Field.CY + 200);
        var b = g.Put(BugKind.Beetle, Field.CX + 300, Field.CY);
        for (int t = 0; t < 60 * 30 && g.Phase == Phase.Play; t++) g.Idle(1);
        Assert.True(b.Hunting);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("CRUSHED BY A BEETLE", g.Cause);
    }

    [Fact]
    public void Hornets_lay_eggs_that_hatch_into_gnats_unless_eaten_or_shot()
    {
        var g = Game();
        g.MoveSpider(Field.CX - 250, Field.CY);
        var h = g.Put(BugKind.Hornet, Field.CX + 100, Field.CY);
        h.Timer = 0.1;
        g.Idle(10);
        var egg = Assert.Single(g.Bugs, b => b.Kind == BugKind.Egg);
        // Shot: 50.
        g.Bugs.Remove(h);
        g.MoveSpider(egg.X - 100, egg.Y);
        g.Run(2, Shoot(1, 0));
        g.Idle(20);
        Assert.True(egg.Dead);
        Assert.Equal(50, g.Score);
        // Eaten: 100.
        var egg2 = g.Put(BugKind.Egg, g.Spider.X + 6, g.Spider.Y);
        g.Idle(1);
        Assert.True(egg2.Dead);
        Assert.Equal(150, g.Score);
        Assert.Equal(Phase.Play, g.Phase);
        // Left alone, it hatches.
        var egg3 = g.Put(BugKind.Egg, Field.CX + 200, Field.CY + 100);
        g.Idle((int)(60 * GameEngine.HatchSeconds) + 2);
        Assert.Equal(BugKind.Gnat, egg3.Kind);
    }

    [Fact]
    public void A_shot_bomber_blows_up_the_bugs_around_it_for_double_points()
    {
        var g = Game();
        g.Put(BugKind.Bomber, Field.CX + 200, Field.CY);
        g.Put(BugKind.Gnat, Field.CX + 200, Field.CY + 60);
        g.Put(BugKind.Hornet, Field.CX + 260, Field.CY - 40);
        var far = g.Put(BugKind.Gnat, Field.CX - 250, Field.CY);
        g.Run(1, Shoot(1, 0));
        g.Idle(25);
        Assert.Single(g.Bugs);
        Assert.Same(far, g.Bugs[0]);
        Assert.Equal(150 + 2 * 100 + 2 * 300, g.Score);
        Assert.Equal(3, g.Coins.Count);
    }

    [Fact]
    public void A_slayer_hunts_the_spider_down()
    {
        var g = Game(5);
        g.Put(BugKind.Slayer, Field.CX + 300, Field.CY);
        for (int t = 0; t < 60 * 10 && g.Phase == Phase.Play; t++) g.Run(1, Walk(-1, 0.3));
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("CAUGHT BY THE SLAYER", g.Cause);
    }

    [Fact]
    public void A_wave_that_drags_on_brings_out_a_slayer()
    {
        var g = Game(2);
        g.Calm = false;
        g.SetToCome(0);
        g.Put(BugKind.Gnat, Field.CX + 300, Field.CY);
        g.SetWaveTime(GameEngine.SlayerAfter + 0.1);
        g.Idle(1);
        Assert.Contains(g.Bugs, b => b.Kind == BugKind.Slayer);
    }

    // ───────────────────────────── waves and spiders ─────────────────────────────

    [Fact]
    public void Each_wave_brings_on_its_new_kind_of_bug()
    {
        var seen = new Dictionary<int, HashSet<BugKind>>();
        foreach (int wave in new[] { 1, 2, 3, 4, 5 })
        {
            var g = Game(wave);
            g.Calm = false;
            g.SetLives(99);
            var kinds = new HashSet<BugKind>();
            for (int t = 0; t < 60 * 40; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                foreach (var b in g.Bugs) kinds.Add(b.Kind);
                if (g.Wave != wave) break;
            }
            seen[wave] = kinds;
        }
        Assert.Equal([BugKind.Gnat], seen[1]);
        Assert.Contains(BugKind.Beetle, seen[2]);
        Assert.DoesNotContain(BugKind.Hornet, seen[2]);
        Assert.Contains(BugKind.Hornet, seen[3]);
        Assert.Contains(BugKind.Bomber, seen[4]);
        Assert.Contains(BugKind.Slayer, seen[5]);
    }

    [Fact]
    public void Clearing_a_wave_pays_a_bonus_and_brings_the_next()
    {
        var g = Game(3);
        g.Calm = false;
        g.SetToCome(0);
        var b = g.Put(BugKind.Gnat, Field.CX + 100, Field.CY);
        g.Run(3, Shoot(1, 0));
        g.Idle(20);
        Assert.Equal(Phase.WaveClear, g.Phase);
        Assert.Equal(100 + 3000, g.Score);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 2);
        Assert.Equal(4, g.Wave);
        Assert.Equal(Phase.WaveStart, g.Phase);
        Assert.Equal(10 + 3 * 4, g.ToCome);
        _ = b;
    }

    [Fact]
    public void A_caught_spider_comes_back_in_the_middle_with_the_bugs_there_swept_away_and_the_last_ends_the_game()
    {
        var g = Game();
        g.Put(BugKind.Gnat, g.Spider.X + 4, g.Spider.Y);
        var near = g.Put(BugKind.Gnat, Field.CX + 60, Field.CY + 60);
        var far = g.Put(BugKind.Gnat, Field.CX + 300, Field.CY);
        g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal(2, g.Lives);
        int toCome = g.ToCome;
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.True(g.Spider.Alive);
        Assert.DoesNotContain(near, g.Bugs);
        Assert.Contains(far, g.Bugs);
        // The gnat that caught it was in the middle too: both come back later in the wave.
        Assert.Equal(toCome + 2, g.ToCome);
        g.SetLives(1);
        g.Put(BugKind.Gnat, g.Spider.X + 4, g.Spider.Y);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 4);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Twenty_thousand_points_earn_another_spider()
    {
        var g = Game();
        g.SetScore(19950);
        g.Put(BugKind.Gnat, Field.CX + 150, Field.CY);
        g.Run(3, Shoot(1, 0));
        g.Idle(20);
        Assert.Equal(4, g.Lives);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_computer_spider_clears_the_first_waves()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        for (int t = 0; t < 60 * 120 && g.Wave < 4 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(4, g.Wave);
        Assert.True(g.Lives >= 3);
    }

    [Fact]
    public void Left_alone_the_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 20);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 20)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 60; t++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Spider.X:0.000} {g.Spider.Y:0.000} {g.Wave} {g.Lives} {g.Bugs.Count} {g.Coins.Count}";
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.SetScore(99999);
        g.SetLives(1);
        g.Put(BugKind.Gnat, g.Spider.X + 4, g.Spider.Y);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 4)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "BLK") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("BLK", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
