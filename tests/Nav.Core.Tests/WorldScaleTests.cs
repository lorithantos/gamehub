using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// The calibration, and the reader that loads it.
/// </summary>
public sealed class WorldScaleTests(ITestOutputHelper output)
{
    private static string ConfigDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config");

    [Fact]
    public void TheShippedCalibrationCrossesABigMapInAboutTwoMinutes()
    {
        // The requirement, as a test rather than as a comment. Everything else
        // in the world is measured against this one number.
        var scale = WorldScale.Default;
        var seconds = scale.SecondsToCross(512);

        output.WriteLine(
            $"512 cells in {seconds:F0} s ({seconds / 60:F1} min); "
                + $"{scale.CellsPerSecond:F1} cells/s; {scale.MetresPerSecond * 3.6:F0} km/h; "
                + $"512 cells = {512 * scale.MetresPerCell / 1000:F2} km");

        Assert.InRange(seconds, 100, 150);
    }

    [Fact]
    public void AllThreeNumbersAreIndividuallyPlausible()
    {
        // The check that a calibration is honest rather than merely arithmetic:
        // pinning two of the three makes the third whatever it makes it, and the
        // old one gave 60 cells/s, which is 432 km/h and reads as nonsense the
        // moment anybody converts it.
        var scale = WorldScale.Default;

        Assert.InRange(scale.MetresPerSecond * 3.6, 10, 60);
        Assert.InRange(512 * scale.MetresPerCell, 500, 2000);
        Assert.InRange(scale.TicksPerSecond, 2, 20);
    }

    [Fact]
    public void RatesConvertFromSecondsRatherThanBeingWrittenPerTick()
    {
        // A rate written per tick silently rescales the whole game when the tick
        // changes, and the tick is one config line from changing.
        var scale = new WorldScale(0.25, 2.0);

        Assert.Equal(0.025, scale.PerTick(0.10), 12);
        Assert.Equal(0.10, scale.PerTick(0.10) * scale.TicksPerSecond, 12);
    }

    [Fact]
    public void TheShippedFileSaysWhatTheDefaultsSay()
    {
        // A config whose values have drifted from the compiled defaults is worse
        // than having neither: whichever one a reader believes will be the wrong
        // one half the time.
        var ini = Ini.FromFile(Path.Combine(ConfigDir, "scale.ini"));
        var fromFile = WorldScale.From(ini);

        Assert.Equal(WorldScale.Default.SecondsPerTick, fromFile.SecondsPerTick, 12);
        Assert.Equal(WorldScale.Default.MetresPerCell, fromFile.MetresPerCell, 12);
    }

    [Fact]
    public void AMissingFileIsRefusedUnlessTolerationIsAskedFor()
    {
        // Falling back quietly is how a thing ships running on numbers nobody
        // chose. The lenient reader still exists, for config that is genuinely
        // optional, but it has to be asked for by name.
        Assert.Throws<FileNotFoundException>(() => Ini.FromFile("nowhere.ini"));
        Assert.Equal(WorldScale.Default, WorldScale.From(Ini.FromFileOrEmpty("nowhere.ini")));
    }

    [Fact]
    public void EveryFallbackIsRecordedRatherThanSwallowed()
    {
        // Tolerating a missing value must never mean not knowing it was missing.
        var partial = Ini.Parse("[scale]\nsecondsPerTick = 0.5\n");
        var scale = WorldScale.From(partial);

        Assert.Equal(0.5, scale.SecondsPerTick, 12);
        Assert.Equal(WorldScale.Default.MetresPerCell, scale.MetresPerCell, 12);

        Assert.Contains("scale.metresPerCell", partial.Defaulted);
        Assert.DoesNotContain("scale.secondsPerTick", partial.Defaulted);
    }

    [Fact]
    public void TheShippedFileAnswersEveryKeyItIsAskedFor()
    {
        // Catches a key being added in code and forgotten in the file, which
        // otherwise runs on the compiled default and looks like it was
        // configured.
        var ini = Ini.FromFile(Path.Combine(ConfigDir, "scale.ini"));
        WorldScale.From(ini);

        Assert.Empty(ini.Defaulted);
    }

    [Fact]
    public void TheReaderHandlesSectionsCommentsAndInvariantNumbers()
    {
        var ini = Ini.Parse(
            "# a comment\n; another\n[one]\na = 1.5\n\n[two]\nb = hello world\nc=3\n[weapon.rifle]\nversus = 100, 35\n");

        Assert.Equal(1.5, ini.Number("one", "a", 0), 12);
        Assert.Equal("hello world", ini.Text("two", "b", ""));
        Assert.Equal(3, ini.Int("two", "c", 0));
        Assert.Equal("100, 35", ini.Text("weapon.rifle", "versus", ""));
        Assert.Equal(9.0, ini.Number("two", "missing", 9.0), 12);
    }

    [Fact]
    public void TheCombatTableIsOursAndIsShaped()
    {
        // Not a check on the values -- those are ours to choose. A check that
        // every weapon answers for every armour class, because a short row would
        // read as zero damage and look like a balance decision.
        var ini = Ini.FromFile(Path.Combine(ConfigDir, "combat.ini"));
        var armour = ini.Text("armour", "order", "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(armour);

        var weapons = new[] { "rifle", "autocannon", "cannon", "rocket", "flame" };
        foreach (var weapon in weapons)
        {
            var row = ini.Text("weapon." + weapon, "versus", "")
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            Assert.True(
                row.Length == armour.Length,
                $"{weapon} answers for {row.Length} armour classes and there are {armour.Length}");
        }

        output.WriteLine($"{weapons.Length} weapons x {armour.Length} armour classes: {string.Join(", ", armour)}");
        Assert.True(ini.Number("rates", "damagePerSecond", 0) > 0);
        Assert.True(
            ini.Number("rates", "selfHealPerSecond", 0) < ini.Number("rates", "damagePerSecond", 0),
            "self-healing must be outrunnable, or a veteran never needs a pad");
    }
}
