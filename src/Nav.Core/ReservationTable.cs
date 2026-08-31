namespace Nav.Core;

/// <summary>
/// Who intends to be where, and when.
/// </summary>
/// <remarks>
/// The difference between this and a set of occupied cells is the time axis:
/// reservations are keyed by <c>(cell, tick)</c>, so two agents crossing the same
/// cell at different moments do not conflict, and two agents crossing it at the
/// same moment do.
/// <para>
/// Bounded by a horizon rather than open-ended. Planning to the end of time costs
/// what the end of time costs; a window makes planning bounded and lets agents
/// replan as they go. Beyond the window nothing is reserved, so a query about it
/// reports free -- that is not an approximation, it is what "we have not planned
/// that far" means.
/// </para>
/// <para>
/// Backed by a ring of <see cref="Horizon"/> dense arrays, indexed
/// <c>tick % Horizon</c>. <see cref="Advance"/> clears the slot falling off the
/// back, which is the only work here proportional to the grid, and it happens once
/// per tick rather than once per search.
/// </para>
/// </remarks>
public sealed class ReservationTable
{
    /// <summary>No agent. Agent ids are non-negative, so this cannot collide with one.</summary>
    private const int Free = -1;

    private readonly record struct Reservation(int Tick, int Cell);

    private readonly int[][] _ring;
    private readonly int _cellCount;
    private readonly Dictionary<int, List<Reservation>> _byAgent = [];

    /// <param name="cellCount">Cells in the grid these reservations refer to.</param>
    /// <param name="horizon">
    /// How many ticks ahead are tracked. At least 2, because a swap is a question
    /// about two consecutive ticks and one tick of lookahead cannot answer it.
    /// </param>
    public ReservationTable(int cellCount, int horizon)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(horizon, 2);

        _cellCount = cellCount;
        Horizon = horizon;

        _ring = new int[horizon][];
        for (var slot = 0; slot < horizon; slot++)
        {
            _ring[slot] = new int[cellCount];
            Array.Fill(_ring[slot], Free);
        }
    }

    public int Horizon { get; }

    /// <summary>The earliest tick still tracked. The window is <c>[CurrentTick, CurrentTick + Horizon)</c>.</summary>
    public int CurrentTick { get; private set; }

    /// <summary>
    /// True if <paramref name="agent"/> may occupy <paramref name="cell"/> at
    /// <paramref name="tick"/> -- either nobody holds it, or this agent already
    /// does.
    /// </summary>
    /// <remarks>
    /// An agent never conflicts with itself. Replanning would otherwise be blocked
    /// by the plan it is replacing.
    /// </remarks>
    public bool IsFree(int cell, int tick, int agent)
    {
        var holder = Occupant(cell, tick);
        return holder == Free || holder == agent;
    }

    /// <summary>
    /// True if moving <paramref name="from"/> to <paramref name="to"/> across the
    /// tick beginning at <paramref name="tick"/> would pass through another agent
    /// coming the other way.
    /// </summary>
    /// <remarks>
    /// THE EDGE COLLISION, and the one a cell-occupancy check cannot see. Two
    /// agents exchanging places share no cell at either tick -- A is here then
    /// there, B is there then here -- and they walk through each other. A suite
    /// that checks only occupancy reports it as clean.
    /// </remarks>
    public bool IsSwap(int from, int to, int tick, int agent)
    {
        var other = Occupant(to, tick);
        if (other == Free || other == agent)
        {
            return false;
        }

        return Occupant(from, tick + 1) == other;
    }

    /// <summary>The agent holding a cell at a tick, or -1. Beyond the horizon, -1.</summary>
    public int HolderOf(int cell, int tick) => Occupant(cell, tick);

    /// <summary>
    /// Records <paramref name="agent"/>'s plan, replacing whatever it held before.
    /// </summary>
    /// <remarks>
    /// The final cell is held for the remainder of the window, and that single rule
    /// covers both cases correctly. An agent that ARRIVED stays put and must keep
    /// reserving its goal, or it becomes invisible and others plan straight through
    /// it -- the most common way a reservation table is quietly wrong. An agent
    /// whose plan merely reached the window edge is already at that cell at the
    /// edge, so the extra hold is an empty range.
    /// </remarks>
    public void Reserve(IReadOnlyList<int> path, int startTick, int agent)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(agent);

        if (startTick < CurrentTick)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startTick), startTick, $"The window begins at tick {CurrentTick}.");
        }

        Release(agent);

        if (path.Count == 0)
        {
            return;
        }

        var held = _byAgent.TryGetValue(agent, out var existing) ? existing : _byAgent[agent] = [];

        var last = CurrentTick + Horizon - 1;
        for (var i = 0; i < path.Count; i++)
        {
            var tick = startTick + i;
            if (tick > last)
            {
                break;
            }

            Mark(path[i], tick, agent, held);
        }

        for (var tick = startTick + path.Count; tick <= last; tick++)
        {
            Mark(path[^1], tick, agent, held);
        }
    }

    /// <summary>Drops everything <paramref name="agent"/> holds.</summary>
    public void Release(int agent)
    {
        if (!_byAgent.TryGetValue(agent, out var held))
        {
            return;
        }

        foreach (var reservation in held)
        {
            // Two guards, and the second is the load-bearing one. A reservation
            // whose tick has fallen out of the window shares a ring slot with a
            // tick one horizon later, which another agent may now hold. Clearing
            // it by position alone would delete somebody else's reservation.
            if (reservation.Tick < CurrentTick || reservation.Tick >= CurrentTick + Horizon)
            {
                continue;
            }

            ref var slot = ref _ring[reservation.Tick % Horizon][reservation.Cell];
            if (slot == agent)
            {
                slot = Free;
            }
        }

        held.Clear();
    }

    /// <summary>
    /// Moves the window forward, forgetting the ticks that fall off the back.
    /// </summary>
    public void Advance(int ticks = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        if (ticks >= Horizon)
        {
            // Everything tracked is now in the past.
            foreach (var slot in _ring)
            {
                Array.Fill(slot, Free);
            }

            CurrentTick += ticks;
            return;
        }

        for (var i = 0; i < ticks; i++)
        {
            Array.Fill(_ring[CurrentTick % Horizon], Free);
            CurrentTick++;
        }
    }

    private int Occupant(int cell, int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cell, _cellCount);

        if (tick < CurrentTick)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tick), tick, $"Tick {tick} is behind the window, which begins at {CurrentTick}.");
        }

        // Beyond the horizon nothing has been planned, so nothing is in the way.
        return tick >= CurrentTick + Horizon ? Free : _ring[tick % Horizon][cell];
    }

    private void Mark(int cell, int tick, int agent, List<Reservation> held)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cell, _cellCount);

        _ring[tick % Horizon][cell] = agent;
        held.Add(new Reservation(tick, cell));
    }
}
