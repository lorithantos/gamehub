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

    private readonly HashSet<int> _heldUp = [];

    /// <summary>
    /// Marks a member as unable to move this tick, whatever the reason. The
    /// default is that everyone is moving, so a fixture only says this when the
    /// rule under test is about being stopped.
    /// </summary>
    public FakeGroupOps HeldUp(int id)
    {
        _heldUp.Add(id);
        return this;
    }

    /// <inheritdoc/>
    public bool IsMoving(int id) => !_heldUp.Contains(id);

    /// <summary>Cells that are a chokepoint or beside one. Empty unless a fixture says otherwise.</summary>
    public HashSet<int> Doorways { get; } = [];

    /// <inheritdoc/>
    public bool IsDoorway(int cell) => Doorways.Contains(cell);

    /// <inheritdoc/>
    public bool IsClaimed(int cell) => Claimed.Contains(cell);

    /// <summary>
    /// Records a claim by an agent OUTSIDE this group: the cell reads as claimed,
    /// <see cref="ClaimantOf"/> names the outsider, and the outsider is not in
    /// <see cref="Members"/>. What a second, concurrent group looks like from here.
    /// </summary>
    public FakeGroupOps ClaimedBy(int cell, int outsider)
    {
        _hasSlot[outsider] = true;
        _goal[outsider] = cell;
        Claimed.Add(cell);
        return this;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// System-wide, as the real one is: every agent placed here, member or
    /// outsider, not only <see cref="Members"/>.
    /// </remarks>
    public int ClaimantOf(int cell)
    {
        foreach (var (id, hasSlot) in _hasSlot)
        {
            if (hasSlot && _goal[id] == cell)
            {
                return id;
            }
        }

        return -1;
    }

    /// <summary>
    /// Mirrors the real seam: a mutator aimed at an agent outside the group is
    /// refused before anything is recorded.
    /// </summary>
    private void RequireMember(int id)
    {
        // On station or away, both are members: confinement is about the group.
        if (!Members.Contains(id) && !Dispatched.Contains(id))
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Not a member of this group.");
        }
    }

    /// <inheritdoc/>
    public bool IsSettled(int cell) => Settled.Contains(cell);

    /// <inheritdoc/>
    public bool IsOccupied(int cell) => Occupied.Contains(cell);

    /// <summary>Cells a member cannot walk to. Empty unless a fixture says otherwise.</summary>
    public HashSet<int> Unreachable { get; } = [];

    /// <inheritdoc/>
    public bool CanWalkTo(int id, int cell) => !Unreachable.Contains(cell);

    /// <summary>Neighbours per cell, for the one rule that needs geometry. Unset cells have none.</summary>
    public Dictionary<int, int[]> Adjacent { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<int> Neighbours(int cell) => Adjacent.TryGetValue(cell, out var n) ? n : [];

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
        RequireMember(id);
        Claims.Add((id, cell));
        if (_hasSlot[id]) { Claimed.Remove(_goal[id]); }

        _hasSlot[id] = true;
        _goal[id] = cell;
        Claimed.Add(cell);
    }

    /// <inheritdoc/>
    public void ReleaseSlot(int id)
    {
        RequireMember(id);
        Releases.Add(id);
        if (!_hasSlot[id]) { return; }

        _hasSlot[id] = false;
        Claimed.Remove(_goal[id]);
    }

    private readonly HashSet<int> _cannotPark = [];

    /// <summary>Every <see cref="Park"/> in order, as (member, whether it was allowed).</summary>
    public List<(int Id, bool Parked)> Parks { get; } = [];

    /// <summary>
    /// Marks a member as standing on a cell somebody else's plan crosses, so a
    /// <see cref="Park"/> on it is refused. The default is that anyone may park,
    /// so a fixture only says this when the rule under test is about refusal.
    /// </summary>
    public FakeGroupOps CannotPark(int id)
    {
        _cannotPark.Add(id);
        return this;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Mirrors the real one: a refusal records nothing but the attempt; a
    /// success is a claim on the cell underfoot plus the cell reading as
    /// settled, which is what a one-cell plan looks like from the seam.
    /// </remarks>
    public bool Park(int id)
    {
        RequireMember(id);
        var allowed = !_cannotPark.Contains(id);
        Parks.Add((id, allowed));
        if (!allowed) { return false; }

        ClaimSlot(id, _cell[id]);
        Settled.Add(_cell[id]);
        _heldUp.Add(id);
        return true;
    }

    /// <inheritdoc/>
    public void Wake(int id)
    {
        RequireMember(id);
        Wakes.Add(id);
    }

    /// <inheritdoc/>
    public void Hold(int id, int ticks)
    {
        RequireMember(id);
        Holds.Add((id, ticks));
    }

    private readonly Dictionary<int, int> _errand = [];

    /// <inheritdoc/>
    public IReadOnlyList<int> Dispatched { get; private set; } = [];

    /// <inheritdoc/>
    public int ErrandOf(int id) => _errand.TryGetValue(id, out var destination) ? destination : -1;

    /// <summary>
    /// Marks a member as away on an errand: out of <see cref="Members"/>, into
    /// <see cref="Dispatched"/>, holding no slot and aimed at the destination.
    /// What the real seam projects after <see cref="MovementSystem.Dispatch"/>,
    /// which no doctrine can call -- so it is state a test sets, not a verb.
    /// </summary>
    public FakeGroupOps Away(int id, int destination)
    {
        RequireMember(id);
        if (_hasSlot[id]) { Claimed.Remove(_goal[id]); }

        _hasSlot[id] = false;
        _goal[id] = destination;
        _errand[id] = destination;
        Members = [.. Members.Where(m => m != id)];
        Dispatched = [.. Dispatched.Append(id).Distinct().Order()];
        return this;
    }

    /// <summary>
    /// The member is back on station: aimed at the ring's innermost slot and
    /// holding nothing, as <see cref="MovementSystem.Recall"/> leaves it.
    /// </summary>
    public FakeGroupOps Back(int id)
    {
        RequireMember(id);
        if (!_errand.Remove(id)) { return this; }

        _hasSlot[id] = false;
        _goal[id] = Slots.Count > 0 ? Slots[0] : Destination;
        Dispatched = [.. Dispatched.Where(m => m != id)];
        Members = [.. Members.Append(id).Distinct().Order()];
        return this;
    }
}
