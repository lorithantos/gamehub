namespace Nav.Tactics.Interfaces;

/// <summary>
/// Everything a <see cref="SquadDoctrine"/> may LOOK AT about its squad.
/// </summary>
/// <remarks>
/// A snapshot taken when the pass began. A verb issued during the pass does not
/// show up here until the next one, which keeps a pass's reads consistent with
/// each other whatever order it asks in.
/// </remarks>
public interface ISquadView
{
    /// <summary>What the player calls the squad.</summary>
    string Name { get; }

    /// <summary>The tick this pass is running for.</summary>
    int CurrentTick { get; }

    /// <summary>
    /// Where the squad is stationed: the destination of its last group move, or
    /// -1 before any.
    /// </summary>
    int Anchor { get; }

    /// <summary>Every member, ascending, whatever each is doing.</summary>
    IReadOnlyList<int> Members { get; }

    /// <summary>
    /// The members currently away on an errand of their own, ascending. Still
    /// members: a group move takes them along, and <see cref="Members"/> lists
    /// them too.
    /// </summary>
    IReadOnlyList<int> Away { get; }

    /// <summary>Where the member is standing.</summary>
    int CellOf(int id);

    /// <summary>The cell the member is currently aimed at.</summary>
    int GoalOf(int id);

    /// <summary>Standing on its goal.</summary>
    bool HasArrived(int id);

    /// <summary>Where a member away on an errand is going, or -1 for one that is not.</summary>
    int ErrandOf(int id);
}
