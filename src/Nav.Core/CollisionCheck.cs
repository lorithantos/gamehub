namespace Nav.Core;

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
    /// <summary>
    /// Examines every tick the plans span and reports each colliding pair once.
    /// </summary>
    /// <remarks>
    /// Agents whose plan is <see cref="PlanResult.IsStuck"/> are dropped first --
    /// they have no cells to be anywhere in -- and are not counted in
    /// <see cref="ConflictReport.AgentTicksChecked"/>. An agent whose plan has
    /// merely <em>ended</em> is not dropped: it goes on occupying its last cell,
    /// so a unit parked on its goal keeps colliding with anyone who walks into it.
    /// <para>
    /// Ticks are swept once, carrying the current and next occupancy maps forward,
    /// so the cost is linear in agent-ticks rather than quadratic in agents.
    /// </para>
    /// </remarks>
    /// <param name="plans">
    /// The agents to check. Their order does not change the verdict, only which
    /// member of a vertex pair is reported as <see cref="Conflict.AgentA"/>.
    /// </param>
    /// <returns>
    /// The conflicts found, together with how many agent-ticks were examined --
    /// enough to tell a genuinely clean run from one that checked nothing.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="plans"/> is null.</exception>
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

        // Allocated only once some cell actually holds a third agent, which on a
        // run that passes never happens at all.
        Dictionary<int, List<int>>? crowded = null;

        foreach (var (agent, plan) in plans)
        {
            var cell = plan.CellAt(tick);
            if (cell < 0)
            {
                continue;
            }

            agentTicks++;

            if (!into.TryGetValue(cell, out var first))
            {
                into[cell] = agent;
                continue;
            }

            // Pair the arrival with EVERYONE already standing here, not just the
            // first one seen. Three agents on one cell is three colliding pairs;
            // reporting two of them would make CountOf a count of reports rather
            // than of collisions, and a test asserting the exact number would be
            // asserting the wrong one. `into` deliberately keeps the first
            // occupant, because that is the one the edge check reads.
            crowded ??= [];
            if (!crowded.TryGetValue(cell, out var standing))
            {
                standing = [first];
                crowded[cell] = standing;
            }

            foreach (var other in standing)
            {
                conflicts.Add(Vertex(tick, other, agent, cell));
            }

            standing.Add(agent);
        }
    }

    /// <summary>
    /// A vertex conflict with its pair in ascending id order, so
    /// <see cref="Conflict.AgentA"/> means the same thing here as it does for an
    /// edge conflict.
    /// </summary>
    /// <remarks>
    /// Without this the vertex pair came out in <em>plan list</em> order, so the
    /// same collision reported different ids depending on how the caller happened
    /// to sort its agents -- and the edge path, which has always ordered its pair,
    /// quietly disagreed with it.
    /// </remarks>
    private static Conflict Vertex(int tick, int one, int another, int cell) =>
        new(ConflictKind.Vertex, tick, Math.Min(one, another), Math.Max(one, another), cell, cell);
}
