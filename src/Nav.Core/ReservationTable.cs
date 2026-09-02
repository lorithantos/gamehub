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
internal sealed class ReservationTable : IReservationView
{
    /// <summary>No agent. Agent ids are non-negative, so this cannot collide with one.</summary>
    private const int Free = -1;

    private readonly record struct Reservation(int Tick, int Cell);

    private readonly int[][] _ring;
    private readonly int _cellCount;
    private readonly Dictionary<int, List<Reservation>> _byAgent = [];

    /// <summary>
    /// Where each agent's plan ENDS, and from when. Parking is indefinite.
    /// </summary>
    /// <remarks>
    /// Kept as a fact rather than written into the ring, and the difference is not
    /// cosmetic. Materialising "I am staying here" as ticks up to the window edge
    /// records it as ending at whatever the edge happened to be WHEN THE
    /// RESERVATION WAS MADE. One tick later the window has moved and its last tick
    /// reads free, so another agent plans to arrive exactly there — and walks into
    /// a unit that was never going to leave.
    /// <para>
    /// Measured, not theorised: two agents in a one-wide corridor stood off for
    /// thirty-one ticks and then passed through each other on tick 32, with a
    /// horizon of 32.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// A LIST per cell, not one entry. One entry was sound while every park came
    /// from a search, which ends nowhere it cannot hold for the whole window.
    /// Followers and stale steps park where they STAND, and a unit can stand,
    /// legitimately and briefly, on a cell a fellow's plan will end on later;
    /// with one entry the second park replaced the first, and when the
    /// transient unit moved on and released its own, the fellow's park was gone
    /// -- a third plan then validated against a cell nobody appeared to own,
    /// and two units stood on it for good. The one standing there is the entry
    /// with the earliest tick that has already come.
    /// </remarks>
    private readonly Dictionary<int, List<(int Agent, int FromTick)>> _parked = [];

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

    /// <inheritdoc/>
    public int Horizon { get; }

    /// <inheritdoc/>
    public int CurrentTick { get; private set; }

    /// <inheritdoc/>
    public bool IsFree(int cell, int tick, int agent)
    {
        var holder = Occupant(cell, tick);
        return holder == Free || holder == agent;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The second question -- is the other one coming HERE next tick -- is asked
    /// of the ring alone, because the asker is standing on <paramref name="from"/>
    /// and, with standing taking precedence over planning, its own park would
    /// answer for the cell and hide the other's intention. That is exactly how
    /// two followers swapped through each other the first time this ran.
    /// </remarks>
    public bool IsSwap(int from, int to, int tick, int agent)
    {
        var other = Occupant(to, tick);
        if (other == Free || other == agent)
        {
            return false;
        }

        return Planned(from, tick + 1) == other;
    }

    /// <summary>Who the ring says will be on a cell at a tick -- intention only, no parks.</summary>
    private int Planned(int cell, int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cell, _cellCount);

        if (tick < CurrentTick || tick >= CurrentTick + Horizon)
        {
            return Free;
        }

        return _ring[tick % Horizon][cell];
    }

    /// <summary>The agent holding a cell at a tick, or -1. Beyond the horizon, -1.</summary>
    public int HolderOf(int cell, int tick) => Occupant(cell, tick);

    /// <inheritdoc/>
    public bool IsHoldable(int cell, int fromTick, int agent)
    {
        if (WillBeParkedOn(cell, agent))
        {
            return false;
        }

        var last = CurrentTick + Horizon - 1;
        for (var tick = Math.Max(fromTick, CurrentTick); tick <= last; tick++)
        {
            if (!IsFree(cell, tick, agent))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Has somebody else already claimed to END on this cell -- at any tick, even
    /// one past the window? A park is a claim on the cell, not just on a tick.
    /// </summary>
    /// <remarks>
    /// One park per cell is all the table keeps, and that was sound while every
    /// park came from a search, because a search checks holdability over the
    /// whole window before ending anywhere. A follower checks only the next
    /// tick. On the arena one stepped through a cell a fellow's plan was going to
    /// end on twelve ticks later, parked there for a tick, and on leaving took
    /// the fellow's park with it; a third unit's plan then validated against a
    /// cell nobody appeared to own, and two units stood on it for good. So a
    /// cell with anybody's park on it is nobody else's to enter or end on.
    /// </remarks>
    public bool WillBeParkedOn(int cell, int agent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cell, _cellCount);
        return _parked.TryGetValue(cell, out var parks) && parks.Any(p => p.Agent != agent);
    }

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

        // Where the plan stops, the agent stays — for good, not until the edge of
        // whatever window happens to be current. See the note on _parked.
        var end = path[^1];
        if (!_parked.TryGetValue(end, out var parks))
        {
            _parked[end] = parks = [];
        }

        parks.Add((agent, startTick + path.Count - 1));
    }

