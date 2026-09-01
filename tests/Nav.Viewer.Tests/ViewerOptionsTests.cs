namespace Nav.Viewer.Tests;

public sealed class ViewerOptionsTests
{
    [Fact]
    public void NoArgumentsMeansTheEmbeddedFixture()
    {
        Assert.True(ViewerOptions.TryParse([], out var options, out var error));

        Assert.Null(error);
        Assert.Null(options.MapPath);
        Assert.Null(options.MaxFrames);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void TheFirstNonFlagArgumentIsTheMapPath()
    {
        // The milestone-1 contract. This must keep working byte for byte.
        Assert.True(ViewerOptions.TryParse(["maps/arena.map"], out var options, out _));

        Assert.Equal("maps/arena.map", options.MapPath);
    }

    [Theory]
    [InlineData("--frames", "300", "maps/arena.map")]
    [InlineData("maps/arena.map", "--frames", "300")]
    [InlineData("--frames=300", "maps/arena.map")]
    public void FramesParsesInEitherFormAndEitherPosition(params string[] args)
    {
        Assert.True(ViewerOptions.TryParse(args, out var options, out _));

        Assert.Equal("maps/arena.map", options.MapPath);
        Assert.Equal(300, options.MaxFrames);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpShortCircuits(string flag)
    {
        Assert.True(ViewerOptions.TryParse([flag, "maps/arena.map"], out var options, out _));

        Assert.True(options.ShowHelp);
    }

    [Theory]
    [InlineData("--scenario", "runs/headon.scenario")]
    [InlineData("--scenario=runs/headon.scenario")]
    public void ScenarioParsesInEitherForm(params string[] args)
    {
        Assert.True(ViewerOptions.TryParse(args, out var options, out _));

        Assert.Equal("runs/headon.scenario", options.ScenarioPath);
        Assert.Null(options.MapPath);
    }

    [Fact]
    public void AScenarioAndAnExplicitMapCanCoexist()
    {
        // The explicit map wins over the one the scenario names.
        Assert.True(ViewerOptions.TryParse(["a.map", "--scenario", "b.scenario"], out var options, out _));

        Assert.Equal("a.map", options.MapPath);
        Assert.Equal("b.scenario", options.ScenarioPath);
    }

    [Theory]
    [InlineData("--scenario")]
    [InlineData("--scenario=")]
    public void AScenarioWithoutAPathIsRefused(params string[] args)
    {
        Assert.False(ViewerOptions.TryParse(args, out _, out var error));

        Assert.NotNull(error);
        Assert.Contains("--scenario", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheScenarioMapResolvesBesideTheFileThenOneDirectoryUp()
    {
        // The fixture layout: scenarios/ sits inside the map folder.
        var root = Directory.CreateTempSubdirectory("nav-viewer-test-");
        try
        {
            var scenarios = Directory.CreateDirectory(Path.Combine(root.FullName, "scenarios"));
            var scenarioPath = Path.Combine(scenarios.FullName, "x.scenario");
            File.WriteAllText(scenarioPath, "unused");

            var above = Path.Combine(root.FullName, "m.map");
            File.WriteAllText(above, "unused");
            Assert.Equal(above, ViewerOptions.ResolveScenarioMap(scenarioPath, "m.map"));

            var beside = Path.Combine(scenarios.FullName, "m.map");
            File.WriteAllText(beside, "unused");
            Assert.Equal(beside, ViewerOptions.ResolveScenarioMap(scenarioPath, "m.map"));

            // Nothing found: hand back the beside-path so the loader's refusal
            // names something real.
            Assert.Equal(
                Path.Combine(scenarios.FullName, "missing.map"),
                ViewerOptions.ResolveScenarioMap(scenarioPath, "missing.map"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void AnUnknownFlagIsRefusedRatherThanIgnored()
    {
        Assert.False(ViewerOptions.TryParse(["--renderer", "d3d11"], out _, out var error));

        Assert.NotNull(error);
        Assert.Contains("--renderer", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondMapPathIsRefused()
    {
        Assert.False(ViewerOptions.TryParse(["a.map", "b.map"], out _, out var error));

        Assert.NotNull(error);
        Assert.Contains("b.map", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("many")]
    public void ANonPositiveOrNonNumericFrameCountIsRefused(string value)
    {
        Assert.False(ViewerOptions.TryParse(["--frames", value], out _, out var error));

        Assert.NotNull(error);
        Assert.Contains("--frames", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FramesWithNoValueIsRefused()
    {
        Assert.False(ViewerOptions.TryParse(["--frames"], out _, out var error));

        Assert.NotNull(error);
    }

    [Fact]
    public void UsageTextNamesEveryOption()
    {
        var usage = ViewerOptions.UsageText;

        Assert.Contains("--frames", usage, StringComparison.Ordinal);
        Assert.Contains("--help", usage, StringComparison.Ordinal);
        Assert.Contains("map-path", usage, StringComparison.Ordinal);
    }
}
