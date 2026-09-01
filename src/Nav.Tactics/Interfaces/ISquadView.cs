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

    /// <summary>A member's health as a fraction, from the perception this pass was given.</summary>
    double HealthOf(int id);

    /// <summary>Cells hostile units occupy this tick, ascending. Empty on a quiet map.</summary>
    IReadOnlyList<int> Hostiles { get; }

    /// <summary>Cells where a unit standing there is repaired, ascending.</summary>
    IReadOnlyList<int> RepairPoints { get; }

    /// <summary>
    /// Straight-line octile distance between two cells, in step costs. A leash
    /// is measured with this: cheap, and blind to walls on purpose, because a
    /// leash is about how far a unit has strayed, not how far it has walked.
    /// </summary>
    double Distance(int cellA, int cellB);
}
