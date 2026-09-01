namespace Nav.Core;

/// <summary>
/// How a group moves: the pluggable seam.
/// </summary>
/// <remarks>
/// No single movement design works for every scenario — a band of soldiers
/// walking across open desert is a different problem from rocket infantry
/// escorting a tank. A doctrine is the per-group strategy that decides slot
/// claiming, reconciliation, and pacing, invoked once per tick before planning.
/// It acts only through <see cref="IGroupOps"/>, which offers
/// queries and a small set of safe mutations — a doctrine cannot touch plans,
/// reservations, or the collision layer, so every guarantee underneath survives
/// any doctrine above. The implementing class is internal, so a doctrine written
/// outside this assembly sees three contracts and no concrete type at all.
/// <para>
/// <b>Passes take the narrowest facet they need, and pass the same object twice
/// to say so.</b> <see cref="IGroupOps"/> composes <see cref="IGroupView"/>,
/// <see cref="IGroupClaiming"/> and <see cref="IGroupPacing"/>; a pass declaring
/// <c>(IGroupView, IGroupPacing)</c> cannot claim a slot, and the compiler is
/// what stops it rather than a reviewer. That is why metering — which paces a
/// queue through a gate and has no business assigning parking — is structurally
/// unable to, where under one wide interface it merely happened not to.
/// </para>
/// <para>
/// The seam was extracted from two real implementations —
/// <see cref="GatherDoctrine"/> and <see cref="MeteredGatherDoctrine"/> — the
/// renderer-seam lesson applied: one implementation is a guess, two are a
/// hypothesis.
/// </para>
/// <para>
/// A doctrine may hold per-group state (a cooldown, a released set), and must
/// keep every decision deterministic: fixed iteration orders, no randomness,
/// ties on id. Replay is still the determinism test and doctrines run inside it.
/// </para>
/// </remarks>
public abstract class GroupDoctrine
{
    /// <summary>Called once per tick for the group, before planning spends its budget.</summary>
    public abstract void Advance(IGroupOps ops);
}

/// <summary>
/// The gathering doctrine: fill like water.
/// </summary>
/// <remarks>
/// Members aim at the shared destination and claim the innermost open parking
/// slot only once NEAR the current crust frontier, closest member first — the
/// way a real team fills in at a gathering point rather than pre-booking spots
/// from across the map. Hard-stalled members (two failed replans) are re-goaled
/// onto spots they can actually walk to, and a member with no reachable empty
/// spot takes its own cell: a hopeless jam becomes an honest arrival in place.
/// </remarks>
public class GatherDoctrine : GroupDoctrine
{
    /// <summary>Ticks between reconciliation passes, or reassignment becomes churn.</summary>
    private const int ReconcileCooldown = 8;

    /// <summary>A member this close to the crust frontier may claim a slot.</summary>
    private static readonly double ClaimMargin = 2.0 * Movement.DiagonalCost;

    private int _lastReconcileTick = -1_000_000;

    /// <summary>
    /// Three passes, always in this order: a stalled member standing on ground no
    /// worse than its goal parks where it is; whoever has reached the crust claims
    /// the innermost open slots; then, on a cooldown, hard-stalled members are
    /// re-goaled onto spots they can actually walk to. On a group that has finished
    /// settling all three fall straight through, which is why leaving the doctrine
    /// running costs nothing.
    /// </summary>
    /// <param name="ops">The seam for this group and tick. Never null.</param>
    public override void Advance(IGroupOps ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        SettleWhereYouStand(ops, ops);
        ClaimPass(ops, ops);
        ReconcilePass(ops, ops);
    }

