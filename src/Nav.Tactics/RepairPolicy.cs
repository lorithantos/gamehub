namespace Nav.Tactics;

/// <summary>
/// Sends the damaged to repair and takes them back when they are repaired: the
/// component every doctrine that keeps its units alive is built from.
/// </summary>
/// <remarks>
/// Lifted out of <see cref="GuardDoctrine"/> when the patrol turned out to need
/// exactly the same rule and had none -- a patrol unit fought to the death.
/// The rule is the same whatever the squad is otherwise doing: on each pass,
/// every member away whose health is back at or above <see cref="ReturnAbove"/>
/// is rejoined, then every member on station whose health has fallen below
/// <see cref="RetreatBelow"/> is detached to the nearest repair point, preferring
/// one no fellow is already heading to so two casualties do not queue at one
/// pad. The two thresholds are apart on purpose: a unit hovering at one value
/// would otherwise leave and return every tick.
/// <para>
/// <b>Run it first.</b> A doctrine carrying this calls <see cref="Advance"/>
/// before its own passes, so that a pad freed this pass is visible to the
/// retreat, and so that the doctrine's own moves see who has just left. It
/// touches nothing but the away set: no station, no route, no engagement.
/// </para>
/// <para>
/// The played form is <em>retreat at middling damage, return as soon as it is
/// worth it</em> -- frequent short trips, so the line is never long without
/// the unit. The defaults here still say retreat late and return full; they
/// are what the recorded replays were made with, and the demos set their own.
/// Rank-aware thresholds and a reserve count belong here too, and arrive with
/// rank.
/// </para>
/// </remarks>
public sealed class RepairPolicy
{
    /// <param name="retreatBelow">Health fraction below which a member on station is sent to repair.</param>
    /// <param name="returnAbove">Health fraction at or above which a member away is brought back.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A threshold is outside 0..1, or the return threshold is not above the retreat one.
    /// </exception>
    public RepairPolicy(double retreatBelow = 0.4, double returnAbove = 0.9)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retreatBelow);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(returnAbove, 1.0);
        if (returnAbove <= retreatBelow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(returnAbove), returnAbove, "The return threshold must be above the retreat threshold, or a unit flaps.");
        }

        RetreatBelow = retreatBelow;
        ReturnAbove = returnAbove;
    }

    /// <summary>Health fraction below which a member on station is sent to repair.</summary>
    public double RetreatBelow { get; }

    /// <summary>Health fraction at or above which a member away is brought back.</summary>
    public double ReturnAbove { get; }

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
