using System.Globalization;
using System.Text;

namespace Nav.Core;

/// <param name="Id">Agent ids are consecutive from zero; the id is also the planning order.</param>
/// <param name="X">
/// Starting column. The format stores x/y pairs because they are what a human
/// writes and reads; <see cref="Grid.Index"/> turns the pair into the flat cell
/// index the engine actually runs on.
/// </param>
/// <param name="Y">Starting row. Placed on a wall or off the map, the scenario is refused rather than clamped.</param>
public sealed record ScenarioAgent(int Id, int X, int Y);

/// <param name="Tick">When the order is issued.</param>
/// <param name="Agents">Everyone it applies to. A group order is one order, not several.</param>
/// <param name="X">
/// Destination column. Every agent named in <paramref name="Agents"/> is sent to
/// this <em>one</em> cell -- spreading them onto cells of their own is the
/// movement system's job, not something the format records.
/// </param>
/// <param name="Y">Destination row.</param>
public sealed record ScenarioOrder(int Tick, IReadOnlyList<int> Agents, int X, int Y);

/// <summary>
/// A map, some starting placements, and a timeline of orders -- this project's
/// own multi-agent replay format, not the Moving AI <c>.scen</c> benchmark
/// format that <see cref="ScenarioFile"/> reads.
/// </summary>
/// <remarks>
/// <b>Not a recording of where units went.</b> If the simulation is deterministic
/// — and it is required to be — then replaying the <em>inputs</em> reproduces the
/// run exactly, so this is kilobytes of orders rather than megabytes of positions.
/// It is the same reason a replay file for a real-time strategy game is tiny.
/// <para>
/// The useful consequence is that playback is the determinism test. A replay that
/// diverges between two plays has found a determinism bug, and nothing else finds
/// one as cheaply.
/// </para>
/// </remarks>
/// <param name="MapName">
/// The map file this was recorded against, as a bare name resolved relative to
/// the scenario. It is how a caller finds the map; it is <em>not</em> a check
/// that the map is the right one -- see <see cref="MapWidth"/>.
/// </param>
/// <param name="MapWidth">
/// Width of the map at recording time, in cells. Recorded so a replay can refuse
/// a map of the wrong shape instead of running plausibly against it; compared by
/// <see cref="EnsureMatches"/>.
/// </param>
/// <param name="MapHeight">Height of the map at recording time, in cells.</param>
/// <param name="TickSeconds">
/// Wall-clock seconds per simulated tick, which sets playback speed and nothing
/// else -- the simulation itself is driven by tick count, so this cannot change
/// what happens.
/// </param>
/// <param name="Agents">Starting placements, indexed by the id each one carries.</param>
/// <param name="Orders">
/// The order timeline in tick sequence. Replaying these inputs is what reproduces
/// the run, which is why this is a list of orders rather than of positions.
/// </param>
/// <param name="EndTick">
/// The last tick simulated, inclusive. A scenario that never ends cannot fail, so
/// the format requires it.
/// </param>
public sealed record RecordedScenario(
    string MapName,
    int MapWidth,
    int MapHeight,
    double TickSeconds,
    IReadOnlyList<ScenarioAgent> Agents,
    IReadOnlyList<ScenarioOrder> Orders,
    int EndTick)
{
    private const double DefaultTickSeconds = 1.0 / 60.0;

    /// <summary>
    /// Throws unless <paramref name="grid"/> is the size this scenario was
    /// recorded against.
    /// </summary>
    /// <remarks>
    /// The replay twin of <see cref="ScenarioRecord.EnsureMatches"/>, and it
    /// exists for the same reason: replaying against a map that is merely the
    /// wrong shape produces a run that is plausible and meaningless, every
    /// position slightly off and nothing obviously broken. Refusing is the only
    /// useful behaviour.
    /// <para>
    /// <b>Dimensions only.</b> This says the map is the right <em>size</em>; it
    /// cannot say it is the right map. A differently walled map of identical
    /// width and height passes here and is not detectable from anything the
    /// format records -- closing that would mean a content fingerprint, which
    /// would also make these files impossible to write by hand.
    /// <see cref="ScenarioPlayback.Play"/> still checks every coordinate against
    /// the grid, which is what catches the rest.
    /// </para>
    /// </remarks>
    /// <param name="grid">The map the scenario is about to be replayed on.</param>
    /// <param name="source">
    /// The scenario's path, when there is one, so the refusal names the file
    /// rather than only the mismatch.
    /// </param>
    /// <exception cref="MapFormatException"><paramref name="grid"/> is a different size.</exception>
    public void EnsureMatches(Grid grid, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(grid);

        if (grid.Width != MapWidth || grid.Height != MapHeight)
        {
            throw new MapFormatException(
                source,
                1,
                $"the scenario expects a {MapWidth}x{MapHeight} map but '{MapName}' loaded as {grid.Width}x{grid.Height}");
        }
    }

    /// <summary>
    /// Reads a scenario off disk. <paramref name="path"/> travels into any
    /// <see cref="MapFormatException"/>, so a bad line is reported as file
    /// <em>and</em> line number -- which is the difference between a fixable
    /// failure and one you have to go hunting for.
    /// </summary>
    /// <param name="path">The scenario file to read.</param>
    /// <exception cref="MapFormatException">Any line does not parse; the message names the file and the line.</exception>
    public static RecordedScenario FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>
    /// Reads a scenario already held as text -- the form tests and fixtures use.
    /// Identical parsing to <see cref="FromFile"/>, except that errors carry only
    /// a line number, since there is no file to name.
    /// </summary>
    /// <param name="text">The scenario text.</param>
    /// <exception cref="MapFormatException">Any line does not parse.</exception>
    public static RecordedScenario FromText(string text) => Parse(text, source: null);

    /// <summary>
    /// Writes the scenario back out. Exists so a round trip is a string
    /// comparison, and so a generated scenario is diffable against a hand-written
    /// one.
    /// </summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        builder.Append("version 1\n");
        builder.Append(CultureInfo.InvariantCulture, $"map {MapName}\n");
        builder.Append(CultureInfo.InvariantCulture, $"size {MapWidth} {MapHeight}\n");
        builder.Append(CultureInfo.InvariantCulture, $"tick {TickSeconds.ToString("0.#########", CultureInfo.InvariantCulture)}\n");

        foreach (var agent in Agents)
        {
            builder.Append(CultureInfo.InvariantCulture, $"agent {agent.Id} {agent.X} {agent.Y}\n");
        }

        foreach (var order in Orders)
        {
            builder.Append(CultureInfo.InvariantCulture, $"order {order.Tick} {string.Join(',', order.Agents)} {order.X} {order.Y}\n");
        }

        builder.Append(CultureInfo.InvariantCulture, $"end {EndTick}\n");
        return builder.ToString();
    }

    /// <remarks>
    /// Strict throughout, in the same spirit as the map and scenario readers: an
    /// unparseable line is an error naming its number, never a skip. A scenario
    /// that quietly drops an order is a scenario that silently stops testing what
    /// it was written to test.
    /// </remarks>
    private static RecordedScenario Parse(string text, string? source)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Split('\n');
        string? mapName = null;
        (int Width, int Height)? size = null;
        var tickSeconds = DefaultTickSeconds;
        var agents = new List<ScenarioAgent>();
        var orders = new List<ScenarioOrder>();
        int? endTick = null;
        var seenVersion = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].TrimEnd('\r').Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (!seenVersion)
            {
                if (parts is not ["version", "1"])
                {
                    throw new MapFormatException(source, lineNumber, $"expected 'version 1', found '{line}'");
                }

                seenVersion = true;
                continue;
            }

            switch (parts[0])
            {
                case "map" when parts.Length == 2:
                    mapName = parts[1];
                    break;

                case "size" when parts.Length == 3:
                {
                    var width = RequireInt(parts[1], "width", source, lineNumber);
                    var height = RequireInt(parts[2], "height", source, lineNumber);
                    if (width <= 0 || height <= 0)
                    {
                        throw new MapFormatException(
                            source, lineNumber, $"'size' must be positive, found {width}x{height}");
                    }

                    size = (width, height);
                    break;
                }

                case "tick" when parts.Length == 2:
                    tickSeconds = RequireDouble(parts[1], "tick", source, lineNumber);
                    break;

                case "agent" when parts.Length == 4:
                {
                    var id = RequireInt(parts[1], "agent id", source, lineNumber);
                    if (id != agents.Count)
                    {
                        throw new MapFormatException(
                            source, lineNumber, $"agent ids must run consecutively from 0; expected {agents.Count}, found {id}");
                    }

                    agents.Add(new ScenarioAgent(
                        id,
                        RequireInt(parts[2], "x", source, lineNumber),
                        RequireInt(parts[3], "y", source, lineNumber)));
                    break;
                }

                case "order" when parts.Length == 5:
                {
                    var tick = RequireInt(parts[1], "order tick", source, lineNumber);
                    if (orders.Count > 0 && tick < orders[^1].Tick)
                    {
                        throw new MapFormatException(
                            source, lineNumber, $"orders must be in tick sequence; {tick} follows {orders[^1].Tick}");
                    }

                    var ids = parts[2]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(id => RequireInt(id, "agent id", source, lineNumber))
                        .ToArray();

                    if (ids.Length == 0)
                    {
                        throw new MapFormatException(source, lineNumber, "an order must name at least one agent");
                    }

                    foreach (var id in ids)
                    {
                        if (id < 0 || id >= agents.Count)
                        {
                            throw new MapFormatException(source, lineNumber, $"order names unknown agent {id}");
                        }
                    }

                    orders.Add(new ScenarioOrder(
                        tick,
                        ids,
                        RequireInt(parts[3], "x", source, lineNumber),
                        RequireInt(parts[4], "y", source, lineNumber)));
                    break;
                }

                case "end" when parts.Length == 2:
                    endTick = RequireInt(parts[1], "end", source, lineNumber);
                    break;

                default:
                    throw new MapFormatException(source, lineNumber, $"unrecognised line '{line}'");
            }
        }

        if (!seenVersion)
        {
            throw new MapFormatException(source, 1, "the scenario is empty");
        }

        if (mapName is null)
        {
            throw new MapFormatException(source, lines.Length, "no 'map' line");
        }

        // Required, not optional. An optional dimension line would be absent from
        // exactly the scenarios nobody thought about, which are the ones that go
        // wrong -- and a check that silently does not run is worse than no check,
        // because the absence looks like a pass.
        if (size is not { } mapSize)
        {
            throw new MapFormatException(source, lines.Length, "no 'size' line");
        }

        if (agents.Count == 0)
        {
            throw new MapFormatException(source, lines.Length, "no agents");
        }

        // A scenario that never ends cannot fail.
        if (endTick is not { } end)
        {
            throw new MapFormatException(source, lines.Length, "no 'end' line");
        }

        if (end < 0)
        {
            throw new MapFormatException(source, lines.Length, $"'end' must not be negative, found {end}");
        }

        return new RecordedScenario(mapName, mapSize.Width, mapSize.Height, tickSeconds, agents, orders, end);
    }

    private static int RequireInt(string value, string field, string? source, int lineNumber)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new MapFormatException(source, lineNumber, $"'{field}' is not an integer: '{value}'");
        }

        return parsed;
    }

    private static double RequireDouble(string value, string field, string? source, int lineNumber)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new MapFormatException(source, lineNumber, $"'{field}' must be a positive number: '{value}'");
        }

        return parsed;
    }
}
