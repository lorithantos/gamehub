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
