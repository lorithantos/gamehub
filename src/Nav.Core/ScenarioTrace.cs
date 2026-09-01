using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nav.Core;

/// <summary>
/// A scenario run as JSONL: one self-contained line per tick, positions
/// included, so a run can be grepped, diffed, graphed and summarised without
/// re-simulating — and without hand-rolling a throwaway tracing console, which
/// is what answering "who planned and who waited" took before this existed.
/// </summary>
/// <remarks>
/// Line 1 is a header carrying a version number, the same discipline as the
/// scenario format: a format without a version drifts silently. Every following
/// line is one tick — state after that tick's orders were issued, before the
/// world advanced, which is the same instant the trajectory collision check
/// reads. Two runs of one scenario must produce byte-identical files; that makes
/// determinism checking a file diff, which also shows <em>where</em> divergence
/// began rather than just that it happened.
/// <para>
/// Lines deliberately carry full per-agent state rather than deltas. Tens of
/// agents over hundreds of ticks is a trivially small file, and self-contained
/// lines are what make grep useful. For reading a big trace,
/// <see cref="Summarize"/> exists precisely so nobody has to page a whole file
/// through a context window: it reduces any size of trace to a bounded digest
/// of aggregates and ticks worth looking at.
/// </para>
/// </remarks>
public static class ScenarioTrace
{
    /// <summary>
    /// The format stamp written into the header line's <c>version</c> field.
    /// </summary>
    /// <remarks>
    /// <see cref="Summarize"/> refuses any other value outright rather than
    /// reading a trace it may not understand -- a digest computed from
    /// misinterpreted fields is worse than no digest, because it looks like an
    /// answer. A consumer that hits the mismatch should regenerate the trace with
    /// <see cref="Write"/>, not try to salvage the old file.
    /// </remarks>
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record HeaderLine(int Version, string Name, string Map, int Agents, int EndTick, double TickSeconds);

    private sealed record AgentLine(int Id, int X, int Y, int GoalX, int GoalY, bool Thinking, int Stall, bool Arrived);

    private sealed record TickLine(
        int Tick,
        IReadOnlyList<AgentLine> Agents,
        int Nodes,
        int Started,
        int Finished,
        int Abandoned,
        int Queued);

    /// <summary>
    /// Plays the scenario and writes the trace. Returns the same outcome
    /// <see cref="ScenarioPlayback.Play"/> would, so tracing a run and checking
    /// it are one simulation, not two that might disagree.
    /// </summary>
    public static ScenarioOutcome Write(
        RecordedScenario scenario, Grid grid, TextWriter writer, string name = "(unnamed)", int horizon = 32)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine(JsonSerializer.Serialize(
            new HeaderLine(Version, name, scenario.MapName, scenario.Agents.Count, scenario.EndTick, scenario.TickSeconds),
            Options));

