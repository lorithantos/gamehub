using Nav.Core;

namespace Nav.Viewer.Interfaces;

/// <summary>
/// How hurt each unit is, as a fraction and nothing else: the one number a bar
/// over a unit's head needs.
/// </summary>
/// <remarks>
/// <b>Named for the contract and not for whoever satisfies it</b>, exactly as
/// <see cref="IVisibilityView"/> and <see cref="IWorldDebugView"/> are. This
/// project references Nav.Core alone, so nothing here can name a world, a kit or
/// a perception -- and a name that pointed at one anyway would claim a
/// relationship the compiler has been set up to forbid.
/// <para>
/// <b>A NEW crossing rather than a widening of either existing one, and that is
/// a decision worth overturning cheaply if it reads wrong.</b> Health could not
/// ride on <see cref="IVisibilityView"/>: everything there is in CELLS and SIDES
/// so that the whole interface is ints about the grid, and a per-agent fraction
/// is neither. It could not ride on <see cref="IWorldDebugView"/> either -- that
/// seam carries STRINGS on purpose, because its only consumer formats them for a
/// panel, and a bar cannot be drawn from "0.42". So the number comes across on
/// its own interface, one member wide, and the two existing seams keep the
/// shapes they were given.
/// </para>
/// <para>
/// <b>A FRACTION, not points.</b> What is drawn is a proportion of a bar, so
/// what crosses is a proportion. Maximum health is a tactics fact with a kit and
/// an armour behind it, and a viewer that took two numbers and divided them
/// would be doing the tactics layer's arithmetic with none of its vocabulary.
/// </para>
/// <para>
/// <b>It observes and must cause nothing.</b> Every member is
/// <see cref="ObservesAttribute"/>, on the interface rather than on an
/// implementation, so the reachable-mutation walk marks every implementer and
/// every decorator with it. An implementation that settled a tick's damage on
/// the way past would move the run on exactly the frames somebody was watching
/// it.
/// </para>
/// <para>
/// <b>Answers are as of the last clock edge and are asked once per unit per
/// frame.</b> The viewer holds nothing back between frames, so an implementation
/// may compute per call.
/// </para>
/// </remarks>
public interface IHealthView
{
    /// <summary>
    /// How much of <paramref name="agent"/> is left: 1 for undamaged, 0 for
    /// destroyed.
    /// </summary>
    /// <remarks>
    /// Clamped to 0..1 by whoever answers, and the viewer clamps it again
    /// before drawing -- a bar is a length and a length outside the track is a
    /// picture nobody can read.
    /// </remarks>
    /// <param name="agent">
    /// Any int; one nobody is tracking answers 1. That is the same answer the
    /// layer below already gives for an unknown id, so a viewer and a doctrine
    /// asking about the same stranger hear the same thing rather than two
    /// conventions that have to be kept in step.
    /// </param>
    [Observes]
    float HealthOf(int agent);
}
