namespace Nav.Core.Interfaces;

/// <summary>
/// A world that can be told to play a tick: the map, the board everything
/// stands on, and the one call that advances the run by one.
/// </summary>
/// <remarks>
/// <b>The tick is a decision, and this is what hands it over intact.</b> What
/// runs inside <see cref="Step"/> and in what order -- doctrine before the
/// board moves, the world settled after it, whoever fell taken off the board
/// after the settle rather than during it -- is a design the world owns the way
/// it owns its map. A caller that wrote those lines for itself would be a
/// second simulation that agrees with the first by coincidence, and on the day
/// the two stopped agreeing, the run watched live and the run narrated into a
/// trace would quietly be different runs.
/// <para>
/// <b>It lives in Nav.Core, and the placement is the point.</b> Every member
/// names a Nav.Core type, and a viewer references Nav.Core alone -- that
/// absence is the seam, kept by the compiler rather than by review. The same
/// interface declared up beside an implementation could never be held down
/// here: a world library references the tactics layer, so holding its interface
/// would drag a whole tactics world across the seam with it. Declared here, a
/// host can drive a world it cannot name a kit, a sighting or a squad from.
/// </para>
/// <para>
/// <b>There is no debug or observation surface on it, and none may be added.</b>
/// This type exists to CAUSE. <see cref="IDebugView"/> and the per-unit surfaces
/// built on it exist to OBSERVE, and the two stay separate types on purpose: a
/// holder that needs to read what happened takes a debug view alongside this
/// one, or asks the concrete world, whose per-tick facts are properties read
/// after <see cref="Step"/> has returned. Merged, every implementer of a clock
/// would owe an instrument as well, and every panel would be one cast away from
/// driving the run it is drawing.
/// </para>
/// <para>
/// <b>Everything but the tick is already standing.</b> The map, the sides, their
/// kits and their doctrine are composed before a holder is handed one, so there
/// is no start, no load and no ready state here: what can be held can be
/// stepped.
/// </para>
/// </remarks>
public interface IWorld
{
    /// <summary>The map every cell index in this world is an index into.</summary>
    Grid Grid { get; }

    /// <summary>
    /// The board: the movement system every side moves on, with whatever is
    /// standing already placed on it.
    /// </summary>
    /// <remarks>
    /// The board itself and not a copy of it, so what a holder draws between
    /// ticks is the run rather than a picture of one taken earlier.
    /// </remarks>
    MovementSystem Board { get; }

    /// <summary>
    /// Plays one tick: every side's doctrine pass, then the board, then the
    /// world settled against it, then whoever fell taken off.
    /// </summary>
    /// <remarks>
    /// <b>Void, deliberately.</b> What happened is read afterwards off the world
    /// itself rather than handed back as a result here, so a world can start
    /// reporting one more fact worth narrating without every holder of this
    /// interface having to be built again around a wider record.
    /// </remarks>
    /// <param name="tick">
    /// Which tick this is, counted from zero and ascending by one. Anything a
    /// world schedules against a clock is scheduled against this number, so a
    /// caller that skips one skips whatever was due on it.
    /// </param>
    void Step(int tick);
}