        return ScenarioPlayback.Play(scenario, grid, horizon, tick =>
        {
            var agents = tick.Agents
                .Select(a => new AgentLine(
                    a.Id,
                    grid.ColumnOf(a.Cell), grid.RowOf(a.Cell),
                    grid.ColumnOf(a.Goal), grid.RowOf(a.Goal),
                    a.Thinking, a.StalledTicks, a.Arrived))
                .ToArray();

            writer.WriteLine(JsonSerializer.Serialize(
                new TickLine(
                    tick.Tick, agents,
                    tick.Report.NodesSpent, tick.Report.SearchesStarted, tick.Report.SearchesFinished,
                    tick.Report.SearchesAbandoned, tick.Report.Queued),
                Options));
        });
    }

    /// <summary>
    /// Reduces a trace of any size to a bounded digest: aggregates, capped
    /// per-agent findings, and a "look at" list of the ticks where something
    /// happened. This is the tool side of the format — a 500-tick, 200-agent
    /// run comes back as a few dozen lines, never as the file.
    /// </summary>
    public static string Summarize(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var header = reader.ReadLine()
            ?? throw new InvalidDataException("the trace is empty; expected a header line.");

        using var headerDoc = JsonDocument.Parse(header);
        var head = headerDoc.RootElement;
        var version = head.GetProperty("version").GetInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"trace version {version} is not the supported version {Version}.");
        }

        var agentCount = head.GetProperty("agents").GetInt32();

        // Per-agent aggregates, fixed-size regardless of trace length.
        var firstStall = new int[agentCount];
        var maxStall = new int[agentCount];
        var firstArrival = new int[agentCount];
        var firstMove = new int[agentCount];
        var goalChanges = new List<(int Tick, int Agent)>();
        var (previousX, previousY) = (new int[agentCount], new int[agentCount]);
        var (previousGx, previousGy) = (new int[agentCount], new int[agentCount]);
        Array.Fill(firstStall, -1);
        Array.Fill(firstArrival, -1);
        Array.Fill(firstMove, -1);

        var ticks = 0;
        long totalNodes = 0;
        var maxNodes = 0;
        var busiestTick = 0;
        var abandonedTotal = 0;
        var abandonedTicks = new List<int>();
        var peakQueued = 0;
        var nodesPerTick = new List<int>();
        var finalArrived = 0;
        var finalStalled = 0;

        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var tick = root.GetProperty("tick").GetInt32();
            var nodes = root.GetProperty("nodes").GetInt32();
            var abandoned = root.GetProperty("abandoned").GetInt32();

            totalNodes += nodes;
            nodesPerTick.Add(nodes);
            if (nodes > maxNodes)
            {
                maxNodes = nodes;
                busiestTick = tick;
            }

            if (abandoned > 0)
            {
                abandonedTotal += abandoned;
                abandonedTicks.Add(tick);
            }

            peakQueued = Math.Max(peakQueued, root.GetProperty("queued").GetInt32());

            finalArrived = 0;
            finalStalled = 0;
            foreach (var agent in root.GetProperty("agents").EnumerateArray())
            {
                var id = agent.GetProperty("id").GetInt32();
                var x = agent.GetProperty("x").GetInt32();
                var y = agent.GetProperty("y").GetInt32();
                var gx = agent.GetProperty("goalX").GetInt32();
                var gy = agent.GetProperty("goalY").GetInt32();
                var stall = agent.GetProperty("stall").GetInt32();
                var arrived = agent.GetProperty("arrived").GetBoolean();

                if (arrived)
                {
                    finalArrived++;
                    if (firstArrival[id] < 0)
                    {
                        firstArrival[id] = tick;
                    }
                }
                else if (stall > 0)
                {
                    finalStalled++;
                }

                if (stall > 0 && firstStall[id] < 0)
                {
                    firstStall[id] = tick;
                }

                maxStall[id] = Math.Max(maxStall[id], stall);

                if (tick > 0 && (x != previousX[id] || y != previousY[id]) && firstMove[id] < 0)
                {
                    firstMove[id] = tick;
                }

                if (tick > 0 && (gx != previousGx[id] || gy != previousGy[id]))
                {
                    goalChanges.Add((tick, id));
                }
                else if (tick == 0 && (gx != x || gy != y))
                {
                    goalChanges.Add((0, id));
                }

                (previousX[id], previousY[id]) = (x, y);
                (previousGx[id], previousGy[id]) = (gx, gy);
            }

            ticks++;
        }

        return Compose(
            head, agentCount, ticks, totalNodes, nodesPerTick, maxNodes, busiestTick,
            abandonedTotal, abandonedTicks, peakQueued, finalArrived, finalStalled,
            firstStall, maxStall, firstArrival, firstMove, goalChanges);
    }

    private static string Compose(
        JsonElement head, int agentCount, int ticks, long totalNodes, List<int> nodesPerTick,
        int maxNodes, int busiestTick, int abandonedTotal, List<int> abandonedTicks, int peakQueued,
        int finalArrived, int finalStalled, int[] firstStall, int[] maxStall, int[] firstArrival,
        int[] firstMove, List<(int Tick, int Agent)> goalChanges)
    {
        var text = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;

        text.AppendLine(invariant,
            $"trace: {head.GetProperty("name").GetString()} on {head.GetProperty("map").GetString()}  " +
            $"{agentCount} agents  {ticks} ticks");

        var sorted = nodesPerTick.Order().ToArray();
        var p50 = sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
        var p99 = sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))];
        text.AppendLine(invariant,
            $"nodes: total {totalNodes:N0}  per-tick p50 {p50:N0}  p99 {p99:N0}  " +
            $"max {maxNodes:N0} (tick {busiestTick})  peak queued {peakQueued}");

        var arrivedIds = Enumerable.Range(0, agentCount).Where(id => firstArrival[id] >= 0).ToArray();
        var arrivalSpread = arrivedIds.Length == 0
            ? ""
            : FormattableString.Invariant(
                $"  (first tick {arrivedIds.Min(id => firstArrival[id])}, last tick {arrivedIds.Max(id => firstArrival[id])})");
        text.AppendLine(invariant, $"arrived: {finalArrived} of {agentCount} at the end{arrivalSpread}");

        var everStalled = Enumerable.Range(0, agentCount).Where(id => firstStall[id] >= 0).ToArray();
        var worstStalls = everStalled.Length == 0
            ? ""
            : "  worst: " + string.Join(", ", everStalled
                .OrderByDescending(id => maxStall[id]).Take(3)
                .Select(id => FormattableString.Invariant(
                    $"agent {id} (stall {maxStall[id]}, from tick {firstStall[id]})")));
        text.AppendLine(invariant, $"stalled: {finalStalled} at the end, {everStalled.Length} ever{worstStalls}");

        var neverMoved = Enumerable.Range(0, agentCount).Where(id => firstMove[id] < 0).ToArray();
        if (neverMoved.Length > 0)
        {
            text.AppendLine(invariant,
                $"never moved: {neverMoved.Length} of {agentCount}  " +
                $"({string.Join(",", neverMoved.Take(8))}{(neverMoved.Length > 8 ? ",…" : "")})");
        }

        if (abandonedTotal > 0)
        {
            text.AppendLine(invariant,
                $"abandoned searches: {abandonedTotal} over {abandonedTicks.Count} ticks " +
                $"(first tick {abandonedTicks[0]}, last tick {abandonedTicks[^1]})");
        }

        var orderTicks = goalChanges.GroupBy(c => c.Tick).OrderBy(g => g.Key).ToArray();
        if (orderTicks.Length > 0)
        {
            text.AppendLine(
                "orders (goal changes): " + string.Join("  ", orderTicks.Take(6)
                    .Select(g => FormattableString.Invariant($"tick {g.Key}: {g.Count()} agents"))) +
                (orderTicks.Length > 6 ? "  …" : ""));
        }

        // The ticks worth opening the file at, deduplicated and capped.
        var lookAt = new SortedDictionary<int, List<string>>();
        void Note(int tick, string what)
        {
            if (!lookAt.TryGetValue(tick, out var notes))
            {
                lookAt[tick] = notes = [];
            }

            notes.Add(what);
        }

        if (nodesPerTick.Count > 0)
        {
            Note(busiestTick, $"busiest ({maxNodes:N0} nodes)");
        }

        foreach (var id in everStalled.OrderBy(id => firstStall[id]).Take(3))
        {
            Note(firstStall[id], $"agent {id} first stalls");
        }

        if (arrivedIds.Length > 0)
        {
            Note(arrivedIds.Min(id => firstArrival[id]), "first arrival");
            Note(arrivedIds.Max(id => firstArrival[id]), "last arrival");
        }

        foreach (var tick in abandonedTicks.Take(2))
        {
            Note(tick, "abandoned search");
        }

        text.AppendLine("look at: " + string.Join("  ", lookAt.Take(10)
            .Select(entry => $"tick {entry.Key} ({string.Join("; ", entry.Value)})")));

        return text.ToString();
    }
}
