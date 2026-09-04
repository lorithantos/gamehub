namespace Nav.Viewer.Models;

/// <summary>
/// How an inspector panel is arranged: which sections it shows, in what order,
/// and which groups sit under each of them, in what order.
/// </summary>
/// <remarks>
/// <b>A SHAPE, NOT A TABLE.</b> This type can hold an arrangement and can rank a
/// heading against one. It names no section and no group, and it must not learn
/// to: the moment a list of group names is written down in this project, the
/// viewer knows what a Kit is and what a Squad is, and Nav.Viewer.Shared
/// references Nav.Core alone precisely so that it cannot.
/// <para>
/// <b>The names live where the composition root can see both halves.</b> A group
/// name is a producer's vocabulary -- the movement layer's, a source's -- and
/// the only place entitled to lay one layer's words out next to another's is
/// whoever composed the two. So the arrangement arrives as an argument, from a
/// host, and a loader that reads one out of a file later changes nothing here.
/// </para>
/// <para>
/// <b><see cref="ArrivalOrder"/> is not a fallback.</b> It is the contract
/// <see cref="DebugRow"/> already states -- rows arrive already in group order,
/// so a panel renders headings by watching the group change rather than by
/// sorting. An arrangement OVERRIDES that with a preference; supplying none
/// leaves the producers' own sequence exactly as it was written, which is what a
/// host with no inspector wants and what the panel did before there was a table.
/// </para>
/// </remarks>
public sealed class InspectorArrangement
{
    /// <summary>
    /// No preference at all: every heading ranks the same, so
    /// <see cref="InspectorLayout.Arrange"/> hands them back in the order they
    /// arrived.
    /// </summary>
    public static InspectorArrangement ArrivalOrder { get; } = new([]);

    /// <summary>Where each section sits, by name.</summary>
    private readonly Dictionary<string, int> _sectionRanks;

    /// <summary>Where each group sits inside its section, by section then name.</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _groupRanks;

    /// <summary>
    /// An arrangement over <paramref name="sections"/>: the sections in the
    /// order they are shown, each with the groups it holds in the order they are
    /// shown.
    /// </summary>
    /// <remarks>
    /// The lists are read once, here, and turned into ranks. Nothing the caller
    /// does to its own arrays afterwards can reorder a panel mid-run.
    /// </remarks>
    /// <param name="sections">The arrangement. Empty is <see cref="ArrivalOrder"/>.</param>
    public InspectorArrangement(IReadOnlyList<(string Section, IReadOnlyList<string> Groups)> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var copied = new List<(string Section, IReadOnlyList<string> Groups)>(sections.Count);
        _sectionRanks = new Dictionary<string, int>(StringComparer.Ordinal);
        _groupRanks = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        for (var i = 0; i < sections.Count; i++)
        {
            var (section, groups) = sections[i];
            ArgumentNullException.ThrowIfNull(section);
            ArgumentNullException.ThrowIfNull(groups);

            var inside = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var g = 0; g < groups.Count; g++)
            {
                inside[groups[g]] = g;
            }

            copied.Add((section, groups.ToArray()));
            _sectionRanks[section] = i;
            _groupRanks[section] = inside;
        }

        Sections = copied;
    }

    /// <summary>
    /// The arrangement as it was supplied, for a test or a host that wants to
    /// read back what it laid out.
    /// </summary>
    public IReadOnlyList<(string Section, IReadOnlyList<string> Groups)> Sections { get; }

    /// <summary>
    /// Where <paramref name="section"/> sits, or after every section this
    /// arrangement names.
    /// </summary>
    internal int SectionRank(string section) =>
        _sectionRanks.TryGetValue(section, out var rank) ? rank : _sectionRanks.Count;

    /// <summary>
    /// Where <paramref name="group"/> sits inside <paramref name="section"/>, or
    /// after every group this arrangement names under that section.
    /// </summary>
    internal int GroupRank(string section, string group) =>
        _groupRanks.TryGetValue(section, out var groups) && groups.TryGetValue(group, out var rank)
            ? rank
            : int.MaxValue;
}
