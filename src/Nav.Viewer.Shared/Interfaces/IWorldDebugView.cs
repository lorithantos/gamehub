using Nav.Core;

namespace Nav.Viewer.Interfaces;

/// <summary>
/// A debug surface that also has UNITS in it: its own rows, and one agent's rows
/// on request.
/// </summary>
/// <remarks>
/// <b>Named for the contract and not for whoever satisfies it, because the
/// viewer must be able to hold several of these and know nothing about any of
/// them.</b> What a panel gets handed is a list of <see cref="IDebugView"/>; the
/// ones that can also answer for a single unit are these. Whoever composes the
/// application decides what is in that list -- a movement layer, a tactics
/// layer, whatever gets written next -- and the viewer merges the rows and
/// renders them without learning what any of the sources are.
/// <para>
/// <b>A name that said which implementer it was for would be the fault this
/// interface exists to avoid.</b> This project references Nav.Core alone, so
/// nothing here can name a kit, a sighting or a world -- and a name that
/// pointed at one anyway would claim a relationship the compiler has been set
/// up to forbid. <see cref="IDistanceFieldView"/> and
/// <see cref="IReservationView"/> are the idiom this follows: both are named
/// for what they promise a holder, and neither for the one class that happens
/// to be handing them out.
/// </para>
/// <para>
/// <b>It derives from <see cref="IDebugView"/> rather than sitting beside
/// one.</b> A source already answers questions about itself as a whole -- rates,
/// settings, when it last moved -- and that is exactly what
/// <see cref="IDebugView.Describe"/> is for. A second way to ask the same kind
/// of question would leave a panel choosing between two shapes of one answer,
/// and would put this type outside the list it is meant to sit in.
/// </para>
/// <para>
/// <see cref="MovementSystem"/> is the shape being generalised: it is an
/// <see cref="IDebugView"/> in its own right and hands out a per-agent one
/// through <see cref="MovementSystem.DebugFor"/>. It does not implement this
/// yet -- Nav.Core cannot see this project -- and nothing about that is a
/// problem, because a caller that has one can adapt it in a line.
/// </para>
/// <para>
/// Everything <see cref="IDebugView"/> forbids applies to both halves: nothing
/// in production may branch on a row, and no caller may read a
/// <see cref="DebugRow.Value"/> back into a number.
/// </para>
/// </remarks>
public interface IWorldDebugView : IDebugView
{
    /// <summary>
    /// What this source knows about ONE unit, for a human reading an instrument.
    /// Any id is answered, including one it has never heard of.
    /// </summary>
    /// <remarks>
    /// Built on demand and costing nothing while nobody is watching: what comes
    /// back holds an id and reads state only when
    /// <see cref="IDebugView.Describe"/> is called on it.
    /// <para>
    /// An instrument pointed at the wrong unit should say so, not throw at the
    /// panel -- the same rule <see cref="MovementSystem.DebugFor"/> keeps.
    /// </para>
    /// </remarks>
    /// <param name="agent">Who to describe. Any int; see the remarks.</param>
    [Observes]
    IDebugView DebugFor(int agent);
}
