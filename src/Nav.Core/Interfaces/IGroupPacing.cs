namespace Nav.Core.Interfaces;

/// <summary>
/// The power to change WHEN a member plans, without changing where it is going.
/// Confined to the group the same way <see cref="IGroupClaiming"/> is: an id
/// outside <see cref="IGroupView.Members"/> is refused before anything changes.
/// </summary>
public interface IGroupPacing
{
    /// <summary>Lets a gated member plan again now.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="id"/> is not one of this group's members.
    /// </exception>
    void Wake(int id);

    /// <summary>
    /// Keeps the member standing, quietly: no goal change, no search, no stall --
    /// just no replanning for a few ticks. Refresh it each tick to hold longer; a
    /// lapsed hold degrades to planning, never to frozen.
    /// </summary>
    /// <param name="id">The member to hold.</param>
    /// <param name="ticks">How long, in ticks. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ticks"/> is not positive, or <paramref name="id"/> is not
    /// one of this group's members.
    /// </exception>
    void Hold(int id, int ticks);
}
