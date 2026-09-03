namespace Nav.Tactics;

/// <summary>
/// What a squad does, tick by tick: the tactical layer, where guard, patrol and
/// retreat live.
/// </summary>
/// <remarks>
/// Distinct from a movement doctrine on purpose:
/// <list type="bullet">
/// <item><description>A MOVEMENT doctrine decides how a body of units settles
/// into a ring.</description></item>
/// <item><description>A SQUAD doctrine decides WHO goes WHERE — everyone to a
/// station, one member to the pad, that member back.</description></item>
/// </list>
/// <para>
/// It is handed <see cref="ISquadOps"/> and never sees a reservation, a plan or
/// a ring, so nothing written here can break collision-freedom.
/// </para>
/// <para>
/// Deterministic, and holding its own state, for the same reason the movement
/// doctrines are: replay is a test.
/// </para>
/// </remarks>
public abstract class SquadDoctrine
{
    /// <summary>One pass, once per tick, before the movement system ticks.</summary>
    /// <param name="ops">
    /// The squad as this pass sees it: reads are a snapshot taken when the pass
    /// began, verbs take effect at the next tick.
    /// </param>
    public abstract void Advance(ISquadOps ops);
}