    /// <summary>
    /// Parks <paramref name="agent"/> on <paramref name="cell"/> from now, for
    /// good -- if it may stay. Refuses, and changes nothing, when anybody else's
    /// plan crosses the cell inside the window.
    /// </summary>
    /// <remarks>
    /// THE VALIDATED PARK. <see cref="Reserve"/> trusts its caller: every plan it
    /// records was checked cell by cell by the search that produced it, and the
    /// goal in particular passed <see cref="IsHoldable"/> before the search
    /// declared it found. A park that did not come from a search has had no such
    /// check, and writing it straight in is unsound -- it stands a unit on a cell
    /// somebody is already committed to walk through, and the collision arrives
    /// ticks later, far from its cause. That is exactly what happened when a
    /// doctrine first tried to say "stay where you are": five scenarios tripped
    /// the assertion in <see cref="Mark"/>. So this is the one way to park without
    /// a search, and it asks first.
    /// <para>
    /// On success the agent's previous route is released, so the cells it was
    /// going to walk are free for others at once. Releasing can never cause a
    /// collision; only marking can, and marking here is guarded.
    /// </para>
    /// </remarks>
    /// <returns>True if the agent is now parked there; false if it may not stay.</returns>
    public bool TryPark(int cell, int agent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(agent);

        if (!IsHoldable(cell, CurrentTick, agent))
        {
            return false;
        }

        Reserve([cell], CurrentTick, agent);
        return true;
    }

    /// <summary>Drops everything <paramref name="agent"/> holds.</summary>
    public void Release(int agent)
    {
        if (!_byAgent.TryGetValue(agent, out var held))
        {
            return;
        }

        foreach (var (cell, parks) in _parked.ToArray())
        {
            parks.RemoveAll(p => p.Agent == agent);
            if (parks.Count == 0)
            {
                _parked.Remove(cell);
            }
        }

        foreach (var reservation in held)
        {
            // Two guards, and the second is the one that prevents corruption. A
            // reservation whose tick has fallen out of the window shares a ring
            // slot with a tick one horizon later, which another agent may now
            // hold. Clearing it by position alone would delete somebody else's
            // reservation.
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
            // Everything in the ring is now in the past. Parked agents are not:
            // they are still standing there.
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

        // STANDING BEATS PLANNING. A parked agent is on the cell, or will be and
        // will not leave; a ring entry is somebody's intention to pass through.
        // When both exist the intention is stale -- a follower stopped here after
        // that plan was made -- and the plan's owner finds out when it tries to
        // take the step and does not hold the cell. Parking does not expire with
        // the window either, which is why it is tracked apart from the ring.
        if (_parked.TryGetValue(cell, out var parks))
        {
            var standing = Free;
            var since = int.MaxValue;
            foreach (var (holder, from) in parks)
            {
                if (from <= tick && from < since)
                {
                    standing = holder;
                    since = from;
                }
            }

            if (standing != Free)
            {
                return standing;
            }
        }

        if (tick < CurrentTick + Horizon)
        {
            return _ring[tick % Horizon][cell];
        }

        return Free;
    }

    private void Mark(int cell, int tick, int agent, List<Reservation> held)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cell);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cell, _cellCount);

        // OVERWRITES, and that is the contract now rather than a defect. Until
        // followers, every plan reaching here had been validated cell by cell by
        // the search that produced it, and a slot already held by somebody else
        // was an invariant broken -- this line threw, and caught two unsound
        // parks. Followers change the table every tick without a search, so a
        // committed plan can go stale honestly: a unit stopped on a cell the
        // plan was going to cross. The guarantee moved to where it belongs -- an
        // agent takes a step only if it holds the cell at that tick (see the
        // move in MovementSystem.Tick) -- and Occupant answers "who holds it"
        // with the standing unit first. A stale ring entry is then harmless: its
        // owner does not take the step, stands, and asks again.
        _ring[tick % Horizon][cell] = agent;
        held.Add(new Reservation(tick, cell));
    }
}
