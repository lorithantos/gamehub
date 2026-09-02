using System.Text;
using System.Text.Json;

namespace Nav.Tactics;

/// <summary>
/// A recording of a squad demo: the map, the fixtures, and every tick's units
/// with their health and errands.
/// </summary>
/// <remarks>
/// One JSON object per line, header first, exactly as
/// <c>Nav.Core.ScenarioTrace</c> writes a movement trace -- a growing file that
/// survives a crash mid-run, and a diff that shows the tick where two runs part.
/// This one carries what a tactical demo is about and a movement trace has no
/// notion of: health, who is away and where, hostiles, repair points.
/// <para>
/// Written for watching. The whole point of the demos is that a doctrine's
/// decisions are visible, so this is the file an animation reads.
/// </para>
/// </remarks>
public static class DemoTrace
{
    /// <summary>Bumped when the shape changes, so a reader can refuse an old file.</summary>
    /// <remarks>
    /// 2 added <c>rank</c> to every unit. A version-1 reader shown one of these
    /// would draw everything correctly and silently show no chevrons, which is
    /// the reason the number exists: a replay that quietly omits the thing the
    /// demo is about is worse than one that refuses to open.
    /// <para>
    /// 3 added <c>exposureRadius</c> to the header, for the same reason
    /// <see cref="WriteHeader"/> already carries a leash: it is the radius that
    /// DECIDES something, and a replay showing three units promoted and three
    /// not, with no circle on screen, is showing an outcome with its cause left
    /// out. Bumped rather than added quietly, because two different shapes both
    /// called version 2 is exactly the sloppiness the number exists to stop.
    /// </para>
    /// </remarks>
    public const int Version = 3;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record HeaderLine(
        int Version,
        string Name,
        string Description,
        int Width,
        int Height,
        string Walls,
        IReadOnlyList<int> RepairPoints,
        IReadOnlyList<int> Route,
        double Leash,
        double ExposureRadius,
        int Ticks);

    /// <remarks>
    /// Carries what the unit is DOING as well as where it is, because a replay
    /// that shows only position makes correct behaviour look broken: a unit
    /// waiting for somebody else's reserved cell stands still for no visible
    /// reason. The live viewer learned this and colours queued units apart from
    /// stuck ones; a demo page cannot do the same without these three flags.
    /// The goal travels for the same reason -- it is why the unit is walking
    /// where it is walking.
    /// <para>
    /// Rank travels for a third reason: without it the replay shows a squad in
    /// which one unit leaves at a scratch and another holds at half health, and
    /// nothing on screen says why. It is the one field here that explains a
    /// decision rather than reporting a state.
    /// </para>
    /// </remarks>
    private sealed record UnitLine(
        int Id,
        int X,
        int Y,
        int GoalX,
        int GoalY,
        double Health,
        int Rank,
        int ErrandX,
        int ErrandY,
        bool Arrived,
        bool Thinking,
        bool Waiting,
        int Stalled);

    private sealed record TickLine(
        int Tick,
        IReadOnlyList<UnitLine> Units,
        IReadOnlyList<int> Hostiles,
        int AnchorX,
        int AnchorY,
        string? Note);

    /// <summary>
    /// Writes the header. Walls travel as one string of '.' and '@' in row
    /// order, which is the map itself and reads as a picture in the file.
    /// </summary>
    /// <param name="writer">Where the line goes.</param>
    /// <param name="name">The demo's short name.</param>
    /// <param name="description">One line on what it shows.</param>
    /// <param name="grid">The map.</param>
    /// <param name="repairPoints">Cells that repair, for the replay to mark.</param>
    /// <param name="ticks">How many ticks follow.</param>
    /// <param name="route">A patrol's waypoints in order, or empty.</param>
    /// <param name="leash">
    /// How far a patrol may be drawn from its waypoint, so a replay can draw the
    /// radius that decides what the units do. Zero when the demo has no leash.
    /// </param>
    /// <param name="exposureRadius">
    /// How close to a hostile a unit must be to be earning rank, so a replay can
    /// draw the circle that explains why some of a formation were promoted and
    /// the rest were not. Zero when the demo does not model exposure.
    /// </param>
    public static void WriteHeader(
        TextWriter writer, string name, string description, Grid grid, IReadOnlyList<int> repairPoints, int ticks,
        IReadOnlyList<int>? route = null, double leash = 0.0, double exposureRadius = 0.0)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(repairPoints);

        var walls = new StringBuilder(grid.CellCount);
        for (var cell = 0; cell < grid.CellCount; cell++)
        {
            walls.Append(grid.IsPassable(cell) ? '.' : '#');
        }

        writer.WriteLine(JsonSerializer.Serialize(
            new HeaderLine(
                Version, name, description, grid.Width, grid.Height, walls.ToString(),
                [.. repairPoints], [.. route ?? []], leash, exposureRadius, ticks),
            Options));
    }

    /// <summary>
    /// Writes one tick: every unit's position, health, errand and arrival, plus
    /// where the hostiles are and where the squad is anchored.
    /// </summary>
    /// <param name="writer">Where the line goes.</param>
    /// <param name="grid">The map, for turning cells into coordinates.</param>
    /// <param name="tick">Which tick this is.</param>
    /// <param name="agents">Every unit, as the movement system reports it.</param>
    /// <param name="world">The world this tick, for health and hostiles.</param>
    /// <param name="anchor">Where the squad is stationed, or -1.</param>
    /// <param name="note">
    /// What just happened, in the demo's own words -- "unit 2 falls back to
    /// repair". Null on a tick where nothing worth narrating occurred.
    /// </param>
    public static void WriteTick(
        TextWriter writer,
        Grid grid,
        int tick,
        IReadOnlyList<AgentState> agents,
        IPerception world,
        int anchor,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(world);

        var units = agents
            .Select(a => new UnitLine(
                a.Id,
                grid.ColumnOf(a.Cell), grid.RowOf(a.Cell),
                grid.ColumnOf(a.Goal), grid.RowOf(a.Goal),
                Math.Round(world.HealthOf(a.Id), 3),
                world.RankOf(a.Id),
                a.Away ? grid.ColumnOf(a.Errand) : -1,
                a.Away ? grid.RowOf(a.Errand) : -1,
                a.Arrived, a.Thinking, a.Waiting, a.StalledTicks))
            .ToArray();

        writer.WriteLine(JsonSerializer.Serialize(
            new TickLine(
                tick, units, [.. world.Hostiles],
                anchor >= 0 ? grid.ColumnOf(anchor) : -1,
                anchor >= 0 ? grid.RowOf(anchor) : -1,
                note),
            Options));
    }

}
