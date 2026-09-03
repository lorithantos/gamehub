namespace Nav.Tactics;

/// <summary>
/// Holds a station, and keeps its units alive with a <see cref="RepairPolicy"/>.
/// </summary>
/// <remarks>
/// The behaviour the whole project was started for: a guard that does not stand
/// beside the cannon until it dies.
/// <para>
/// The retreat and return are the repair policy's. What is the guard's is the
/// STATION — wherever the squad was last moved as a group.
/// </para>
/// <para>
/// Before any such move the doctrine's own <c>station</c> is used once; after a
/// group move the guard holds the new place rather than marching back.
/// </para>
/// <para>
/// No engagement in this version: a guard neither chases nor sorties. That is
/// the leash's job, and it arrives with the patrol.
/// </para>
/// </remarks>
public sealed class GuardDoctrine : SquadDoctrine
{
    private readonly int _station;

    /// <param name="station">Where to stand until the squad is moved as a group.</param>
    /// <param name="repair">How the damaged are sent away and brought back.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="station"/> is negative.</exception>
    public GuardDoctrine(int station, RepairPolicy repair)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(station);
        ArgumentNullException.ThrowIfNull(repair);

        _station = station;
        Repair = repair;
    }

    /// <param name="station">Where to stand until the squad is moved as a group.</param>
    /// <param name="retreatBelow">Health fraction below which a member on station is sent to repair.</param>
    /// <param name="returnAbove">Health fraction at or above which a member away is brought back.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A threshold is outside 0..1, or the return threshold is not above the retreat one.
    /// </exception>
    public GuardDoctrine(int station, double retreatBelow = 0.4, double returnAbove = 0.9)
        : this(station, new RepairPolicy(retreatBelow, returnAbove))
    {
    }

    /// <summary>How the damaged are sent away and brought back.</summary>
    public RepairPolicy Repair { get; }

    /// <summary>Health fraction below which a member on station is sent to repair.</summary>
    public double RetreatBelow => Repair.RetreatBelow;

    /// <summary>Health fraction at or above which a member away is brought back.</summary>
    public double ReturnAbove => Repair.ReturnAbove;

    /// <inheritdoc/>
    public override void Advance(ISquadOps ops)
    {
        ArgumentNullException.ThrowIfNull(ops);

        if (ops.Anchor < 0)
        {
            ops.MoveAll(_station);
            return;
        }

        Repair.Advance(ops);
    }
}
