using System.Diagnostics.CodeAnalysis;

namespace Nav.Core.Interfaces;

/// <summary>
/// What an instrument may ASK of a field source. One question, and no way from
/// here to make a field exist.
/// </summary>
/// <remarks>
/// <b>A source keeping K fields decides what to drop from the order it was asked
/// in.</b> An instrument reading a field through the mutating member becomes part
/// of that decision: the field it looked at survives, a colder one is dropped in
/// its place, and the drop is paid for later as a rebuild. The field and node
/// counts this project decides things on are then a function of whether anybody
/// was watching, and an instrument that moves the number it reports is measuring
/// itself.
/// <para>
/// <b>A type rather than a convention, because a convention is what failed.</b>
/// The peek used to sit on <see cref="IDistanceFieldSource"/> beside
/// <c>For</c>, and it worked only for as long as every caller chose the polite
/// member -- while <c>For</c> reads at the call site exactly like the question it
/// is not. Handing an instrument a reference that cannot build is the same
/// guarantee without the discipline.
/// </para>
/// <para>
/// <see cref="IReservationView"/> exists for this reason and is worth reading
/// beside this: a planner gets the questions and none of the verbs, so no amount
/// of asking can book, release or advance anything.
/// </para>
/// <para>
/// It is the source's own <see cref="IDistanceFieldSource.View"/> and not a
/// snapshot: what it answers moves with the run, which is what an instrument
/// wants. What it CANNOT do is move the run.
/// </para>
/// </remarks>
public interface IDistanceFieldView
{
    /// <summary>
    /// The field for this destination IF the source already holds one: no build,
    /// and no mark of use. False means nothing is held, and the caller is given
    /// no field rather than an empty one.
    /// </summary>
    /// <remarks>
    /// It is not a fast path and not an optimisation. A caller that NEEDS the
    /// field wants it built and wants the ask to count, which is
    /// <see cref="IDistanceFieldSource.For"/> and is not reachable from here.
    /// <para>
    /// A decorator forwards this to what it wraps. Answering false because the
    /// wrapper keeps nothing of its own would report "not held" about a field that
    /// is held -- a lie in the one direction an instrument cannot detect.
    /// </para>
    /// </remarks>
    /// <param name="destination">The cell the wanted field is keyed by.</param>
    /// <param name="field">The held field, or null.</param>
    [Observes]
    bool TryPeek(int destination, [NotNullWhen(true)] out DistanceField? field);
}
