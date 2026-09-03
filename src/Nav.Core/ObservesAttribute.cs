namespace Nav.Core;

/// <summary>
/// Marks a member as an INSTRUMENT: something a panel, a report or a test calls
/// to find out what the run currently believes, and which must leave the run
/// exactly as it found it.
/// </summary>
/// <remarks>
/// <b>Looking must not cause.</b> An instrument that mutates what it observes
/// moves the numbers this project decides on, and moves them only when somebody
/// is watching -- which is the one condition under which nobody can see it
/// happening. <see cref="Interfaces.IDistanceFieldView"/> exists because reading
/// a field through the ordinary member marks it most-recently-used and reorders
/// eviction; <see cref="Interfaces.IReservationView"/> is the same idiom, older.
/// <para>
/// Both of those are structural: the instrument is handed a type that has no
/// verb on it, so no discipline is required. This attribute is for what cannot
/// be made structural -- a query on a class that also has verbs -- and it does
/// nothing at runtime. It is read by the reachable-mutation walk in the test
/// projects, which follows calls from here and reports state it can reach.
/// </para>
/// <para>
/// Put it on the INTERFACE member where there is one. The walk expands to every
/// implementation it can see, so marking the contract marks the decorators and
/// the fakes with it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
public sealed class ObservesAttribute : Attribute;
