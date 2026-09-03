namespace Nav.Core.Interfaces;

/// <summary>
/// The power to assign and withdraw parking slots.
/// </summary>
/// <remarks>
/// Separated from <see cref="IGroupPacing"/> because they are different
/// authorities and only one doctrine needs both. Metering paces a queue through a
/// gate and has no business deciding where anyone parks; a pass handed only
/// <see cref="IGroupPacing"/> cannot claim, and the compiler enforces it rather
/// than a reviewer.
/// <para>
/// Confined to the group. Every method refuses an id that is not one of this
/// group's <see cref="IGroupView.Members"/>, before it changes anything, so a
/// doctrine handed one group's seam cannot reach another group's unit even by
/// naming it. The check is the seam's, not the doctrine's to remember.
/// </para>
/// </remarks>
public interface IGroupClaiming
{
    /// <summary>
    /// Gives the member this cell as its parking slot: goal, claim, wake.
    /// Idempotent when the goal already matches.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="id"/> is not one of this group's members.
    /// </exception>
    void ClaimSlot(int id, int cell);

    /// <summary>
    /// Releases a member's claim and sends it back to the queue -- it will claim
    /// again on approach, or reconcile in its turn. A no-op on a member that holds
    /// no slot.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="id"/> is not one of this group's members.
    /// </exception>
    void ReleaseSlot(int id);

    /// <summary>
    /// Stops the member where it stands, and makes that cell its slot: goal,
    /// claim, and a plan that goes nowhere -- <b>if the cell can be held</b>.
    /// Refuses, changing nothing, when another unit's plan crosses the cell; the
    /// member then keeps the plan it had.
    /// </summary>
    /// <remarks>
    /// The only way a doctrine can stop a unit, and the reason doing so cannot
    /// cause a collision. Both alternatives are wrong:
    /// <list type="bullet">
    /// <item><description><see cref="ClaimSlot"/> sets the goal but does not stop
    /// the unit — it walks off along its committed plan and comes back,
    /// forever.</description></item>
    /// <item><description>Discarding the plan and holding the cell unasked stands
    /// it on a cell somebody else is committed to walk through.</description></item>
    /// </list>
    /// <para>This asks.</para>
    /// </remarks>
    /// <returns>True if the member is now parked; false if it may not stay.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="id"/> is not one of this group's members.
    /// </exception>
    bool Park(int id);
}
