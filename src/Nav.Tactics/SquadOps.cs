namespace Nav.Tactics;

/// <summary>
/// The squad seam over a live <see cref="MovementSystem"/>, built once per pass.
/// </summary>
/// <remarks>
/// Reads come from one snapshot of the system's agents taken in the constructor,
/// so a pass sees one consistent tick however many questions it asks and
/// whatever it orders in between. Every verb is one call on the movement
/// system's public surface; there is nothing else here to reach.
/// </remarks>
internal sealed class SquadOps : ISquadOps
{
    private readonly Squad _squad;
    private readonly MovementSystem _system;
    private readonly IReadOnlyList<AgentState> _agents;

    internal SquadOps(Squad squad, MovementSystem system)
    {
        _squad = squad;
        _system = system;
        _agents = system.Agents;

        Members = [.. squad.Members];
        Away = [.. Members.Where(id => id < _agents.Count && _agents[id].Away)];
    }

    /// <inheritdoc/>
    public string Name => _squad.Name;

    /// <inheritdoc/>
    public int CurrentTick => _system.CurrentTick;

    /// <inheritdoc/>
    public int Anchor => _squad.Anchor;

    /// <inheritdoc/>
    public IReadOnlyList<int> Members { get; }

    /// <inheritdoc/>
    public IReadOnlyList<int> Away { get; }

    /// <inheritdoc/>
    public int CellOf(int id) => Member(id).Cell;

    /// <inheritdoc/>
    public int GoalOf(int id) => Member(id).Goal;

    /// <inheritdoc/>
    public bool HasArrived(int id) => Member(id).Arrived;

    /// <inheritdoc/>
    public int ErrandOf(int id) => Member(id).Errand;

    /// <inheritdoc/>
    public void MoveAll(int destination)
    {
        _system.Order(Members, destination);
        _squad.Anchor = destination;
    }

    /// <inheritdoc/>
    public void Detach(int id, int destination)
    {
        RequireMember(id);
        if (_squad.Anchor < 0)
        {
            throw new InvalidOperationException(
                $"squad '{_squad.Name}' has never been moved as a group, so there is no formation to detach from.");
        }

        _system.Dispatch(id, destination);
    }

    /// <inheritdoc/>
    public void Rejoin(int id)
    {
        RequireMember(id);
        _system.Recall(id);
    }

    /// <summary>
    /// The snapshot row for a member, refusing an id outside the squad before
    /// anything is read: confinement is the seam's, not the doctrine's.
    /// </summary>
    private AgentState Member(int id)
    {
        RequireMember(id);
        if (id >= _agents.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(id), id, $"squad '{_squad.Name}' lists agent {id}, which this system does not have.");
        }

        return _agents[id];
    }

    private void RequireMember(int id)
    {
        if (!_squad.Members.Contains(id))
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, $"not a member of squad '{_squad.Name}'.");
        }
    }
}
