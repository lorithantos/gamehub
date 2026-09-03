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

    /// <summary>
    /// A member's rank, from the perception this pass was given. 0 for a unit
    /// that has proved nothing.
    /// </summary>
    int RankOf(int id);

    /// <summary>Cells hostile units occupy this tick, ascending. Empty on a quiet map.</summary>
    /// <remarks>
    /// Under fog, what this squad's side can SEE this tick. An enemy that walks
    /// out of sight leaves this list at once and stays in
    /// <see cref="Sightings"/>.
    /// </remarks>
    IReadOnlyList<int> Hostiles { get; }

    /// <summary>
    /// What this squad's side knows about enemy units it has seen: where each
    /// was last seen and when, by agent, ascending. Empty without fog.
    /// </summary>
    /// <remarks>
    /// A doctrine reading only <see cref="Hostiles"/> forgets an enemy the
    /// instant it loses sight of it, which is what every doctrine written before
    /// fog does. Reading this is how one holds a line against, chases, or is
    /// baited by something it can no longer see.
    /// <para>
    /// How old a sighting may be and still be worth acting on is the doctrine's
    /// own decision — compare against <see cref="CurrentTick"/>. There is
    /// deliberately no answer to that here, because a patrol and a guard have
    /// every reason to give different ones.
    /// </para>
    /// </remarks>
    IReadOnlyList<Sighting> Sightings { get; }

    /// <summary>Cells where a unit standing there is repaired, ascending.</summary>
    IReadOnlyList<int> RepairPoints { get; }

    /// <summary>
    /// Straight-line octile distance between two cells, in step costs. Blind to
    /// walls on purpose: it answers how far apart two things are, not how far
    /// anyone would have to walk.
    /// </summary>
    double Distance(int cellA, int cellB);

    /// <summary>The cell's column.</summary>
    /// <remarks>
    /// With <see cref="RowOf"/>, enough for a doctrine to do its own geometry --
    /// a leash measured to a patrol's route rather than to one point, a facing,
    /// a flank. <see cref="Distance"/> answers the common case; these two are
    /// there so a doctrine is not limited to the questions this interface
    /// happened to anticipate.
    /// </remarks>
    int ColumnOf(int cell);

    /// <summary>The cell's row.</summary>
    int RowOf(int cell);
}
