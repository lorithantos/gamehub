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
    private readonly IPerception _perception;
    private readonly IReadOnlyList<AgentState> _agents;

    internal SquadOps(Squad squad, MovementSystem system, IPerception perception)
    {
        _squad = squad;
        _system = system;
        _perception = perception;
        _agents = system.Agents;

        // The dead are not listed. A doctrine never has to ask whether a member
        // still exists: what it is handed is who can act, and a casualty simply
        // stops appearing, on station and away alike.
        Members = [.. squad.Members.Where(id => id >= _agents.Count || _agents[id].Alive)];
        Away = [.. Members.Where(id => id < _agents.Count && _agents[id].Away)];
        Hostiles = perception.Hostiles;
        Sightings = perception.Sightings;
        RepairPoints = perception.RepairPoints;
    }

    /// <inheritdoc/>
    public double HealthOf(int id)
    {
        RequireMember(id);
        return _perception.HealthOf(id);
    }

    /// <inheritdoc/>
    public int RankOf(int id)
    {
        RequireMember(id);
        return _perception.RankOf(id);
    }

    /// <inheritdoc/>
    public IReadOnlyList<int> Hostiles { get; }

    /// <inheritdoc/>
    public IReadOnlyList<Sighting> Sightings { get; }

    /// <inheritdoc/>
    public IReadOnlyList<int> RepairPoints { get; }

    /// <inheritdoc/>
    public double Distance(int cellA, int cellB)
    {
        var grid = _system.Grid;
        return Movement.OctileDistance(
            grid.ColumnOf(cellA), grid.RowOf(cellA), grid.ColumnOf(cellB), grid.RowOf(cellB));
    }

    /// <inheritdoc/>
    public int ColumnOf(int cell) => _system.Grid.ColumnOf(cell);

    /// <inheritdoc/>
    public int RowOf(int cell) => _system.Grid.RowOf(cell);

    /// <inheritdoc/>
    /// <remarks>
    /// Who is on station is read LIVE here, not from the pass's snapshot. A
    /// verb acts on the world as it is: a doctrine that detaches a casualty and
    /// then sorties in the same pass must not drag that casualty along, and an
    /// order ends an errand, so the snapshot would have done exactly that.
    /// </remarks>
    public void Sortie(int destination)
    {
        var agents = _system.Agents;
        var onStation = Members.Where(id => id < agents.Count && agents[id].Alive && !agents[id].Away).ToArray();
        if (onStation.Length == 0)
        {
            return;
        }

        _system.Order(onStation, destination);
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
    public void MoveAll(int destination) => _squad.MoveAll(_system, destination);

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

        // Into the formation the squad is in NOW. After a sortie that is not the
        // one the member left, so it joins alongside the lowest fellow on
        // station; with nobody on station, the formation it left is the squad.
        var alongside = Members.FirstOrDefault(m => m != id && !Away.Contains(m), -1);
        if (alongside >= 0)
        {
            _system.Recall(id, alongside);
        }
        else
        {
            _system.Recall(id);
        }
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
