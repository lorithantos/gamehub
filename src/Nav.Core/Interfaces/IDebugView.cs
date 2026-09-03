namespace Nav.Core.Interfaces;

/// <summary>
/// A DEBUG SURFACE. Something that can dump what it currently believes, as
/// ordered rows for a human to read through an instrument.
/// </summary>
/// <remarks>
/// <b>None of it is a contract.</b> What rows appear, what they are called, what
/// order they come in and how their values are worded may change with any commit
/// that changes what is worth looking at -- which is the point: an implementer
/// adds the row that would have explained last night's mystery without asking
/// anyone's permission.
/// <para>
/// <b>Nothing in production may branch on it, and no caller may parse a
/// <see cref="DebugRow.Value"/> back into a number.</b> A value here is prose
/// about a number, not the number; the moment something reads it back, the
/// wording is frozen and this stops being free to change. A fact production
/// needs is a fact that belongs on a real accessor.
/// </para>
/// <para>
/// The alternative this replaces is a bespoke record and a hand-written accessor
/// per fact, which is how private state ends up public in odd shapes: a
/// <c>Waiting</c> bool because somebody wanted to see a retry gate, and the
/// gate's actual tick still nowhere.
/// </para>
/// <para>
/// An implementation builds ON DEMAND and costs nothing when nobody is watching.
/// Anything that would have to be accumulated per tick to be reportable here does
/// not belong here.
/// </para>
/// </remarks>
public interface IDebugView
{
    /// <summary>
    /// Everything worth knowing right now, most useful first, in group order.
    /// </summary>
    /// <remarks>
    /// A fresh list each call, describing the instant it was asked. Never cached:
    /// a cache is one more thing that can go on describing a unit after it moved.
    /// </remarks>
    IReadOnlyList<DebugRow> Describe();
}
