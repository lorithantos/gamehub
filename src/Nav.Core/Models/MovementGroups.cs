namespace Nav.Core.Models;

/// <summary>
/// The headings the movement layer puts its <see cref="DebugRow"/>s under.
/// </summary>
/// <remarks>
/// <b>NAMED, SO THAT A RENAME FAILS TO COMPILE.</b> These used to be method-local
/// constants inside <c>MovementSystem.DebugFor</c>, and anybody laying a panel
/// out had to quote them. A quoted name that drifts does not break: the group
/// simply stops matching, drops to unknown-order and lands at the bottom of its
/// section, silently, in a running window nobody has open. Naming them here
/// makes the same drift a build error.
/// <para>
/// <b>It says nothing about ORDER.</b> Which of these comes first on a panel is
/// a decision for whoever composed the application -- it is the only party that
/// can see this layer's words and another layer's at the same time -- so
/// <see cref="All"/> is a SET, listed in the order <c>DebugFor</c> happens to
/// write them and meaning nothing by it.
/// </para>
/// <para>
/// Here beside <see cref="DebugRow"/> rather than on <c>MovementSystem</c>,
/// because this is the debug-row vocabulary and not a fact about the system: a
/// reader following <see cref="DebugRow.Group"/> arrives at the words that can
/// go in one.
/// </para>
/// </remarks>
public static class MovementGroups
{
    /// <summary>The unit itself: who it is, where it is, what it was told.</summary>
    public const string Agent = "Agent";

    /// <summary>Whether it is getting anywhere, and what is in the way if not.</summary>
    public const string Progress = "Progress";

    /// <summary>The route it is on.</summary>
    public const string Plan = "Plan";

    /// <summary>The group it moves with, and the slot it holds in one.</summary>
    public const string Formation = "Formation";

    /// <summary>The distance field it is steering down.</summary>
    public const string Field = "Field";

    /// <summary>What the planner did last time it was asked.</summary>
    public const string Planning = "Planning";

    /// <summary>
    /// Every heading this layer can produce. A set -- see the remarks; the order
    /// a panel shows them in is not this layer's to declare.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        [Agent, Progress, Plan, Formation, Field, Planning];
}
