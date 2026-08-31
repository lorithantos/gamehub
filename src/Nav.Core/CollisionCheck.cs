namespace Nav.Core;

public enum ConflictKind
{
    /// <summary>Two agents on one cell at one tick.</summary>
    Vertex,

    /// <summary>Two agents exchanging cells across one tick, passing through each other.</summary>
    Edge,
}

/// <param name="Tick">The tick the conflict begins at. For an edge conflict, the tick the move starts.</param>
/// <param name="Cell">The cell <paramref name="AgentA"/> is involved with.</param>
/// <param name="OtherCell">For an edge conflict, the cell they exchange with. Equal to <paramref name="Cell"/> for a vertex conflict.</param>
public readonly record struct Conflict(
    ConflictKind Kind,
    int Tick,
    int AgentA,
    int AgentB,
    int Cell,
    int OtherCell);

/// <param name="AgentTicksChecked">
/// How much was actually looked at. A clean report over nothing is not evidence,
/// so the count travels with the verdict.
/// </param>
public sealed record ConflictReport(IReadOnlyList<Conflict> Conflicts, int AgentTicksChecked)
{
    public bool Clean => Conflicts.Count == 0;

    public int CountOf(ConflictKind kind) => Conflicts.Count(c => c.Kind == kind);
}

public readonly record struct AgentPlan(int Agent, PlanResult Plan);

/// <summary>
/// Checks a set of plans for the two ways agents can occupy the same space.
/// </summary>
/// <remarks>
/// This is the acceptance criteria made mechanical. Milestone 1 could compare
/// against 367,010 published optimal costs; there is no equivalent published truth
/// for multi-agent plans, so correctness here rests on properties that are exactly
/// decidable instead — and on this checking them over every agent and every tick
/// rather than spot-checking.
/// <para>
/// Both kinds matter and the second is the one that gets forgotten. Two agents
/// exchanging places share no cell at either tick, so a checker that only looks at
/// occupancy reports it clean while the units walk through each other.
/// </para>
/// </remarks>
public static class CollisionCheck
{
    public static ConflictReport Inspect(IReadOnlyList<AgentPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var live = plans.Where(p => !p.Plan.IsStuck).ToArray();
        if (live.Length == 0)
        {
            return new ConflictReport([], 0);
        }

        var firstTick = live.Min(p => p.Plan.StartTick);
        var lastTick = live.Max(p => p.Plan.LastTick);

        var conflicts = new List<Conflict>();
        var agentTicks = 0;

        // Occupancy for the tick being examined and the one after it, so an edge
        // conflict can be answered without rebuilding either.
        var here = new Dictionary<int, int>();
        var next = new Dictionary<int, int>();

        Occupancy(live, firstTick, here, ref agentTicks, conflicts);

        for (var tick = firstTick; tick <= lastTick; tick++)
        {
            next.Clear();
            if (tick < lastTick)
            {
                Occupancy(live, tick + 1, next, ref agentTicks, conflicts);
            }

            foreach (var (agent, plan) in live)
            {
                var from = plan.CellAt(tick);
                var to = plan.CellAt(tick + 1);
                if (from < 0 || to < 0 || from == to)
                {
                    continue;
                }

                // Somebody else is standing where this agent is going, and they
                // are heading for the cell it is leaving.
                // agent < mover so the pair is reported once. Both sides see the
                // same exchange, and a swap is one conflict rather than two.
                if (next.TryGetValue(from, out var mover) &&
                    agent < mover &&
                    here.TryGetValue(to, out var wasThere) &&
                    wasThere == mover)
                {
                    conflicts.Add(new Conflict(ConflictKind.Edge, tick, agent, mover, from, to));
                }
            }

            (here, next) = (next, here);
        }

        return new ConflictReport(conflicts, agentTicks);
    }

    private static void Occupancy(
        AgentPlan[] plans,
        int tick,
        Dictionary<int, int> into,
        ref int agentTicks,
        List<Conflict> conflicts)
    {
        into.Clear();

        foreach (var (agent, plan) in plans)
        {
            var cell = plan.CellAt(tick);
            if (cell < 0)
            {
                continue;
            }

            agentTicks++;

            if (into.TryGetValue(cell, out var other))
            {
                conflicts.Add(new Conflict(ConflictKind.Vertex, tick, other, agent, cell, cell));
                continue;
            }

            into[cell] = agent;
        }
    }
}
