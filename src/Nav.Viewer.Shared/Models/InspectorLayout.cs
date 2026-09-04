namespace Nav.Viewer.Models;

/// <summary>
/// The inspector's sections, and the mechanism that lays a panel's blocks out in
/// the order an <see cref="InspectorArrangement"/> asks for.
/// </summary>
/// <remarks>
/// <b>MECHANISM HERE, POLICY IN THE COMPOSITION ROOT.</b> This class can order
/// blocks; it does not know what order anyone wants. The arrangement is handed
/// in, from the host that composed the application, because that host is the one
/// place entitled to see both halves of the seam at once -- see
/// <see cref="InspectorArrangement"/> for what a list of group names written
/// down here would have cost.
/// <para>
/// <b>A SECTION SAYS WHICH LAYER ANSWERED, and that is why the section names
/// stay.</b> They come from PROVENANCE -- the movement layer answered, a
/// supplied source answered, the viewer answered about itself -- which this
/// project knows without naming anybody's vocabulary, and
/// <see cref="ViewerApp"/> stamps them onto the blocks as it merges them.
/// Deciding a section from the GROUP NAME instead would hand a stranger the
/// movement layer's caption: a source is somebody else's code and may name a
/// group anything, which is the whole reason the de-collision machinery exists.
/// </para>
/// <para>
/// So an arrangement is ORDER, and a declaration of what each layer is expected
/// to say. A group it has never heard of is a normal case rather than an error:
/// it keeps the section of the layer that produced it and sorts after every
/// group the arrangement does name, in the order it arrived.
/// </para>
/// </remarks>
public static class InspectorLayout
{
    /// <summary>The section for rows the movement layer answered.</summary>
    public const string MovementSection = "Movement";

    /// <summary>The section for rows a supplied source answered.</summary>
    public const string TacticsSection = "Tactics";

    /// <summary>The section for rows the viewer answered about itself.</summary>
    public const string ViewerSection = "Viewer";

    /// <summary>
    /// The viewer's own group: what got drawn, what got selected, and which
    /// source refused to answer.
    /// </summary>
    public const string SourcesGroup = "Sources";

    /// <summary>The viewer's other group: the keyboard, read off the keymap.</summary>
    public const string ControlsGroup = "Controls";

    /// <summary>
    /// The same rows, laid out: sections in the arrangement's order, groups in
    /// its order inside them, and the rows of a group untouched.
    /// </summary>
    /// <remarks>
    /// <b>Blocks move, rows do not.</b> A group is gathered whole and the group
    /// is what gets ordered, so a producer's own sequence inside a group survives
    /// exactly as it was written -- which matters, because the eye tracks a
    /// number by where it sits and a producer ordered its rows most useful first.
    /// <para>
    /// A group whose rows arrive in two runs comes out as one block. That cannot
    /// happen from the sources shipped today, and coalescing beats the
    /// alternative: a host prints a heading when the group changes, so two runs
    /// would print two identical headings.
    /// </para>
    /// <para>
    /// With <see cref="InspectorArrangement.ArrivalOrder"/> every heading ranks
    /// the same and the stable sort is a no-op, so the rows come back in the
    /// order the producers wrote them.
    /// </para>
    /// </remarks>
    /// <param name="rows">Rows with their sections already stamped.</param>
    /// <param name="arrangement">What order to lay the blocks out in.</param>
    public static IReadOnlyList<DebugRow> Arrange(
        IReadOnlyList<DebugRow> rows,
        InspectorArrangement arrangement)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(arrangement);

        // The headings, in the order they arrived. A list rather than a set
        // because arrival order is what breaks a tie between two groups the
        // arrangement says nothing about, and there are a dozen or so of these.
        var headings = new List<(string Section, string Group)>();
        foreach (var row in rows)
        {
            var heading = (row.Section, row.Group);
            if (!headings.Contains(heading))
            {
                headings.Add(heading);
            }
        }

        // OrderBy is stable, so two headings the arrangement ranks the same come
        // out in the order they arrived in -- which is what an unranked group
        // gets, and what every group gets under ArrivalOrder.
        var laid = new List<DebugRow>(rows.Count);
        foreach (var heading in headings.OrderBy(h => arrangement.SectionRank(h.Section))
                                        .ThenBy(h => arrangement.GroupRank(h.Section, h.Group)))
        {
            foreach (var row in rows)
            {
                if (string.Equals(row.Section, heading.Section, StringComparison.Ordinal) &&
                    string.Equals(row.Group, heading.Group, StringComparison.Ordinal))
                {
                    laid.Add(row);
                }
            }
        }

        return laid;
    }
}
