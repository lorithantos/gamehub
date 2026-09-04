namespace Nav.Viewer.Tactics;

/// <summary>
/// The headings <see cref="DemoWorldDebugView"/> puts its <see cref="DebugRow"/>s
/// under.
/// </summary>
/// <remarks>
/// <b>NAMED, SO THAT A RENAME FAILS TO COMPILE.</b> A composition root lays this
/// source's blocks out next to the movement layer's, and it used to do it by
/// quoting these words. A quoted name that drifts does not break: the group stops
/// matching, drops to unknown-order and lands at the bottom of its section,
/// silently, in a running window nobody has open.
/// <para>
/// <b>HERE AND NOT IN Nav.Tactics.</b> These are an INSTRUMENT'S words, not the
/// tactics layer's: Nav.Tactics constructs no <see cref="DebugRow"/> anywhere,
/// and a squad or a kit knows what it is rather than what heading somebody files
/// it under. This project is where a tactics world is spent on rows, so this is
/// where the headings those rows carry belong -- and a host can see it, which is
/// what lets the arrangement name them.
/// </para>
/// <para>
/// <b>It says nothing about ORDER.</b> Which of these comes first is a decision
/// for whoever composed the application, so <see cref="All"/> is a SET, listed in
/// the order the view happens to write them and meaning nothing by it.
/// </para>
/// </remarks>
public static class DemoWorldGroups
{
    /// <summary>The squad the unit is in, as its own doctrine sees it.</summary>
    public const string Squad = "Squad";

    /// <summary>Whether the unit is up, and how badly hurt.</summary>
    public const string Condition = "Condition";

    /// <summary>What it is carrying and what that lets it do.</summary>
    public const string Kit = "Kit";

    /// <summary>What it is doing about the enemy.</summary>
    public const string Fight = "Fight";

    /// <summary>What its side can see and what it remembers seeing.</summary>
    public const string Perception = "Perception";

    /// <summary>The board as a whole: the tick, the sides, the standing.</summary>
    public const string World = "World";

    /// <summary>The numbers the fight is tuned by.</summary>
    public const string Rates = "Rates";

    /// <summary>What the unit has earned.</summary>
    public const string Rank = "Rank";

    /// <summary>
    /// Every heading this source can produce. A set -- see the remarks; the
    /// order a panel shows them in is not this project's to declare.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        [Squad, Condition, Kit, Fight, Perception, World, Rates, Rank];
}