    /// <summary>
    /// A stalled member standing on an unclaimed cell at least as close as its
    /// assigned goal claims where it stands. Arriving beats replanning to
    /// somewhere worse — the countermand fixture ended with a unit squatting on
    /// the DESTINATION ITSELF, walled in by later arrivals, still assigned a
    /// cell two steps away it could no longer reach. Stall-gated so a unit
    /// merely pausing in traffic never parks early; terminal by construction,
    /// so it cannot churn.
    /// </summary>
    private static void SettleWhereYouStand(IGroupView ops, IGroupClaiming claiming)
    {
        foreach (var id in ops.Members)
        {
            if (ops.StalledReplans(id) == 0 || ops.CellOf(id) == ops.GoalOf(id))
            {
                continue;
            }

            var cell = ops.CellOf(id);
            if (!ops.IsClaimed(cell) && ops.FieldCost(cell) <= ops.FieldCost(ops.GoalOf(id)))
            {
                claiming.ClaimSlot(id, cell);
            }
        }
    }

    /// <summary>
    /// FILL LIKE WATER. Claims open only just ahead of the current crust: the
    /// claiming radius is the outermost claimed slot plus a small margin, so a
    /// slot is booked moments before it is filled, by whoever is actually
    /// closest. A wide radius is pre-booking from across the field wearing a
    /// different hat — measured at 91 sealed holes against the 28 it was meant
    /// to fix; this rule measured 2, with the blob at the ideal pack exactly.
    /// </summary>
    private static void ClaimPass(IGroupView ops, IGroupClaiming claiming)
    {
        if (ops.Members.All(ops.HasSlot))
        {
            return;
        }

        var frontier = 0.0;
        foreach (var id in ops.Members)
        {
            if (ops.HasSlot(id))
            {
                frontier = Math.Max(frontier, ops.FieldCost(ops.GoalOf(id)));
            }
        }

        var radius = frontier + ClaimMargin;

        var near = ops.Members
            .Where(id => !ops.HasSlot(id))
            .Select(id => (Id: id, Cost: ops.FieldCost(ops.CellOf(id))))
            .Where(pair => pair.Cost <= radius)
            .OrderBy(pair => pair.Cost)
            .ThenBy(pair => pair.Id);

        foreach (var (id, _) in near)
        {
            // The first unit to reach the destination itself claimed it by
            // standing on it.
            if (ops.CellOf(id) == ops.GoalOf(id))
            {
                claiming.ClaimSlot(id, ops.GoalOf(id));
                continue;
            }

            // Innermost open slot, globally. Member-nearest picking was tried
            // against the endgame orbit and measured WORSE (11 late backward
            // moves against 4): with a whole group approaching from one face,
            // it burns the near-face slots first and forces every latecomer
            // through the pack. Innermost-global fills the far side while the
            // near ground is still empty road.
            foreach (var slot in ops.Slots)
            {
                if (ops.IsClaimed(slot) || ops.IsSettled(slot))
                {
                    continue;
                }

                // Never claim outward. A unit already at the rim — a displaced
                // claimant after a squatter's swap, most often — offered the
                // innermost REMAINING slot can be offered one behind itself,
                // and it walks away from the crowd to reach it. Standing put
                // and waiting for an interior slot is always better; the
                // reconcile pass is the safety net if none ever opens.
                if (ops.FieldCost(slot) > ops.FieldCost(ops.CellOf(id)))
                {
                    continue;
                }

                claiming.ClaimSlot(id, slot);
                break;
            }

            // Every slot taken: stay aimed at the destination; the hard-stall
            // reconciliation is the safety net for that.
        }
    }

