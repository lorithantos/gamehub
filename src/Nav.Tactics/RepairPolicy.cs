namespace Nav.Tactics;

/// <summary>
/// Sends the damaged to repair and takes them back when they are repaired: the
/// component every doctrine that keeps its units alive is built from.
/// </summary>
/// <remarks>
/// One pass, in this order:
/// <list type="number">
/// <item><description>Everyone away at or above <see cref="ReturnAbove"/>
/// rejoins.</description></item>
/// <item><description>Everyone on station below their rank's
/// <see cref="RetreatBelow"/> is sent to the nearest free repair point.</description></item>
/// </list>
/// <para>
/// The thresholds are apart on purpose, or a unit at one value leaves and
/// returns every tick.
/// </para>
/// <para>
/// <b>Run it first.</b> Call <see cref="Advance"/> before a doctrine's own
/// passes, so a pad freed this pass is visible to the retreat. It touches
/// nothing but the away set.
/// </para>
/// <para>
/// <b>Rank raises the retreat threshold</b>: a veteran is pulled EARLIER than a
/// rookie, because the good unit is the one you cannot replace. The opposite
/// table is a different doctrine and is accepted — the direction is data.
/// </para>
/// <para>
/// <b>The reserve is spent on the LOWEST rank first</b>, the opposite way round
/// from the thresholds. A veteran's place is the line: it earns faster there.
/// </para>
/// <para>
/// <b>Neither self-healing nor shielding is built yet.</b> The ordering is
/// already shaped for them.
/// </para>
/// </remarks>
public sealed class RepairPolicy
{
    // WHY THE TWO RANK RULES POINT OPPOSITE WAYS.
    //
    // They answer different questions. The threshold asks who is worth pulling
    // when there is room, and the answer is the veteran, because it cannot be
    // replaced. The reserve asks who is worth pulling when there is NOT room,
    // and the answer is the rookie -- but the reason is about the veteran.
    //
    // A veteran earns faster where the enemy is, and at full rank it is meant to
    // heal itself, so a scarce pad handed to one is handed to the unit least
    // likely to need it. LEAST LIKELY, not never: self-healing is a rate and a
    // rate can be overwhelmed, so a veteran under enough fire still falls under
    // its threshold and still goes.
    //
    // Its standing there is also what makes the position survivable for the
    // rookies beside it: they are safer in its company and they earn more slowly
    // for the same reason, which is the trade a player is actually making when
    // deciding who to post where. Rotating rookies through the pads and leaving
    // the veteran holding is not spending the veteran; it is putting each unit
    // where it does the most.
    //
    // So a stretched squad shows the veteran holding the line badly hurt while
    // the rookies rotate through the pads, and an unstretched one shows the
    // veteran pulled at a scratch. Both are the same doctrine at different
    // pressures. docs/scale-and-doctrine.md has why the reserve inverted.
    //
    // NOT BUILT: nothing heals by rank, and a rookie beside a veteran earns
    // exposure at exactly the rate it would alone -- DemoWorld's exposure is
    // proximity to a hostile and nothing else.
    //
    // The played form is retreat at middling damage, return as soon as it is
    // worth it: frequent short trips, so the line is never long without the
    // unit. The defaults here still say retreat late and return full, because
    // they are what the recorded replays were made with; the demos set their own.

    private readonly double[] _retreatByRank;

    /// <param name="retreatBelow">Health fraction below which a member on station is sent to repair.</param>
    /// <param name="returnAbove">Health fraction at or above which a member away is brought back.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A threshold is outside 0..1, or the return threshold is not above the retreat one.
    /// </exception>
    public RepairPolicy(double retreatBelow = 0.4, double returnAbove = 0.9)
        : this([retreatBelow], returnAbove)
    {
    }

