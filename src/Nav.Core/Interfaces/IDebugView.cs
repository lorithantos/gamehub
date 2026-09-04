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
    [Observes]
    IReadOnlyList<DebugRow> Describe();

    /// <summary>
    /// Every group name a row out of this view can carry, in a fixed order.
    /// </summary>
    /// <remarks>
    /// <b>What it CAN put a row under, not what it did.</b>
    /// <see cref="Describe"/> legitimately says less -- a corpse gets four rows
    /// and no more, a unit in nobody's squad gets one, an id that was never
    /// issued gets one -- and a panel wants the same headings in the same places
    /// whatever a given tick happens to contain. So the rows are a SUBSET of
    /// this, always, and a group named here that today's rows never reach is a
    /// conditional block rather than a fault.
    /// <para>
    /// <b>A SET, in a fixed order, and every implementation answers for
    /// itself.</b> That is what lets whoever composed an application lay a panel
    /// out by ASKING the views it is holding, rather than by writing the same
    /// names down a second time in a table that can only ever drift from them.
    /// What order to read one view's blocks against another's is still the
    /// composer's call -- taking this sequence as it stands is a decision it
    /// makes, not a claim made here.
    /// </para>
    /// <para>
    /// <b>The name before anybody renames it.</b> A panel holding two views that
    /// both say <c>Agent</c> shows the second as <c>Agent (2)</c>; what a view
    /// declares is what it emits, and what the panel finally printed is the
    /// panel's business.
    /// </para>
    /// <para>
    /// Answered without reading anything: a vocabulary is a fact about the code,
    /// so this costs nothing and cannot be wrong about a unit the way a cached
    /// row could.
    /// </para>
    /// </remarks>
    [Observes]
    IReadOnlyList<string> Groups { get; }
}