    /// <summary>
    /// Goal assignment is a snapshot but settling is a process; this reconciles
    /// the two. Only HARD-stalled members are re-goaled — a single no-progress
    /// replan is usually traffic, and reassigning on any stall re-goals
    /// transiently blocked units until the group churns to a standstill.
    /// </summary>
    private void ReconcilePass(IGroupView ops, IGroupClaiming claiming)
    {
        if (ops.CurrentTick - _lastReconcileTick < ReconcileCooldown)
        {
            return;
        }

        var stalled = ops.Members
            .Where(id => ops.StalledReplans(id) >= 2 && ops.CellOf(id) != ops.GoalOf(id))
            .ToArray();
        if (stalled.Length == 0)
        {
            return;
        }

        _lastReconcileTick = ops.CurrentTick;

        // Spots the stalled crowd can ACTUALLY WALK TO, then closest member
        // takes closest spot — assigning by raw distance hands out the holes
        // the arrived crust has sealed shut.
        var spots = ops.ReachableSpots(stalled);
        var members = stalled
            .OrderBy(id => ops.FieldCost(ops.CellOf(id)))
            .ThenBy(id => id)
            .ToArray();

        for (var i = 0; i < members.Length; i++)
        {
            var id = members[i];
            var cell = ops.CellOf(id);
            var pick = i < spots.Count ? spots[i] : (ops.IsClaimed(cell) ? -1 : cell);
            if (pick < 0 || pick == ops.GoalOf(id))
            {
                continue;
            }

            // Only ever CLOSER, never farther. A member whose stall is mere
            // crowding at the rim is best left where it is, and the alternative
            // -- a goal beyond the crust -- is a unit walking away from the
            // destination it already reached.
            if (ops.FieldCost(pick) > ops.FieldCost(ops.GoalOf(id)) &&
                ops.FieldCost(pick) > ops.FieldCost(cell))
            {
                pick = cell;
            }

            // Never move a member's goal FARTHER from the destination than its
            // own position: beyond the crust there is nothing to gain, and a
            // member whose best spot is worse than standing arrives in place.
            if (pick != cell && ops.FieldCost(pick) > ops.FieldCost(cell))
            {
                // Standing put beats walking outward -- even when the cell
                // underfoot belongs to somebody else's claim. THE ENDGAME
                // RUSH-BACKWARDS came from the else branch of that condition:
                // a squatter on a claimed cell had no legal stay, so it was
                // sent to the only unclaimed spot left, which by then lay
                // BEHIND the settled rim -- units that had visibly arrived
                // turning around and walking away at the backstop. Yielding a
                // claimed cell is the claimant's business (it will stall and
                // reconcile in its turn); marching a unit outward to solve it
                // is a cure worse than the crowding.
                if (!ops.IsClaimed(cell))
                {
                    pick = cell;
                    if (pick == ops.GoalOf(id))
                    {
                        continue;
                    }
                }
                else
                {
                    // SQUATTER'S SWAP. The unit is standing on a cell somebody
                    // else claimed, and every remaining spot is farther out.
                    // Marching it away is the endgame rush-backwards; leaving
                    // it strands it. So it TAKES the claim it is standing on --
                    // an instant arrival -- and the absent claimant is returned
                    // to the queue to claim again. Sum of distances cannot
                    // rise: the squatter is already here, and the claimant was
                    // not. The classic MAPF goal swap, arrived at from the
                    // other direction.
                    //
                    // ClaimantOf is system-wide, so the holder may belong to
                    // another group, and another group's member is not this
                    // doctrine's to release. The seam would refuse the call;
                    // checking here means the squatter simply stays put, which
                    // is the right outcome, rather than the tick throwing.
                    var claimant = ops.ClaimantOf(cell);
                    if (claimant >= 0 && claimant != id && ops.Members.Contains(claimant))
                    {
                        claiming.ReleaseSlot(claimant);
                        claiming.ClaimSlot(id, cell);
                    }

                    continue;
                }
            }

            claiming.ClaimSlot(id, pick);
        }
    }
}

/// <summary>
/// Gathering, metered through chokepoints: the default doctrine.
/// </summary>
/// <remarks>
/// Where the map has no chokepoint between a member and the destination, this
/// IS <see cref="GatherDoctrine"/> — the metering layer does nothing, which is
/// why it can be the default. Where one exists, at most the chokepoint's width
/// in members approach it at once; the rest HOLD where they are, quietly, and
/// are released in field-distance order as predecessors pass through. A queue
/// discovered by reservation contention costs search nodes every tick; a queue
/// ordered by the doctrine costs nothing to stand in.
/// </remarks>
public sealed class MeteredGatherDoctrine : GroupDoctrine
{
    /// <summary>A released member has passed once it is this far inside.</summary>
    private static readonly double PassedMargin = Movement.DiagonalCost;

