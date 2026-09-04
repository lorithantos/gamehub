namespace Nav.Core;

/// <summary>
/// Marks a member as an INSTRUMENT: something a panel, a report or a test calls
/// to find out what the run currently believes, and which must leave everything
/// it can reach exactly as it found it.
/// </summary>
/// <remarks>
/// <b>Reading changes nothing, full stop.</b> An instrument that mutates what it
/// observes moves the numbers this project decides on, and moves them only when
/// somebody is watching -- which is the one condition under which nobody can see
/// it happening. <see cref="Interfaces.IDistanceFieldView"/> exists because
/// reading a field through the ordinary member marks it most-recently-used and
/// reorders eviction; <see cref="Interfaces.IReservationView"/> is the same
/// idiom, older.
/// <para>
/// Both of those are structural: the instrument is handed a type that has no
/// verb on it, so no discipline is required. This attribute is for what cannot
/// be made structural -- a query on a class that also has verbs -- and it does
/// nothing at runtime. It is read by the reachable-mutation walk in the test
/// projects, which follows calls from here and reports state it can reach.
/// </para>
/// <para>
/// <b>The rule was narrower and is not any more.</b> It used to say "does not
/// change the SIMULATION", which left a marked member free to move state the
/// simulation never sees. The viewer's input accumulator sat in exactly that
/// gap: its <c>Snapshot</c> drained the frame's key and button presses on the
/// way out, so a second caller of a member named like a look got an empty frame
/// and nothing anywhere warned them. Nothing in the tick moved, and the audit
/// was right to stay green under the old rule -- which is the problem with it.
/// </para>
/// <para>
/// The looser rule was rejected because a clean audit has to mean an instrument
/// CANNOT MOVE ANYTHING. Read the other way it means only that a human who
/// already knows which state counts would have approved of what it moved, and
/// then every green report has to be read by that human before it is worth
/// anything. So the rule now covers what it did not: the member's own class, a
/// caller's buffer, a cursor, a cache -- any state the walk can reach, whether
/// or not a simulation is downstream of it. A member that must move something is
/// not an instrument. Split it, mark the read, and let the verb take the verb's
/// name.
/// </para>
/// <para>
/// Put it on the INTERFACE member where there is one. The walk expands to every
/// implementation it can see, so marking the contract marks the decorators and
/// the fakes with it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
public sealed class ObservesAttribute : Attribute;
