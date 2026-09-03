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
/// </remarks>
public readonly record struct DebugRow(string Group, string Key, string Value);