    /// <summary>
    /// Members per unit of gate width that may actively approach: a short
    /// CONVOY, nose to tail, not exclusive occupancy.
    /// </summary>
    private const int ConvoyDepth = 4;

    private readonly GatherDoctrine _gather = new();

    /// <summary>
    /// Meters, then gathers. The metering pass holds everyone past the front of the
    /// queue for two ticks; the rest of the tick is <see cref="GatherDoctrine"/>
    /// unchanged, on the same <paramref name="ops"/>. Metering is dormant until a
    /// member is actually AT a gate, so on open ground the two doctrines do the same
    /// thing for the same cost.
    /// </summary>
    /// <param name="ops">The seam for this group and tick. Never null.</param>
    public override void Advance(IGroupOps ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        Meter(ops, ops);
        _gather.Advance(ops);
    }

    /// <summary>How close to the gate somebody must be before the meter turns on.</summary>
    private static readonly double ContactRange = 2.0 * Movement.DiagonalCost;

    /// <summary>
    /// A STATELESS pacing brake, deliberately. Two stateful versions preceded
    /// it and both stranded units: a released-set entry that neither passed nor
    /// arrived consumed a width-one gate forever. This holds nothing in memory
    /// that can rot — each tick, the front of the queue (gate width x convoy
    /// depth, by field distance) plans freely and everyone deeper is held for
    /// two ticks. Holds lapse, the queue promotes itself as the front passes,
    /// and the failure mode of a missed hold is a unit planning slightly early
    /// — the scrum — never a unit frozen.
    /// </summary>
    /// <remarks>
    /// AND IT TURNS ON AT CONTACT, NOT AT ORDER TIME. Metering is door
    /// discipline, and door discipline starts at the door: until somebody has
    /// actually reached the gate there is no queue to manage, and the march
    /// across open ground is free. The first version engaged the moment the
    /// order was issued and froze the tail half a chamber from the gate —
    /// every batch then paid the full transit latency serially, which is where
    /// its measured 4x slowdown lived. With contact activation the whole group
    /// compresses to the doorway at scrum pace and the ordering applies to a
    /// queue that exists.
    /// </remarks>
    private static void Meter(IGroupView ops, IGroupPacing pacing)
    {
        // The chokepoint that stands between outsiders and the destination is
        // the one with the smallest field cost that still has members beyond
        // it. One gate is metered at a time: a map with a chain of gates
        // meters at the innermost first, and the queue re-forms naturally at
        // the next as members pass.
        Chokepoint? gate = null;
        var gateCost = double.PositiveInfinity;
        foreach (var choke in ops.Chokepoints)
        {
            var cost = ops.FieldCost(choke.Cell);
            if (cost < gateCost &&
                ops.Members.Any(id => ops.FieldCost(ops.CellOf(id)) > cost + PassedMargin))
            {
                gate = choke;
                gateCost = cost;
            }
        }

        if (gate is null)
        {
            return;
        }

        var queue = ops.Members
            .Where(id => ops.CellOf(id) != ops.GoalOf(id) &&
                         ops.FieldCost(ops.CellOf(id)) > gateCost + PassedMargin)
            .OrderBy(id => ops.FieldCost(ops.CellOf(id)))
            .ThenBy(id => id)
            .ToArray();

        // Dormant until contact: somebody has to be AT the door before there
        // is a queue to discipline.
        if (queue.Length == 0 ||
            ops.FieldCost(ops.CellOf(queue[0])) > gateCost + PassedMargin + ContactRange)
        {
            return;
        }

        for (var i = gate.Width * ConvoyDepth; i < queue.Length; i++)
        {
            // Holding is standing, cheaply: no goal change, no search, no
            // stall — just no replanning for a moment.
            pacing.Hold(queue[i], ticks: 2);
        }
    }
}
