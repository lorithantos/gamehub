namespace Nav.Core.Tests;

public sealed class FixedTimestepTests
{
    private const double Sixtieth = 1.0 / 60.0;

    [Fact]
    public void ShortFramesBankUntilAWholeStepIsDue()
    {
        var clock = new FixedTimestep();

        Assert.Equal(0, clock.Accumulate(Sixtieth / 3.0));
        Assert.Equal(0, clock.Accumulate(Sixtieth / 3.0));
        Assert.Equal(1, clock.Accumulate(Sixtieth / 3.0));
    }

    [Fact]
    public void ALongFrameYieldsSeveralSteps()
    {
        var clock = new FixedTimestep();

        Assert.Equal(6, clock.Accumulate(Sixtieth * 6.0));
    }

    [Fact]
    public void TheRemainderIsCarriedRatherThanDropped()
    {
        var clock = new FixedTimestep();

        clock.Accumulate((Sixtieth * 2.0) + (Sixtieth / 2.0));

        Assert.Equal(Sixtieth / 2.0, clock.Pending, 1e-12);
    }

    [Fact]
    public void ASecondOfFramesIsSixtyStepsHoweverItIsSliced()
    {
        var wholeFrames = new FixedTimestep();
        var total = 0;
        for (var i = 0; i < 60; i++)
        {
            total += wholeFrames.Accumulate(Sixtieth);
        }

        Assert.Equal(60, total);

        var choppedFrames = new FixedTimestep();
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
        var clock = new FixedTimestep(maxStepsPerFrame: 8);

        // Five seconds is 300 steps. Running them all would take longer than the
        // frame trying to catch up, which is the spiral of death.
        Assert.Equal(8, clock.Accumulate(5.0));

        // And the surplus is gone rather than waiting to fire next frame.
        Assert.Equal(0.0, clock.Pending);
    }

    [Fact]
    public void ResetDropsWhatWasBanked()
    {
        var clock = new FixedTimestep();
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedTimestep(maxStepsPerFrame: 0));
    }

    [Fact]
    public void ANegativeDeltaIsRefused()
    {
        var clock = new FixedTimestep();

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Accumulate(-0.1));
    }
}
