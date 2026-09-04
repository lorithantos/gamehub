namespace Nav.Core.Models;

/// <summary>
/// One fact about something the simulation is doing, as a line an instrument can
/// lay out.
/// </summary>
/// <param name="Group">
/// Which heading the row belongs under. Rows arrive already in group order, so a
/// panel renders headings by watching this change rather than by sorting.
/// </param>
/// <param name="Key">What the fact is called.</param>
/// <param name="Value">The fact, already formatted -- see the remarks.</param>
/// <param name="Note">
/// The sentence saying what the fact MEANS, or null where the fact speaks for
/// itself. Optional, so a row with nothing to explain is still three arguments.
/// </param>
/// <param name="Section">
/// Which LAYER answered: the caption above the group, empty until somebody
/// stamps it. See the remarks -- a producer must leave this alone.
/// </param>
/// <remarks>
/// <b>A LIST, NOT A DICTIONARY.</b> A dictionary has no contractual ordering, and
/// a panel whose rows reshuffle between frames is unreadable -- the eye tracks a
/// number by where it sits, so a value that moves reads as a value that changed.
/// Ordering is the whole reason this is a sequence: most useful first, grouped so
/// the headings mean something.
/// <para>
/// <b>Strings, not <c>object?</c>.</b> The only consumer formats for display, so
/// boxing a value in order to unbox it and call <c>ToString</c> buys nothing.
/// <c>object?</c> also invites production code to start reading these as data,
/// which is exactly what <see cref="Nav.Core.Interfaces.IDebugView"/> forbids.
/// </para>
/// <para>
/// And formatting AT THE SOURCE lets each type say how its own values should
/// read: "12 ticks until the gate lifts" rather than a raw tick number that only
/// means something next to a clock the reader has to find. A cell is "col,row"
/// here and not a pair, for the same reason -- the type decides what it says, the
/// panel decides how it looks.
/// </para>
/// <para>
/// <b>The fact and the gloss are two members, not one string.</b> They used to
/// be welded together with a dash, and a panel then had no way to show one
/// without the other: a column wide enough for "yes -- in the world and holding
/// its cell" is a column nothing else in the list needs, and a column sized for
/// everything else clipped that sentence mid-word. Split, <see cref="Value"/>
/// stays short enough to line up -- <c>yes</c>, <c>open</c>, <c>393</c>,
/// <c>25,15 (#760)</c> -- and <see cref="Note"/> is what an instrument shows on
/// demand, as a tooltip or a second line or not at all.
/// </para>
/// <para>
/// A panel may NOT recover the two by splitting the one. Cell text, a route and
/// a plan's own wording all carry dashes of their own, so a producer says which
/// half is which by constructing both, and a row with nothing to add leaves
/// <see cref="Note"/> null rather than repeating its value into it.
/// </para>
/// <para>
/// <b><see cref="Section"/> IS STAMPED BY THE INSTRUMENT, NEVER BY THE
/// PRODUCER.</b> It says which layer answered, and no producer is in a position
/// to know that: a class describing itself knows what it is, not what it is one
/// of, and the same rows can be merged by two instruments that would caption
/// them differently. So every producer leaves it empty and the instrument that
/// merged the rows writes it in with <c>row with { Section = ... }</c> as it
/// appends each block. That is the whole reason this is a fifth member rather
/// than a prefix on <see cref="Group"/> -- a producer would have had to learn
/// what layer it was in to write one.
/// </para>
/// </remarks>
public readonly record struct DebugRow(
    string Group,
    string Key,
    string Value,
    string? Note = null,
    string Section = "");
