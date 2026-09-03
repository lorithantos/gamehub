namespace Nav.Tactics;

/// <summary>
/// A named, persistent membership: units that belong together whatever each of
/// them happens to be doing.
/// </summary>
/// <remarks>
/// Membership is not a movement property. A formation is an ORDER — created by
/// one command, forgetting a unit the moment another command names it. A squad
/// outlives every order.
/// <para>
/// It is what a numbered control group is in an RTS: a move is done TO the
/// membership and resolved into a movement order when issued.
/// </para>
/// <para>
/// So a unit away on an errand is still moved by the next group move, and is
/// still a member while it is away.
/// </para>
/// <para>
/// A squad influences movement only through the public surface of
/// <see cref="MovementSystem"/>, and needs no privileged access.
/// </para>
/// </remarks>
public sealed class Squad
{
    private readonly SortedSet<int> _members;

    /// <param name="name">What the player calls it. Not required to be unique.</param>
    /// <param name="members">Agent ids. Non-negative; repeats collapse.</param>
    /// <param name="doctrine">How the squad behaves, tick by tick. Holds its own state.</param>
    /// <exception cref="ArgumentOutOfRangeException">A member id is negative.</exception>
    public Squad(string name, IEnumerable<int> members, SquadDoctrine doctrine)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(doctrine);

        _members = [];
        foreach (var id in members)
        {
            Add(id);
        }

        Name = name;
        Doctrine = doctrine;
    }

    /// <summary>What the player calls it.</summary>
    public string Name { get; }

    /// <summary>How the squad behaves. Deterministic, and holds its own state.</summary>
    public SquadDoctrine Doctrine { get; }

    /// <summary>
    /// Where the squad is stationed: the destination of its last group move, or
    /// -1 before it has been moved as a group at all.
    /// </summary>
    public int Anchor { get; internal set; } = -1;

    /// <summary>Every member, ascending, whatever each is doing.</summary>
    public IReadOnlySet<int> Members => _members;

    /// <summary>Adds a member. Returns false if it was already one.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is negative.</exception>
    public bool Add(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        return _members.Add(id);
    }

    /// <summary>
    /// Removes a member. Returns false if it was not one. The movement layer is
    /// not told: whatever the unit was doing, it keeps doing.
    /// </summary>
    public bool Remove(int id) => _members.Remove(id);

    /// <summary>
    /// The player's group move: every member, detached ones included, is ordered
    /// to <paramref name="destination"/> as one formation, and
    /// <see cref="Anchor"/> becomes that cell so the doctrine holds the new place
    /// rather than the old one. Selecting a squad and saying "move" is exactly
    /// one input to the movement engine, and this is it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A member id this system does not have.</exception>
    public void MoveAll(MovementSystem system, int destination)
    {
        ArgumentNullException.ThrowIfNull(system);

        // The dead stay members and stay behind. A squad that has taken losses
        // is still one input to the engine; it is just a smaller one.
        var agents = system.Agents;
        var living = _members.Where(id => id >= agents.Count || agents[id].Alive).ToArray();
        if (living.Length == 0)
        {
            return;
        }

        system.Order(living, destination);
        Anchor = destination;
    }

    /// <summary>
    /// Runs the doctrine once against <paramref name="system"/> in a quiet world:
    /// nobody hurt, nobody hostile. See the overload for the real thing.
    /// </summary>
    public void Advance(MovementSystem system) => Advance(system, NoPerception.Instance);

    /// <summary>
    /// Runs the doctrine once against <paramref name="system"/>, seeing the world
    /// as <paramref name="perception"/> reports it this tick. Call it each tick
    /// before <see cref="MovementSystem.Tick"/>, so that what the doctrine
    /// decides is what the tick then plans.
    /// </summary>
    public void Advance(MovementSystem system, IPerception perception)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(perception);
        Doctrine.Advance(new SquadOps(this, system, perception));
    }
}
