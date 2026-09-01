namespace Nav.Core;

/// <summary>
/// Which of the two ways a pair of agents overlapped.
/// </summary>
/// <remarks>
/// These are the only kinds <see cref="CollisionCheck"/> reports, and
/// <see cref="Edge"/> is the one an occupancy-only check never sees -- see the
/// remarks on <see cref="CollisionCheck"/>.
/// </remarks>
public enum ConflictKind
{
    /// <summary>Two agents on one cell at one tick.</summary>
    Vertex,

    /// <summary>Two agents exchanging cells across one tick, passing through each other.</summary>
    Edge,
}

/// <param name="Kind">
/// Which overlap this is, and therefore how to read <paramref name="Cell"/> and
/// <paramref name="OtherCell"/>.
/// </param>
/// <param name="Tick">The tick the conflict begins at. For an edge conflict, the tick the move starts.</param>
/// <param name="AgentA">
/// For an edge conflict, the lower of the two agent ids. For a vertex conflict,
/// whichever of the two came first in the list handed to
/// <see cref="CollisionCheck.Inspect"/>.
/// </param>
/// <param name="AgentB">
/// The other agent. A colliding pair is reported once and once only -- there is
/// no mirrored <c>(B, A)</c> entry for the same tick.
/// </param>
/// <param name="Cell">The cell <paramref name="AgentA"/> is involved with.</param>
/// <param name="OtherCell">For an edge conflict, the cell they exchange with. Equal to <paramref name="Cell"/> for a vertex conflict.</param>
/// <remarks>
/// A conflict is always a pair, so three agents piled onto one cell come back as
/// two vertex conflicts, both naming the first of the three as
/// <paramref name="AgentA"/> -- not as one report of three.
/// </remarks>
public readonly record struct Conflict(
    ConflictKind Kind,
    int Tick,
    int AgentA,
    int AgentB,
    int Cell,
    int OtherCell);

/// <param name="Conflicts">
/// Every overlap found, in the tick order they were discovered. Empty is the
/// good case; <see cref="Clean"/> is the way to ask.
/// </param>
/// <param name="AgentTicksChecked">
/// How much was actually looked at. A clean report over nothing is not evidence,
/// so the count travels with the verdict.
/// </param>
public sealed record ConflictReport(IReadOnlyList<Conflict> Conflicts, int AgentTicksChecked)
{
    /// <summary>
    /// No conflicts of <em>either</em> kind. This is the gate the multi-agent
    /// tests assert on -- and it is true of a report that examined nothing, which
    /// is why <see cref="AgentTicksChecked"/> is read alongside it.
    /// </summary>
    public bool Clean => Conflicts.Count == 0;

    /// <summary>
    /// How many conflicts of one kind, so a test can say <em>which</em> kind it
    /// expected rather than only that something went wrong -- an edge conflict
    /// counted as a vertex one would otherwise pass.
    /// </summary>
    public int CountOf(ConflictKind kind) => Conflicts.Count(c => c.Kind == kind);
}

/// <summary>
/// One agent's id paired with the cells it occupies over time.
/// </summary>
/// <param name="Agent">
/// The id a <see cref="Conflict"/> names. It travels with the plan because
/// position in the list is not identity -- stuck agents are dropped before the
/// check runs.
/// </param>
/// <param name="Plan">
/// Cells against ticks. Read only through <see cref="PlanResult.CellAt"/>, so an
/// intended plan and an actual trajectory are interchangeable here; that is what
/// lets <see cref="ScenarioPlayback"/> check what happened rather than what was
/// meant.
/// </param>
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
