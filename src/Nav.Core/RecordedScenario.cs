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
public sealed record RecordedScenario(
    string MapName,
    double TickSeconds,
    IReadOnlyList<ScenarioAgent> Agents,
    IReadOnlyList<ScenarioOrder> Orders,
    int EndTick)
{
    private const double DefaultTickSeconds = 1.0 / 60.0;

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

        return new RecordedScenario(mapName, tickSeconds, agents, orders, end);
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
