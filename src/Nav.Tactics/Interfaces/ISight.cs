namespace Nav.Tactics.Interfaces;

/// <summary>
/// Whether a watcher standing on one cell can make out another.
/// </summary>
/// <remarks>
/// The seam fog is built on, and the only reason fog is not hard-wired to a
/// radius. <see cref="RadiusSight"/> ships: distance and nothing else, so a
/// unit is seen through a wall. That is knowingly wrong and visibly wrong,
/// which is the trade — a line-of-sight implementation drops in here without a
/// caller changing, and the difference between the two is then measurable
/// rather than argued.
/// <para>
/// Deliberately NOT given the watcher's id. Sight is a property of ground and
/// reach, not of who is looking; a rule that needs to know WHICH unit is
/// watching is a rule about the unit, and belongs in the sight range this is
/// handed rather than in here.
/// </para>
/// <para>
/// Must be pure and must not depend on the tick. The sighting sweep runs it
/// once per side per candidate with no ordering between sides, which is what
/// lets the sweep be split across threads later.
/// </para>
/// </remarks>
public interface ISight
{
    /// <summary>
    /// Whether a watcher on <paramref name="from"/> with this much reach can see
    /// <paramref name="to"/>. A watcher always sees the cell it stands on, whatever
    /// the range, so that a blind unit is still aware of its own ground.
    /// </summary>
    /// <param name="from">Where the watcher stands.</param>
    /// <param name="to">The cell being looked at.</param>
    /// <param name="range">How far the watcher can see, in octile step cost. Zero is blind.</param>
    bool CanSee(int from, int to, double range);
}
