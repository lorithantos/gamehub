namespace Nav.Core.Models;

/// <summary>
/// Which of the two ways a pair of agents overlapped.
/// </summary>
/// <remarks>
/// These are the only kinds <see cref="CollisionCheck"/> reports, and
/// <see cref="Edge"/> is the one an occupancy-only check never sees -- see the
/// remarks on <see cref="CollisionCheck"/>.
/// </remarks>
public enum ConflictKind
{
    /// <summary>Two agents on one cell at one tick.</summary>
    Vertex,

    /// <summary>Two agents exchanging cells across one tick, passing through each other.</summary>
    Edge,
}
