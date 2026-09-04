namespace Nav.Viewer.Models;

/// <summary>
/// How the inspector panel is arranged: which sections it shows, in what order,
/// and which groups sit under each of them, in what order.
/// </summary>
/// <remarks>
/// <b>ONE TABLE, IN ONE PLACE.</b> <see cref="Arrangement"/> is the whole of the
/// arrangement. Everything else in this class reads it. Nothing else in the
/// viewer holds an opinion about what order a panel is in, so a reader who wants
/// a different panel -- movement at two hundred agents wants FIELD and PLANNING
/// and none of KIT or RATES; a fight wants SQUAD, CONDITION and FIGHT and none of
/// FIELD -- has one list to change and one place for a loader to point at later.
/// <para>
/// No file format, no settings type and no interface: this is a static table
/// today, and the extension point is that <see cref="Arrangement"/> is the only
/// thing that would have to come from somewhere else. That is the same shape
/// <see cref="Keymap"/> uses -- the bindings are a table and the extension point
/// is its constructor -- and for the same reason.
/// </para>
/// <para>
/// <b>A SECTION SAYS WHICH LAYER ANSWERED, so the table does not get to decide
/// it.</b> The instrument stamps <see cref="DebugRow.Section"/> from the question
/// it asked -- the movement layer, a supplied source, or itself -- and this table
/// says how the answers are laid out. Deciding a section from the GROUP NAME
/// instead would hand a stranger the movement layer's caption: a source is
/// somebody else's code and may name a group anything, which is the whole reason
/// the de-collision machinery exists, and a source that happened to say "Agent"
/// would land its rows under MOVEMENT and the panel would be claiming the
/// movement layer answered when it did not.
/// </para>
/// <para>
/// So the group lists here are ORDER, and a declaration of what each layer is
/// expected to say. A group the table has never heard of is a normal case rather
/// than an error: it keeps the section of the layer that produced it and sorts
/// after every group the table does know, in the order it arrived.
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
    /// The panel, declared: sections in the order they are shown, each with the
    /// groups it holds in the order they are shown.
    /// </summary>
    public static IReadOnlyList<(string Section, IReadOnlyList<string> Groups)> Arrangement { get; } =
    [
        (MovementSection, new[] { "Agent", "Progress", "Plan", "Formation", "Field", "Planning" }),
        (TacticsSection, new[] { "Squad", "Condition", "Kit", "Fight", "Perception", "World", "Rates", "Rank" }),
        (ViewerSection, new[] { SourcesGroup, ControlsGroup }),
    ];

    /// <summary>Where each section sits, by name.</summary>
    private static readonly Dictionary<string, int> SectionRanks = BuildSectionRanks();

    /// <summary>Where each group sits inside its section, by section then name.</summary>
    private static readonly Dictionary<string, Dictionary<string, int>> GroupRanks = BuildGroupRanks();

    /// <summary>
    /// The same rows, laid out: sections in table order, groups in table order
    /// inside them, and the rows of a group untouched.
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
    /// </remarks>
    /// <param name="rows">Rows with their sections already stamped.</param>
    public static IReadOnlyList<DebugRow> Arrange(IReadOnlyList<DebugRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        // The headings, in the order they arrived. A list rather than a set
        // because arrival order is what breaks a tie between two groups the table
        // says nothing about, and there are a dozen or so of these.
        var headings = new List<(string Section, string Group)>();
        foreach (var row in rows)
        {
            var heading = (row.Section, row.Group);
            if (!headings.Contains(heading))
            {
                headings.Add(heading);
            }
        }

        // OrderBy is stable, so two headings the table ranks the same come out in
        // the order they arrived in -- which is what an unranked group gets.
        var laid = new List<DebugRow>(rows.Count);
        foreach (var heading in headings.OrderBy(h => SectionRank(h.Section))
                                        .ThenBy(h => GroupRank(h.Section, h.Group)))
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

    /// <summary>
    /// Where <paramref name="section"/> sits, or after every section the table
    /// knows.
    /// </summary>
    internal static int SectionRank(string section) =>
        SectionRanks.TryGetValue(section, out var rank) ? rank : SectionRanks.Count;

    /// <summary>
    /// Where <paramref name="group"/> sits inside <paramref name="section"/>, or
    /// after every group the table knows about that section.
    /// </summary>
    internal static int GroupRank(string section, string group) =>
        GroupRanks.TryGetValue(section, out var groups) && groups.TryGetValue(group, out var rank)
            ? rank
            : int.MaxValue;

    private static Dictionary<string, int> BuildSectionRanks()
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < Arrangement.Count; i++)
        {
            ranks[Arrangement[i].Section] = i;
        }

        return ranks;
    }

    private static Dictionary<string, Dictionary<string, int>> BuildGroupRanks()
    {
        var ranks = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (section, groups) in Arrangement)
        {
            var inside = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < groups.Count; i++)
            {
                inside[groups[i]] = i;
            }

            ranks[section] = inside;
        }

        return ranks;
    }
}
