namespace Nav.Tactics;

/// <summary>
/// A named, persistent membership: units that belong together whatever each of
/// them happens to be doing.
/// </summary>
/// <remarks>
/// Membership is not a movement property. The movement layer knows formations,
/// and a formation is an order: created by one command, holding that command's
/// ring, forgetting a unit the moment another command names it. A squad outlives
/// every order. It is what a numbered control group is in an RTS: a move is
/// something done TO the membership and resolved into a movement order when it
/// is issued, so a unit away on an errand of its own is still moved by the next
/// group move, and is still a member while it is away.
/// <para>
/// Membership can influence movement -- a group move, a detachment, a return --
/// and does so only through the public surface of <see cref="MovementSystem"/>.
/// A squad has no privileged access to the movement layer and needs none.
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
    /// Runs the doctrine once against <paramref name="system"/>. Call it each
    /// tick before <see cref="MovementSystem.Tick"/>, so that what the doctrine
    /// decides is what the tick then plans.
    /// </summary>
    public void Advance(MovementSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        Doctrine.Advance(new SquadOps(this, system));
    }
}
