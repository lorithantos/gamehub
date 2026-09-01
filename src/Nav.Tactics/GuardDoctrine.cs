namespace Nav.Tactics;

/// <summary>
/// Holds a station, sends the damaged to repair, and takes them back when they
/// are repaired.
/// </summary>
/// <remarks>
/// The behaviour the whole project was started for: a guard that does not stand
/// beside the cannon until it dies. Each pass looks at every member on station
/// and detaches any whose health has fallen below <see cref="RetreatBelow"/> to
/// the nearest repair point, preferring one no fellow is already heading to so
/// two casualties do not queue at one pad; and looks at every member away and
/// rejoins any whose health is back above <see cref="ReturnAbove"/>. The two
/// thresholds are apart on purpose: a unit hovering at one value would otherwise
/// leave and return every tick.
/// <para>
/// The station is wherever the squad was last moved as a group. Before any such
/// move the doctrine's own <c>station</c> is used once; after a group move by
/// the player the guard holds the new place rather than marching back.
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
    /// <param name="retreatBelow">Health fraction below which a member on station is sent to repair.</param>
    /// <param name="returnAbove">Health fraction at or above which a member away is brought back.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A threshold is outside 0..1, or the return threshold is not above the retreat one.
    /// </exception>
    public GuardDoctrine(int station, double retreatBelow = 0.4, double returnAbove = 0.9)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(station);
        ArgumentOutOfRangeException.ThrowIfNegative(retreatBelow);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(returnAbove, 1.0);
        if (returnAbove <= retreatBelow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(returnAbove), returnAbove, "The return threshold must be above the retreat threshold, or a unit flaps.");
        }

        _station = station;
        RetreatBelow = retreatBelow;
        ReturnAbove = returnAbove;
    }

    /// <summary>Health fraction below which a member on station is sent to repair.</summary>
    public double RetreatBelow { get; }

    /// <summary>Health fraction at or above which a member away is brought back.</summary>
    public double ReturnAbove { get; }

    /// <inheritdoc/>
    public override void Advance(ISquadOps ops)
    {
        ArgumentNullException.ThrowIfNull(ops);

        if (ops.Anchor < 0)
        {
            ops.MoveAll(_station);
            return;
        }

        // Away first, so a pad freed this pass is visible to the retreat below
        // -- and because bringing a unit back never needs anything the retreat
        // decides.
        foreach (var id in ops.Away)
        {
            if (ops.HealthOf(id) >= ReturnAbove)
            {
                ops.Rejoin(id);
            }
        }

        if (ops.RepairPoints.Count == 0)
        {
            return;
        }

        // Pads already spoken for by a fellow on its way; grows as this pass
        // sends more, so two casualties in one pass spread too.
        var taken = new HashSet<int>(ops.Away.Select(ops.ErrandOf));

        foreach (var id in ops.Members)
        {
            if (ops.Away.Contains(id) || ops.HealthOf(id) >= RetreatBelow)
            {
                continue;
            }

            var pad = NearestPad(ops, ops.CellOf(id), taken);
            taken.Add(pad);
            ops.Detach(id, pad);
        }
    }

    /// <summary>
    /// The nearest repair point to <paramref name="from"/> that nobody is
    /// heading to, or the nearest of all when every pad is spoken for. Ties on
    /// distance go to the lower cell, so the choice is the same on every run.
    /// </summary>
    private static int NearestPad(ISquadView ops, int from, HashSet<int> taken)
    {
        var best = -1;
        var bestDistance = double.PositiveInfinity;
        var anyFree = false;

        foreach (var pad in ops.RepairPoints)
        {
            var free = !taken.Contains(pad);
            if (anyFree && !free)
            {
                continue;
            }

            var distance = ops.Distance(from, pad);
            if (free && !anyFree)
            {
                // The first free pad beats every taken one seen so far.
                anyFree = true;
                best = pad;
                bestDistance = distance;
                continue;
            }

            if (distance < bestDistance || (distance == bestDistance && pad < best))
            {
                best = pad;
                bestDistance = distance;
            }
        }

        return best;
    }
}
