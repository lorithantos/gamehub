namespace Nav.Viewer.Models;

/// <summary>
/// How a unit's health is DEPICTED: where the colour changes, what the three
/// bands are painted with, and how big the bar is.
/// </summary>
/// <remarks>
/// <b>PRESENTATION OVER AN EXISTING FACT.</b> Nothing here decides when a unit
/// retreats, when it is destroyed or what any side knows about it -- those are
/// the tactics layer's, and a doctrine's thresholds have nothing to do with
/// these. This says only where the bar stops being green.
/// <para>
/// A table for the same reason <see cref="FogStyle"/> is one: the alternatives
/// are values rather than features. Three bands or five, a bar that hides itself
/// at full health, thresholds a demo wants moved so the colour change is on
/// screen while somebody is watching -- each of those is a row to pass, and none
/// of them is a branch to write. <see cref="Default"/> is the only row today.
/// </para>
/// <para>
/// A host that wants one member changed says
/// <c>HealthStyle.Default with { Track = RgbaColor.Black }</c> and inherits the
/// reasoning behind the rest.
/// </para>
/// </remarks>
public sealed record HealthStyle
{
    /// <summary>
    /// What the viewer draws when nobody said otherwise, which is every host
    /// today.
    /// </summary>
    /// <remarks>
    /// Each member carries its own value and the argument for it, so this is the
    /// whole row and not a second place a number is written down.
    /// </remarks>
    public static HealthStyle Default { get; } = new();

    /// <summary>
    /// Above this fraction a unit is drawn <see cref="Healthy"/>. At it exactly
    /// it is <see cref="Hurt"/>.
    /// </summary>
    /// <remarks>
    /// BOTH BOUNDARIES BELONG TO THE MIDDLE BAND, here and at
    /// <see cref="CriticalBelow"/>, so a unit sitting exactly on a threshold has
    /// one colour rather than whichever of two the comparison happened to be
    /// written with. The middle band owns them because it is the one that says
    /// "something has happened to this unit", and the moment that becomes true
    /// is the moment worth showing.
    /// <para>
    /// 0.6 rather than any other number is a first guess and nothing more:
    /// it puts the first colour change well before a unit is in trouble, which
    /// is what a watcher wants from a bar they are not staring at.
    /// </para>
    /// </remarks>
    public float HealthyAbove { get; init; } = 0.6f;

    /// <summary>
    /// Below this fraction a unit is drawn <see cref="Critical"/>. At it exactly
    /// it is <see cref="Hurt"/>.
    /// </summary>
    /// <remarks>
    /// 0.3 is a first guess as well, chosen against nothing in the simulation --
    /// deliberately, because a display threshold that tracked a doctrine's
    /// retreat threshold would quietly become a second copy of it, and the two
    /// would drift the first time a rank changed one of them.
    /// </remarks>
    public float CriticalBelow { get; init; } = 0.3f;

    /// <summary>Above <see cref="HealthyAbove"/>: nothing to worry about yet.</summary>
    public RgbaColor Healthy { get; init; } = RgbaColor.Rgb(70, 200, 80);

    /// <summary>Between the two thresholds, and on either of them.</summary>
    public RgbaColor Hurt { get; init; } = RgbaColor.Rgb(225, 200, 60);

    /// <summary>Below <see cref="CriticalBelow"/>: one more exchange and it is gone.</summary>
    public RgbaColor Critical { get; init; } = RgbaColor.Rgb(220, 60, 55);

    /// <summary>
    /// The full-width bar the coloured fill is drawn over, so what is MISSING is
    /// as visible as what is left.
    /// </summary>
    /// <remarks>
    /// The two-line bar is the whole reason this reads as damage. One line
    /// alone shortens as a unit is hurt, and a short bar at a distance is
    /// indistinguishable from a full bar on a unit that happens to be drawn
    /// smaller -- so half health looked like a healthy unit further away.
    /// <para>
    /// Dark rather than mid-grey: it has to separate from the terrain underneath
    /// without competing with the three colours on top of it.
    /// </para>
    /// </remarks>
    public RgbaColor Track { get; init; } = RgbaColor.Rgb(30, 30, 34);

    /// <summary>
    /// How wide the bar is, as a multiple of the cell size.
    /// </summary>
    /// <remarks>
    /// EVERY NUMBER IN THIS BLOCK IS A MULTIPLE OF THE CELL, not a pixel count,
    /// so the bar holds its proportions at any zoom -- which is the same thing
    /// the unit's own radius, the route thickness and the no-route cross all do.
    /// A pixel width would be a bar wider than the unit on a zoomed-out map and
    /// a smear on a zoomed-in one.
    /// <para>
    /// 0.8 of a cell is a little wider than the unit's disc, whose radius is
    /// 0.34 of a cell, so the bar reads as belonging to the unit and still
    /// gives the fill somewhere to be seen shrinking.
    /// </para>
    /// </remarks>
    public float Width { get; init; } = 0.8f;

    /// <summary>How thick the bar is, as a multiple of the cell size.</summary>
    /// <remarks>
    /// 0.12, which is the route's thickness: two marks of the same weight on the
    /// same map, and thin enough that a crowd of units is not a wall of bars.
    /// </remarks>
    public float Thickness { get; init; } = 0.12f;

    /// <summary>
    /// How far above the unit's centre the bar sits, as a multiple of the cell
    /// size.
    /// </summary>
    /// <remarks>
    /// 0.55 clears the disc's 0.34 radius and the no-route cross's arms reach
    /// 1.2 radii -- about 0.41 of a cell -- so the bar sits above both without
    /// climbing into the row of units above it.
    /// </remarks>
    public float Above { get; init; } = 0.55f;
}
