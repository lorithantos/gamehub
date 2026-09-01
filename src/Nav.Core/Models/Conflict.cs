namespace Nav.Core.Models;

/// <param name="Kind">
/// Which overlap this is, and therefore how to read <paramref name="Cell"/> and
/// <paramref name="OtherCell"/>.
/// </param>
/// <param name="Tick">The tick the conflict begins at. For an edge conflict, the tick the move starts.</param>
/// <param name="AgentA">
/// For an edge conflict, the lower of the two agent ids. For a vertex conflict,
/// whichever of the two came first in the list handed to
/// <see cref="CollisionCheck.Inspect"/>.
/// </param>
/// <param name="AgentB">
/// The other agent. A colliding pair is reported once and once only -- there is
/// no mirrored <c>(B, A)</c> entry for the same tick.
/// </param>
/// <param name="Cell">The cell <paramref name="AgentA"/> is involved with.</param>
/// <param name="OtherCell">For an edge conflict, the cell they exchange with. Equal to <paramref name="Cell"/> for a vertex conflict.</param>
/// <remarks>
/// A conflict is always a pair, so three agents piled onto one cell come back as
/// two vertex conflicts, both naming the first of the three as
/// <paramref name="AgentA"/> -- not as one report of three.
/// </remarks>
public readonly record struct Conflict(
    ConflictKind Kind,
    int Tick,
    int AgentA,
    int AgentB,
    int Cell,
    int OtherCell);
