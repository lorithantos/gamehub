using System.Globalization;

namespace Nav.Core.Tests;

public sealed class ScenarioFileTests
{
    private const string TwoRecords =
        "version 1\n" +
        "0\tarena.map\t49\t49\t1\t11\t1\t12\t1.00000000\n" +
        "3\tarena.map\t49\t49\t38\t28\t38\t29\t2.41421356\n";

    [Fact]
    public void ReadsEveryFieldInOrder()
    {
        var records = ScenarioFile.FromText(TwoRecords);

        Assert.Equal(2, records.Count);

        var first = records[0];
        Assert.Equal(0, first.Bucket);
        Assert.Equal("arena.map", first.MapName);
        Assert.Equal(49, first.MapWidth);
        Assert.Equal(49, first.MapHeight);
        Assert.Equal(1, first.StartX);
        Assert.Equal(11, first.StartY);
        Assert.Equal(1, first.GoalX);
        Assert.Equal(12, first.GoalY);
        Assert.Equal(1.0, first.OptimalLength, 1e-9);

        Assert.Equal(3, records[1].Bucket);
        Assert.Equal(2.41421356, records[1].OptimalLength, 1e-9);
    }

    [Fact]
    public void CarriesTheLineItCameFrom()
    {
        var records = ScenarioFile.FromText(TwoRecords);

        Assert.Equal(2, records[0].LineNumber);
        Assert.Equal(3, records[1].LineNumber);
    }

    [Theory]
    [InlineData("version 1")]
    [InlineData("version 1.0")]
    public void BothSpellingsOfTheVersionLineAreAccepted(string versionLine)
    {
        var records = ScenarioFile.FromText($"{versionLine}\n0\ta.map\t2\t2\t0\t0\t1\t1\t1.41421356\n");

        Assert.Single(records);
    }

    [Theory]
    [InlineData("version 2")]
    [InlineData("v 1")]
    [InlineData("0\ta.map\t2\t2\t0\t0\t1\t1\t1.4")]
    public void AnyOtherFirstLineIsRefused(string firstLine)
    {
        var ex = Assert.Throws<MapFormatException>(() => ScenarioFile.FromText(firstLine + "\n"));

        Assert.Equal(1, ex.LineNumber);
    }

    [Fact]
    public void AShortRecordIsRefusedNamingTheLine()
    {
        var text = "version 1\n0\tarena.map\t49\t49\t1\t11\t1\t12\n";

        var ex = Assert.Throws<MapFormatException>(() => ScenarioFile.FromText(text));

        Assert.Equal(2, ex.LineNumber);
        Assert.Contains("9", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonNumericFieldIsRefusedNamingTheField()
    {
        var text = "version 1\n0\tarena.map\t49\t49\t1\t11\t1\tnorth\t1.0\n";

        var ex = Assert.Throws<MapFormatException>(() => ScenarioFile.FromText(text));

        Assert.Equal(2, ex.LineNumber);
        Assert.Contains("goalY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BlankLinesAreSkipped()
    {
        var records = ScenarioFile.FromText(TwoRecords + "\n\n");

        Assert.Equal(2, records.Count);
    }

    /// <summary>
    /// Pitfall from section 10, and the reason the parser pins InvariantCulture.
    /// </summary>
    /// <remarks>
    /// Under a comma-decimal culture <c>double.Parse("2.41421356")</c> does not
    /// throw -- it reads 241421356. Every expected value would be silently
    /// corrupted and the oracle would fail for a reason nowhere near the
    /// pathfinder.
    /// </remarks>
    [Fact]
    public void ACommaDecimalCultureDoesNotCorruptTheOptimalLength()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var records = ScenarioFile.FromText(TwoRecords);

            Assert.Equal(2.41421356, records[1].OptimalLength, 1e-9);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ARecordSizedForADifferentMapIsRefusedRatherThanScaled()
    {
        var record = ScenarioFile.FromText(TwoRecords)[0];
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        var ex = Assert.Throws<MapFormatException>(() => record.EnsureMatches(grid));

        Assert.Equal(2, ex.LineNumber);
        Assert.Contains("49x49", ex.Message, StringComparison.Ordinal);
        Assert.Contains("12x7", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordMatchingItsMapIsAccepted()
    {
        var record = ScenarioFile.FromText(TwoRecords)[0];
        var grid = Grid.FromMapFile(Fixtures.ArenaMap);

        record.EnsureMatches(grid);

        Assert.Equal(grid.Index(1, 11), record.StartIndex(grid));
        Assert.Equal(grid.Index(1, 12), record.GoalIndex(grid));
    }

    [Fact]
    public void FromFile_NamesTheFileInTheError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nav-{Guid.NewGuid():N}.scen");
        File.WriteAllText(path, "version 3\n");
        try
        {
            var ex = Assert.Throws<MapFormatException>(() => ScenarioFile.FromFile(path));

            Assert.Equal(path, ex.SourcePath);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
