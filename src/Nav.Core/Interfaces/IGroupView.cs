namespace Nav.Core.Interfaces;

/// <summary>
/// Everything a <see cref="GroupDoctrine"/> may LOOK AT about its group.
/// </summary>
/// <remarks>
/// Every query is O(1) against caches rebuilt at the head of the tick, except
/// <see cref="ReachableSpots"/>, which says so. A doctrine may call them inside a
/// loop without thinking about cost.
/// <para>
/// Reads only. A component handed this -- a diagnostic, an overlay, a metric --
/// is <em>structurally incapable</em> of changing the world, which is a stronger
/// statement than a comment saying it does not.
/// </para>
/// </remarks>
public interface IGroupView
{
    /// <summary>
    /// The tick this pass is running for -- identical across every pass and every
    /// group in the tick, which is what makes a doctrine's own cooldowns
    /// comparable from one call to the next.
    /// </summary>
    int CurrentTick { get; }

    /// <summary>
    /// The cell the order was aimed at, after any snap off impassable ground: the
    /// centre the parking ring surrounds, and not necessarily any member's goal.
    /// <see cref="FieldCost"/> is measured to the ring's innermost slot, which is
    /// this cell except where the ring was pushed clear of a doorway.
    /// </summary>
    int Destination { get; }

    /// <summary>The parking ring, innermost first.</summary>
    IReadOnlyList<int> Slots { get; }

    /// <summary>
    /// The members ON STATION, ascending: every member of the group except those
    /// away on an errand, which <see cref="Dispatched"/> lists instead. A snapshot
    /// taken when the pass began, so it does not reflect membership changed
    /// during the tick it is serving.
    /// </summary>
    /// <remarks>
    /// The split is what makes an errand safe for every existing pass: a pass
    /// that iterates this list never claims a slot for, meters, or reconciles a
    /// unit that is somewhere else on purpose. The mutating facets accept both
    /// lists; confinement is about the group, not about who is on station.
    /// <para>
    /// Sending a member away is not a movement doctrine's decision, which is why
    /// no facet here can do it. Membership above the movement layer decides,
    /// through <see cref="MovementSystem.Dispatch"/> and
    /// <see cref="MovementSystem.Recall(int)"/>; the formation only reports it.
    /// </para>
    /// </remarks>
    IReadOnlyList<int> Members { get; }

    /// <summary>
    /// Members away on an errand, ascending -- sent by
    /// <see cref="MovementSystem.Dispatch"/> and not yet recalled. Empty for a
    /// group nobody has dispatched from, which is every group today.
    /// </summary>
    IReadOnlyList<int> Dispatched { get; }

    /// <summary>
    /// Where a dispatched member is going, or -1 for a member on station. Reads
    /// for any member of the group, on station or not.
    /// </summary>
    int ErrandOf(int id);

    /// <summary>
    /// Every chokepoint on the MAP, not merely those between this group and its
    /// destination -- detected once for the system's life and shared by every
    /// group. A doctrine that wants the gate in its way filters these by
    /// <see cref="FieldCost"/>.
    /// </summary>
    IReadOnlyList<Chokepoint> Chokepoints { get; }

    /// <summary>Exact distance from a cell to the destination, or infinity.</summary>
    double FieldCost(int cell);

    /// <summary>
    /// Where the member is standing. An O(1) indexed read, and FIXED for the whole
    /// pass -- agents move after planning, never during a doctrine.
    /// </summary>
    int CellOf(int id);

    /// <summary>
    /// The cell the member is currently aimed at: its parking slot if it holds one,
    /// otherwise the ring's innermost slot it is walking toward. O(1), and LIVE --
    /// a <see cref="IGroupClaiming.ClaimSlot"/> earlier in this same pass shows up
    /// here.
    /// </summary>
    int GoalOf(int id);

    /// <summary>
    /// Consecutive replans that ended no nearer the goal -- failed attempts, not
    /// ticks waited, so a member sitting out a long backstop still reads 1. Reset
    /// to zero whenever a claim moves its goal.
    /// </summary>
    int StalledReplans(int id);

    /// <summary>
    /// False while the member is still QUEUED: walking toward the ring with no cell
    /// of its own, because group members claim on approach rather than at order
    /// time. It is the flag the fill-like-water claiming turns on.
    /// </summary>
    bool HasSlot(int id);

    /// <summary>
    /// Will this member change cell this tick?
    /// </summary>
    /// <remarks>
    /// False when anything is holding it where it is -- traffic ahead, a cell
    /// somebody else has reserved, a plan that has not landed yet. A doctrine gets
    /// the fact and not the cause, which is what keeps it on the right side of the
    /// seam: no plan, no reservation, no search, nothing it could use to break
    /// collision-freedom. The same kind of fact the viewer already reads as
    /// <c>AgentState.Waiting</c>.
    /// <para>
    /// It answers for the tick the pass is running in, like every other read here.
    /// </para>
    /// </remarks>
    bool IsMoving(int id);

    /// <summary>
    /// Is this cell some slot-holder's goal? <b>System-wide</b> -- it answers for
    /// every agent, including members of other groups.
    /// </summary>
    /// <remarks>
    /// <see cref="ClaimantOf"/> ranges over the same set, so the two can be used
    /// as the pair they read as (is it claimed; then by whom) and agree. They did
    /// not always: ClaimantOf once answered for this group alone, so with two
    /// concurrent groups the second said "nobody" about a cell the first called
    /// taken. A doctrine acting on the holder must still check it is one of its
    /// own <see cref="Members"/>; <see cref="IGroupClaiming"/> refuses an
    /// outsider regardless.
    /// </remarks>
    bool IsClaimed(int cell);

    /// <summary>
    /// The agent holding this cell as its slot, or -1. <b>System-wide</b>, like
    /// <see cref="IsClaimed"/>: the holder may belong to another group, in which
    /// case it is not in <see cref="Members"/>.
    /// </summary>
    int ClaimantOf(int cell);

    /// <summary>Is a unit parked on this cell (standing on its goal)?</summary>
    bool IsSettled(int cell);

    /// <summary>
    /// Is anybody standing here -- this group's members and every other agent
    /// alike? An O(1) hit against the tick's occupancy snapshot, which no doctrine
    /// pass can change, since nothing moves until planning is done.
    /// </summary>
    bool IsOccupied(int cell);

    /// <summary>
    /// Empty, unclaimed cells the given members can jointly WALK TO -- settled
    /// units are walls -- ordered by field distance then cell. One O(cells) sweep;
    /// call once per pass, not per member.
    /// </summary>
    /// <param name="fromMembers">The members to flood out from.</param>
    IReadOnlyList<int> ReachableSpots(IReadOnlyList<int> fromMembers);
}
