namespace Nav.Core.Tests;

/// <summary>
/// The accumulator's arithmetic: banking, carrying, and refusing to catch up.
/// </summary>
/// <remarks>
/// Every test states its OWN step rather than taking the shipped default. They
/// did take the default, and when the world was calibrated -- a tick became a
/// quarter second rather than a sixtieth, because one cell per tick at 60 Hz is
/// 432 km/h -- four of them failed. Nothing about banking a short frame had
/// changed; they were asserting the project''s clock speed while claiming to test
/// an accumulator, and one of them said so in its name.
/// </remarks>
public sealed class FixedTimestepTests
{
    /// <summary>A step chosen here, so these tests do not move when the world does.</summary>
    private const double Sixtieth = 1.0 / 60.0;

    private static FixedTimestep Clock(int maxStepsPerFrame = 8) =>
        new(Sixtieth, maxStepsPerFrame);

    [Fact]
    public void ShortFramesBankUntilAWholeStepIsDue()
    {
        var clock = Clock();

        Assert.Equal(0, clock.Accumulate(Sixtieth / 3.0));
        Assert.Equal(0, clock.Accumulate(Sixtieth / 3.0));
        Assert.Equal(1, clock.Accumulate(Sixtieth / 3.0));
    }

    [Fact]
    public void ALongFrameYieldsSeveralSteps()
    {
        var clock = Clock();

        Assert.Equal(6, clock.Accumulate(Sixtieth * 6.0));
    }

    [Fact]
    public void TheRemainderIsCarriedRatherThanDropped()
    {
        var clock = Clock();

        clock.Accumulate((Sixtieth * 2.0) + (Sixtieth / 2.0));

        Assert.Equal(Sixtieth / 2.0, clock.Pending, 1e-12);
    }

    [Fact]
    public void ASecondOfFramesIsTheSameCountHoweverItIsSliced()
    {
        var wholeFrames = Clock();
        var total = 0;
        for (var i = 0; i < 60; i++)
        {
            total += wholeFrames.Accumulate(Sixtieth);
        }

        Assert.Equal(60, total);

        var choppedFrames = Clock();
        var chopped = 0;
        for (var i = 0; i < 240; i++)
        {
            chopped += choppedFrames.Accumulate(1.0 / 240.0);
        }

        Assert.Equal(60, chopped);
    }

    [Fact]
    public void AStallIsCappedRatherThanCaughtUpOn()
    {
        var clock = Clock(maxStepsPerFrame: 8);

        // Five seconds is 300 steps. Running them all would take longer than the
        // frame trying to catch up, which is the spiral of death.
        Assert.Equal(8, clock.Accumulate(5.0));

        // And the surplus is gone rather than waiting to fire next frame.
        Assert.Equal(0.0, clock.Pending);
    }

    [Fact]
    public void ResetDropsWhatWasBanked()
    {
        var clock = Clock();
        clock.Accumulate(Sixtieth / 2.0);

        clock.Reset();

        Assert.Equal(0.0, clock.Pending);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void ANonPositiveStepIsRefused(double step)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedTimestep(step));
    }

    [Fact]
    public void ANonPositiveStepCapIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedTimestep(Sixtieth, maxStepsPerFrame: 0));
    }

    [Fact]
    public void ANegativeDeltaIsRefused()
    {
        var clock = Clock();

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Accumulate(-0.1));
    }
}
