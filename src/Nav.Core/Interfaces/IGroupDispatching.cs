namespace Nav.Core.Interfaces;

/// <summary>
/// The power to send a member away on an errand of its own, and to call it back.
/// </summary>
/// <remarks>
/// An errand is a goal, not a change of membership. The member stays in its
/// group -- the mutators still accept it, the confinement guard still covers it,
/// and an order that re-groups it clears the errand like any other goal -- but
/// while it is away it is listed under <see cref="IGroupView.Dispatched"/> rather
/// than <see cref="IGroupView.Members"/>, so no pass claims a slot for it, meters
/// it, or reconciles it. This is what lets a guard send one damaged unit to the
/// repair pad while the rest hold the ring, and take it back when it returns.
/// <para>
/// Confined to the group, like every other mutating facet: an id outside the
/// group is refused before anything changes.
/// </para>
/// </remarks>
public interface IGroupDispatching
{
    /// <summary>
    /// Sends the member to <paramref name="destination"/> on its own. Any slot
    /// it held is released first, so the ring sees the space at once.
    /// </summary>
    /// <param name="id">The member to send.</param>
    /// <param name="destination">Where to. Must be a passable cell; it is not snapped.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="id"/> is not one of this group's members, or
    /// <paramref name="destination"/> is off the map or impassable.
    /// </exception>
    void Dispatch(int id, int destination);

    /// <summary>
    /// Ends the member's errand: it is aimed back at the ring, holds no slot, and
    /// claims one on approach exactly as a freshly ordered member does. A no-op
    /// on a member that is not dispatched.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="id"/> is not one of this group's members.
    /// </exception>
    void Recall(int id);
}
