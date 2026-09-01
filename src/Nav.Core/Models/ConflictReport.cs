namespace Nav.Core.Models;

/// <param name="Conflicts">
/// Every overlap found, in the tick order they were discovered. Empty is the
/// good case; <see cref="Clean"/> is the way to ask.
/// </param>
/// <param name="AgentTicksChecked">
/// How much was actually looked at. A clean report over nothing is not evidence,
/// so the count travels with the verdict.
/// </param>
public sealed record ConflictReport(IReadOnlyList<Conflict> Conflicts, int AgentTicksChecked)
{
    /// <summary>
    /// No conflicts of <em>either</em> kind. This is the gate the multi-agent
    /// tests assert on -- and it is true of a report that examined nothing, which
    /// is why <see cref="AgentTicksChecked"/> is read alongside it.
    /// </summary>
    public bool Clean => Conflicts.Count == 0;

    /// <summary>
    /// How many conflicts of one kind, so a test can say <em>which</em> kind it
    /// expected rather than only that something went wrong -- an edge conflict
    /// counted as a vertex one would otherwise pass.
    /// </summary>
    public int CountOf(ConflictKind kind) => Conflicts.Count(c => c.Kind == kind);
}