    /// <param name="retreatByRank">
    /// Health fraction below which a member is sent to repair, indexed by rank.
    /// A rank past the end of the table uses the last entry, so a two-entry
    /// table is a complete answer for every rank a world can invent. At least
    /// one entry.
    /// </param>
    /// <param name="returnAbove">Health fraction at or above which a member away is brought back.</param>
    /// <param name="reserve">How many members must stay on station however hurt the squad is.</param>
    /// <exception cref="ArgumentNullException"><paramref name="retreatByRank"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The table is empty, a threshold is outside 0..1, the return threshold is
    /// not above every retreat threshold, or the reserve is negative.
    /// </exception>
    public RepairPolicy(IReadOnlyList<double> retreatByRank, double returnAbove, int reserve = 0)
    {
        ArgumentNullException.ThrowIfNull(retreatByRank);
        ArgumentOutOfRangeException.ThrowIfZero(retreatByRank.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(returnAbove, 1.0);
        ArgumentOutOfRangeException.ThrowIfNegative(reserve);

        foreach (var threshold in retreatByRank)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(threshold);
            if (returnAbove <= threshold)
            {
                // Checked against EVERY entry, not just the first: a table whose
                // veteran threshold overtakes the return would flap that rank
                // and only that rank, which is the kind of defect that reaches a
                // replay before anyone notices.
                throw new ArgumentOutOfRangeException(
                    nameof(returnAbove), returnAbove, "The return threshold must be above every retreat threshold, or a unit flaps.");
            }
        }

        _retreatByRank = [.. retreatByRank];
        ReturnAbove = returnAbove;
        Reserve = reserve;
    }

    /// <summary>Health fraction below which a rank-0 member on station is sent to repair.</summary>
    public double RetreatBelow => _retreatByRank[0];

    /// <summary>Retreat thresholds by rank; the last entry covers every rank above it.</summary>
    public IReadOnlyList<double> RetreatByRank => _retreatByRank;

    /// <summary>Health fraction at or above which a member away is brought back.</summary>
    public double ReturnAbove { get; }

    /// <summary>How many members must stay on station however hurt the squad is.</summary>
    public int Reserve { get; }

    /// <summary>The retreat threshold for a member of this rank.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rank"/> is negative.</exception>
    public double RetreatBelowFor(int rank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rank);
        return _retreatByRank[Math.Min(rank, _retreatByRank.Length - 1)];
    }

    /// <summary>
    /// One pass: bring back the repaired, then send off the damaged. Nothing
    /// else about the squad is touched.
    /// </summary>
    /// <param name="ops">The seam for this squad and tick. Never null.</param>
    public void Advance(ISquadOps ops)
    {
        ArgumentNullException.ThrowIfNull(ops);

        // Away first, so a pad freed this pass is visible to the retreat below
        // -- and because bringing a unit back never needs anything the retreat
        // decides. ONLY THOSE AWAY FOR REPAIR: a member on some other errand --
        // a scout, a unit the player sent somewhere -- is healthy and away on
        // purpose, and is not this policy's to recall. The guard used to bring
        // back anyone healthy; the patrol's existing errand test caught it the
        // moment the rule became shared.
        foreach (var id in ops.Away)
        {
            if (ops.HealthOf(id) >= ReturnAbove && ops.RepairPoints.Contains(ops.ErrandOf(id)))
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

        // Everyone on station who is under their OWN rank's threshold, in the
        // order the reserve will spend itself: rank ASCENDING, then health
        // ascending so between equals the hurt one goes, then id so a demo plays
        // the same way twice.
        //
        // Rank ascending because a veteran's place is the line: it earns faster
        // where the enemy is, at full rank it is meant to heal itself, and its
        // standing there is what makes the position survivable for the rookies
        // beside it. A scarce pad given to a veteran goes to the unit least
        // likely to need one -- not one that never will, since self-healing is
        // a rate and can be overwhelmed. Neither the self-healing nor the
        // shielding exists yet; the ordering is shaped for them.
        var leaving = ops.Members
            .Where(id => !ops.Away.Contains(id) && ops.HealthOf(id) < RetreatBelowFor(ops.RankOf(id)))
            .OrderBy(ops.RankOf)
            .ThenBy(ops.HealthOf)
            .ThenBy(id => id)
            .ToList();

        // Away is the snapshot this pass began with, so a member rejoined above
        // still counts against the line here. That errs toward keeping people
        // standing for one tick, which is the safe direction for a reserve.
        var onStation = ops.Members.Count - ops.Away.Count;

        foreach (var id in leaving)
        {
            if (onStation <= Reserve)
            {
                break;
            }

            var pad = NearestPad(ops, ops.CellOf(id), taken);
            taken.Add(pad);
            ops.Detach(id, pad);
            onStation--;
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
