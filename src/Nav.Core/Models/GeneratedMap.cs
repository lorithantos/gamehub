namespace Nav.Core.Models;

/// <summary>
/// A passage the generator cut, and two different facts about how much it matters.
/// </summary>
/// <remarks>
/// <b>Separating and constricting are not the same property</b>, so both numbers
/// are kept:
/// <list type="bullet">
/// <item><description><see cref="SmallerSide"/> — does filling this break the map
/// in two? Binary and exact.</description></item>
/// <item><description><see cref="Detour"/> — how much worse is the way round?
/// Graded, and the one a doctrine cares about.</description></item>
/// </list>
/// <para>A cut has both: infinite detour and a real smaller side.</para>
/// <para>
/// <b>Think of the Panama Canal.</b> Closing it strands nobody, so by the
/// separating measure it is worth nothing — and it is the most important
/// chokepoint in the hemisphere, because the way round is eight thousand miles.
/// </para>
/// <para>
/// A detector scored only on what it disconnects would be marked correct for
/// ignoring it.
/// </para>
/// </remarks>
/// <param name="Cell">A cell in the passage — the corner of what was carved.</param>
/// <param name="Cells">
/// Every cell that would be filled to close this passage, so a detector can be
/// scored on position rather than only on count.
/// <para>
/// Matching <see cref="Cell"/> alone would be unfair: the corner of an L-shaped
/// passage need not be its narrowest part, so a detector naming the throat would
/// be marked wrong for not naming the elbow.
/// </para>
/// </param>
/// <param name="Width">How wide the passage was cut, in cells.</param>
/// <param name="SmallerSide">
/// Open cells stranded if this passage were filled, counting the smaller of the two
/// sides. Zero when filling it strands nobody because a loop goes round.
/// </param>
/// <param name="Detour">
/// Extra step cost between the two places this passage joins, if it were filled and
/// traffic had to go round. <see cref="double.PositiveInfinity"/> when there is no
/// way round, which is exactly the case where <see cref="SmallerSide"/> is positive.
/// A small value means the passage is redundant; a large one means it is a gate
/// whether or not it separates anything.
/// </param>
public sealed record KnownGate(
    int Cell, IReadOnlyList<int> Cells, int Width, int SmallerSide, double Detour);

/// <summary>
/// A generated map together with the answers about it.
/// </summary>
/// <remarks>
/// The answers are the point. A detector run against a downloaded map can only
/// be eyeballed: nobody knows how many gates that map really has, so "it found
/// sixteen" is not a result.
/// <para>
/// Here the passages were CUT, so their positions, widths, separating power and
/// detour cost are known by construction — and a detector can be scored rather
/// than admired.
/// </para>
/// </remarks>
/// <param name="Grid">The map itself.</param>
/// <param name="MapText">The same map in Moving AI octile format, ready to write out.</param>
/// <param name="Gates">Every passage cut, in ascending cell order.</param>
public sealed record GeneratedMap(Grid Grid, string MapText, IReadOnlyList<KnownGate> Gates);
