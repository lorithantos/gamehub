using System.Globalization;
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
    public const int Version = 1;

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
        int Ticks);

    private sealed record UnitLine(int Id, int X, int Y, double Health, int ErrandX, int ErrandY, bool Arrived);

    private sealed record TickLine(
        int Tick,
        IReadOnlyList<UnitLine> Units,
        IReadOnlyList<int> Hostiles,
        int AnchorX,
        int AnchorY,
        string? Note);

    /// <summary>
    /// Writes the header. Walls travel as one string of '.' and '#' in row
    /// order, which is the map itself and reads as a picture in the file.
    /// </summary>
    public static void WriteHeader(
        TextWriter writer, string name, string description, Grid grid, IReadOnlyList<int> repairPoints, int ticks)
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
                [.. repairPoints], ticks),
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
                Math.Round(world.HealthOf(a.Id), 3),
                a.Away ? grid.ColumnOf(a.Errand) : -1,
                a.Away ? grid.RowOf(a.Errand) : -1,
                a.Arrived))
            .ToArray();

        writer.WriteLine(JsonSerializer.Serialize(
            new TickLine(
                tick, units, [.. world.Hostiles],
                anchor >= 0 ? grid.ColumnOf(anchor) : -1,
                anchor >= 0 ? grid.RowOf(anchor) : -1,
                note),
            Options));
    }

    /// <summary>
    /// A one-line summary of a finished demo, for a console: name, ticks, and
    /// how many units ended arrived and at what health.
    /// </summary>
    public static string Summarise(string name, int ticks, IReadOnlyList<AgentState> agents, IPerception world)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(world);

        var arrived = agents.Count(a => a.Arrived);
        var healthy = agents.Count(a => world.HealthOf(a.Id) >= 0.99);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name,-22} {ticks,5} ticks  {arrived}/{agents.Count} in place  {healthy}/{agents.Count} at full health");
    }
}
