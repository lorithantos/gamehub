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

    /// <summary>
    /// An arrangement built out of what the VIEWS said they can produce, with
    /// whatever the composer wants moved moved.
    /// </summary>
    /// <remarks>
    /// <b>THE NAMES COME FROM THE PRODUCERS AND THE ORDER COMES FROM HERE.</b>
    /// A composition root holding several views can read
    /// <see cref="IDebugView.Groups"/> off each of them, so the one thing it has
    /// to supply is what it alone knows: which section each view answers under,
    /// what order the sections go in, and any group it wants somewhere other than
    /// where its view put it. Nothing is written down twice, so nothing can
    /// drift.
    /// <para>
    /// <b>Derived by default is not fixed.</b> A section named in
    /// <paramref name="preferences"/> gets those groups FIRST, in that order, and
    /// the rest of what was declared follows in declared order -- so hoisting one
    /// heading costs one name rather than a restatement of the section. A
    /// preference for a group nobody declared is kept and ranks above the
    /// declared ones, which is what a composer expecting a view that has not
    /// shipped yet asked for.
    /// </para>
    /// <para>
    /// A section named twice in <paramref name="sections"/> -- two sources under
    /// one caption -- is ONE section, holding the first view's groups and then
    /// whatever the next one adds. It keeps the position of its first mention,
    /// because a caption does not move because a second view was handed over.
    /// </para>
    /// <para>
    /// This is where a loader would land: a file that says what to hoist becomes
    /// <paramref name="preferences"/>, and the vocabulary still comes from the
    /// code that emits it.
    /// </para>
    /// </remarks>
    /// <param name="sections">
    /// What each section holds, in section order: a caption, and the groups a
    /// view under it declared.
    /// </param>
    /// <param name="preferences">
    /// Groups to place first inside a section, in the order wanted. Null or empty
    /// takes every view's own order as it stands.
    /// </param>
    public static InspectorArrangement Derived(
        IReadOnlyList<(string Section, IReadOnlyList<string> Declared)> sections,
        IReadOnlyList<(string Section, IReadOnlyList<string> First)>? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(sections);

        // Ordered, because the caption order is the composer's answer and a
        // dictionary alone would lose it.
        var order = new List<string>();
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Add(string section, IReadOnlyList<string> names)
        {
            ArgumentNullException.ThrowIfNull(section);
            ArgumentNullException.ThrowIfNull(names);

            if (!groups.TryGetValue(section, out var into))
            {
                order.Add(section);
                into = [];
                groups[section] = into;
                seen[section] = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (var name in names)
            {
                ArgumentNullException.ThrowIfNull(name);
                if (seen[section].Add(name))
                {
                    into.Add(name);
                }
            }
        }

        foreach (var (section, declared) in sections)
        {
            ArgumentNullException.ThrowIfNull(section);

            // A section's preferences are applied once, at its first mention, so
            // a hoisted group outranks the declaration it was hoisted out of --
            // and a second view under the same caption cannot re-hoist anything.
            var first = !groups.ContainsKey(section);
            Add(section, []);
            if (first && preferences is not null)
            {
                foreach (var (named, hoisted) in preferences)
                {
                    if (string.Equals(named, section, StringComparison.Ordinal))
                    {
                        Add(section, hoisted);
                    }
                }
            }

            Add(section, declared);
        }

        var built = new List<(string Section, IReadOnlyList<string> Groups)>(order.Count);
        foreach (var section in order)
        {
            built.Add((section, groups[section]));
        }

        return new InspectorArrangement(built);
    }

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
