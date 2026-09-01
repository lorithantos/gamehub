namespace Nav.Core;

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
    /// Member ids, ascending. A snapshot taken when the pass began, so it does not
    /// reflect membership changed during the tick it is serving.
    /// </summary>
    IReadOnlyList<int> Members { get; }

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
    /// Is this cell some slot-holder's goal? <b>System-wide</b> -- it answers for
    /// every agent, including members of other groups.
    /// </summary>
    /// <remarks>
    /// The scope difference against <see cref="ClaimantOf"/> is deliberate now and
    /// was an accident before: the two were written as a matched pair (ask whether
    /// a cell is claimed, then ask who claimed it) while ranging over different
    /// sets, so with two concurrent groups the second answered "nobody" about a
    /// cell the first called taken. Read both scopes before pairing them.
    /// </remarks>
    bool IsClaimed(int cell);

    /// <summary>
    /// The member of <see cref="Members"/> holding this cell as its slot, or -1.
    /// <b>Group-local</b> -- an agent outside this group holding the cell answers
    /// -1 here while <see cref="IsClaimed"/> answers true.
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

/// <summary>
/// The power to assign and withdraw parking slots.
/// </summary>
/// <remarks>
/// Separated from <see cref="IGroupPacing"/> because they are different
/// authorities and only one doctrine needs both. Metering paces a queue through a
/// gate and has no business deciding where anyone parks; a pass handed only
/// <see cref="IGroupPacing"/> cannot claim, and the compiler enforces it rather
/// than a reviewer.
/// </remarks>
public interface IGroupClaiming
{
    /// <summary>
    /// Gives the member this cell as its parking slot: goal, claim, wake.
    /// Idempotent when the goal already matches.
    /// </summary>
    void ClaimSlot(int id, int cell);

    /// <summary>
    /// Releases a member's claim and sends it back to the queue -- it will claim
    /// again on approach, or reconcile in its turn. A no-op on a member that holds
    /// no slot.
    /// </summary>
    void ReleaseSlot(int id);
}

/// <summary>
/// The power to change WHEN a member plans, without changing where it is going.
/// </summary>
public interface IGroupPacing
{
    /// <summary>Lets a gated member plan again now.</summary>
    void Wake(int id);

    /// <summary>
    /// Keeps the member standing, quietly: no goal change, no search, no stall --
    /// just no replanning for a few ticks. Refresh it each tick to hold longer; a
    /// lapsed hold degrades to planning, never to frozen.
    /// </summary>
    /// <param name="id">The member to hold.</param>
    /// <param name="ticks">How long, in ticks. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticks"/> is not positive.</exception>
    void Hold(int id, int ticks);
}

/// <summary>
/// What a <see cref="GroupDoctrine"/> is handed: the whole seam, as one object.
/// </summary>
/// <remarks>
/// The three facets exist so a consumer can ask for less. This composite exists
/// because the doctrine entry point has to be one parameter, and because the
/// implementation is a single coherent object -- one class serving three
/// contracts, which is the point rather than an accident.
/// <para>
/// <b>What is NOT here is the guarantee.</b> There is no plan, no reservation, no
/// search and no grid. A doctrine cannot reach the collision layer because this
/// contract does not mention it, so no doctrine -- including one written outside
/// this assembly -- can break collision-freedom. The implementing class is
/// internal, so a third party sees these contracts and no concrete type at all.
/// </para>
/// </remarks>
public interface IGroupOps : IGroupView, IGroupClaiming, IGroupPacing;
