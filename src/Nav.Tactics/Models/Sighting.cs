namespace Nav.Tactics.Models;

/// <summary>
/// What one side knows about one enemy unit: where it was last actually seen,
/// and when.
/// </summary>
/// <remarks>
/// A sighting is a MEMORY, not an observation. <see cref="Tick"/> is how old it
/// is, and there is deliberately no "is it stale" flag: how many ticks of age
/// make a sighting worth acting on is a doctrine's decision, and a patrol and a
/// guard have every reason to answer differently. Compare against the view's
/// own tick.
/// <para>
/// A sighting whose <see cref="Tick"/> is the current one is a unit being
/// watched right now. Its cell is also in <c>Hostiles</c>, which is the split:
/// <c>Hostiles</c> is what I can see, this is what I know.
/// </para>
/// <para>
/// Only real units are remembered. A scripted threat has no id to hang a memory
/// on, so it appears while it is in sight and is not missed when it goes.
/// </para>
/// </remarks>
/// <param name="Agent">Whose unit was seen.</param>
/// <param name="Cell">Where it stood when it was last seen.</param>
/// <param name="Tick">The tick that sighting was taken on.</param>
public readonly record struct Sighting(int Agent, int Cell, int Tick);
