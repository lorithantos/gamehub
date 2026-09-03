namespace Nav.Core.Models;

/// <summary>
/// What a cell is worth in metres and a tick in seconds — the two numbers the
/// whole simulation was missing.
/// </summary>
/// <remarks>
/// Unit speed used to be a DERIVED ACCIDENT rather than a chosen quantity.
/// Movement is one cell per tick, a tick was a sixtieth of a second because that
/// is what a render loop runs at, and nobody multiplied the two: units crossed
/// ground at sixty cells a second. With a cell at two metres that is 432 km/h,
/// and the whole guard demo — six promotions, four repair trips and a rotation —
/// happened in five and a third seconds.
/// <para>
/// <b>Pin any two and the third follows.</b> Here the two chosen are seconds per
/// tick and metres per cell, and cells per second falls out of the movement rule
/// that already existed. The check that a calibration is honest is that all
/// three read plausibly at once:
/// </para>
/// <list type="bullet">
/// <item><description>512 cells edge to edge in 128 seconds — the two minutes
/// asked for.</description></item>
/// <item><description>512 cells is 1.02 km, which is a battlefield rather than a
/// car park.</description></item>
/// <item><description>4 cells a second at 2 m is 29 km/h, which is a tracked
/// vehicle.</description></item>
/// </list>
/// <para>
/// <b>Rates belong in seconds, never in ticks.</b> A rate written per tick
/// silently rescales the entire game when the tick changes, and it changes here
/// by editing one line of a config file. Anything expressed per tick in this
/// codebase is a bug waiting for somebody to retune the clock.
/// </para>
/// </remarks>
/// <param name="SecondsPerTick">How long one simulation step lasts.</param>
/// <param name="MetresPerCell">How much ground one cell covers.</param>
public readonly record struct WorldScale(double SecondsPerTick, double MetresPerCell)
{
    /// <summary>The shipped calibration, used when no config says otherwise.</summary>
    public static WorldScale Default { get; } = new(0.25, 2.0);

    /// <summary>Simulation steps per second.</summary>
    public double TicksPerSecond => 1.0 / SecondsPerTick;

    /// <summary>
    /// How fast the quickest unit covers ground, in cells. One cell per tick is
    /// the movement rule, so this is the tick rate — slower units wait a tick,
    /// which is how a speed spread is expressed without sub-cell positions.
    /// </summary>
    public double CellsPerSecond => TicksPerSecond;

    /// <summary>The quickest unit's speed in metres per second.</summary>
    public double MetresPerSecond => CellsPerSecond * MetresPerCell;

    /// <summary>Seconds for the quickest unit to cross <paramref name="cells"/> of open ground.</summary>
    public double SecondsToCross(int cells) => cells * SecondsPerTick;

    /// <summary>Converts a rate written per second into the per-tick amount the loop applies.</summary>
    public double PerTick(double perSecond) => perSecond * SecondsPerTick;

    /// <summary>Reads the <c>[scale]</c> section, falling back to <see cref="Default"/> per key.</summary>
    public static WorldScale From(Ini ini)
    {
        ArgumentNullException.ThrowIfNull(ini);

        return new WorldScale(
            ini.Number("scale", "secondsPerTick", Default.SecondsPerTick),
            ini.Number("scale", "metresPerCell", Default.MetresPerCell));
    }
}
