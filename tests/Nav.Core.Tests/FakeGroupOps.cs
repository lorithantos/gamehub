namespace Nav.Core.Tests;

/// <summary>
/// An <see cref="IGroupOps"/> a test builds by hand, and which remembers what a
/// doctrine did to it.
/// </summary>
/// <remarks>
/// The doctrine layer could not be tested at all until the seam became an
/// interface. The obstacle was never the <c>private</c> on the passes -- it was
/// that their parameter was a concrete class with an internal constructor
/// wanting a live <see cref="MovementSystem"/> and a private nested Group, so
/// there was nothing to hand them. Every rule below was pinned only by whole
/// scenario outcomes, which report "arrivals fell from 24 to 19" rather than
/// naming the rule that broke.
/// <para>
/// Deliberately a recording fake and not a mock with expectations. Asserting
/// that <c>ClaimSlot</c> "was called" tests the double; asserting WHICH cell it
/// was given tests the doctrine.
/// </para>
/// </remarks>
public sealed class FakeGroupOps : IGroupOps
{
    private readonly Dictionary<int, int> _cell = [];
    private readonly Dictionary<int, int> _goal = [];
    private readonly Dictionary<int, bool> _hasSlot = [];
    private readonly Dictionary<int, int> _stalled = [];
    private readonly Dictionary<int, double> _cost = [];

    /// <summary>Cells some slot-holder is aimed at. System-wide, as the real one is.</summary>
    public HashSet<int> Claimed { get; } = [];

    /// <summary>Cells with a unit parked on them.</summary>
    public HashSet<int> Settled { get; } = [];

    /// <summary>Cells with anybody standing on them.</summary>
    public HashSet<int> Occupied { get; } = [];

    /// <summary>Every <see cref="ClaimSlot"/> in order, as (member, cell).</summary>
    public List<(int Id, int Cell)> Claims { get; } = [];

    /// <summary>Every <see cref="ReleaseSlot"/> in order.</summary>
    public List<int> Releases { get; } = [];

    /// <summary>Every <see cref="Hold"/> in order, as (member, ticks).</summary>
    public List<(int Id, int Ticks)> Holds { get; } = [];

    /// <summary>Every <see cref="Wake"/> in order.</summary>
    public List<int> Wakes { get; } = [];

    /// <inheritdoc/>
    public int CurrentTick { get; set; }

    /// <inheritdoc/>
    public int Destination { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<int> Slots { get; set; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<int> Members { get; set; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<Chokepoint> Chokepoints { get; set; } = [];

    /// <summary>
    /// Places a member: where it stands, where it is aimed, whether it holds a
    /// slot, and how many replans it has failed.
    /// </summary>
    public FakeGroupOps With(int id, int cell, int goal, bool hasSlot = false, int stalledReplans = 0)
    {
        _cell[id] = cell;
        _goal[id] = goal;
        _hasSlot[id] = hasSlot;
        _stalled[id] = stalledReplans;
        Occupied.Add(cell);
        if (hasSlot) { Claimed.Add(goal); }

        Members = [.. Members.Append(id).Distinct().Order()];
        return this;
    }

    /// <summary>
    /// Sets the distance from a cell to the destination. Unset cells answer
    /// <see cref="double.PositiveInfinity"/>, so a test that forgets one gets an
    /// unreachable cell rather than a plausible zero.
    /// </summary>
    public FakeGroupOps Cost(int cell, double cost)
    {
        _cost[cell] = cost;
        return this;
    }

    /// <inheritdoc/>
    public double FieldCost(int cell) => _cost.TryGetValue(cell, out var c) ? c : double.PositiveInfinity;

    /// <inheritdoc/>
    public int CellOf(int id) => _cell[id];

    /// <inheritdoc/>
    public int GoalOf(int id) => _goal[id];

    /// <inheritdoc/>
    public int StalledReplans(int id) => _stalled[id];

    /// <inheritdoc/>
    public bool HasSlot(int id) => _hasSlot[id];

    /// <inheritdoc/>
    public bool IsClaimed(int cell) => Claimed.Contains(cell);

    /// <inheritdoc/>
    public int ClaimantOf(int cell)
    {
        foreach (var id in Members)
        {
            if (_hasSlot[id] && _goal[id] == cell)
            {
                return id;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public bool IsSettled(int cell) => Settled.Contains(cell);

    /// <inheritdoc/>
    public bool IsOccupied(int cell) => Occupied.Contains(cell);

    /// <inheritdoc/>
    public IReadOnlyList<int> ReachableSpots(IReadOnlyList<int> fromMembers) =>
        [.. Slots.Where(s => !Claimed.Contains(s) && !Occupied.Contains(s)).OrderBy(FieldCost).ThenBy(s => s)];

    /// <inheritdoc/>
    /// <remarks>
    /// Mirrors the real one deliberately, INCLUDING the held check. A fake that
    /// retracted unconditionally would hide the bug the real one had; a fake that
    /// is simply correct lets a test assert the claimed set and mean it.
    /// </remarks>
    public void ClaimSlot(int id, int cell)
    {
        Claims.Add((id, cell));
        if (_hasSlot[id]) { Claimed.Remove(_goal[id]); }

        _hasSlot[id] = true;
        _goal[id] = cell;
        Claimed.Add(cell);
    }

    /// <inheritdoc/>
    public void ReleaseSlot(int id)
    {
        Releases.Add(id);
        if (!_hasSlot[id]) { return; }

        _hasSlot[id] = false;
        Claimed.Remove(_goal[id]);
    }

    /// <inheritdoc/>
    public void Wake(int id) => Wakes.Add(id);

    /// <inheritdoc/>
    public void Hold(int id, int ticks) => Holds.Add((id, ticks));
}
