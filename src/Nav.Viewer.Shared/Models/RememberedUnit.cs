namespace Nav.Viewer.Models;

/// <summary>
/// One enemy unit as a side still has it written down: where it was last
/// actually seen, and when.
/// </summary>
/// <remarks>
/// A MEMORY, not a contact. Nothing here says the unit is still there -- the
/// gap between <see cref="Tick"/> and the tick being drawn is how wrong it may
/// be, and that gap is the only thing the viewer fades a ghost by.
/// <para>
/// <b>Three ints, because the seam only carries ints.</b> The tactics layer has
/// its own richer record of the same fact; this project cannot name it, and a
/// viewer that could would be a viewer holding a side's internals rather than a
/// side's picture. See <see cref="Interfaces.IVisibilityView"/>.
/// </para>
/// </remarks>
/// <param name="Agent">Whose unit was seen.</param>
/// <param name="Cell">Where it stood when it was last seen.</param>
/// <param name="Tick">The tick that sighting was taken on.</param>
public readonly record struct RememberedUnit(int Agent, int Cell, int Tick);
