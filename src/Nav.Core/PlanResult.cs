namespace Nav.Core;

/// <summary>
/// One agent's intended occupancy over time: where it is on each tick.
/// </summary>
/// <param name="Cells">
/// One cell per tick, starting at <paramref name="StartTick"/>. A repeated cell is
/// a tick spent waiting, which is a real part of the plan rather than padding.
/// </param>
/// <param name="StartTick">The tick <paramref name="Cells"/> begins at.</param>
/// <param name="Cost">Sum of step and wait costs along the plan.</param>
/// <param name="Expanded">States popped and closed. A cost measure, not a result.</param>
/// <param name="Found">Whether the plan reaches the goal. See the remarks.</param>
/// <remarks>
/// <b><paramref name="Found"/> false does not mean failure.</b> Planning is
/// bounded by the reservation window, so a goal further away than the window is
/// deep is normal and expected: the plan then walks as close as it can and the
/// agent replans when the window has moved. That is a <see cref="IsPartial"/>
/// result, and it is progress.
/// <para>
/// Genuine failure is an empty <paramref name="Cells"/> -- the agent could not
/// even stand still, which means its own cell is taken at the next tick and it has
/// nowhere to step.
/// </para>
/// </remarks>
public sealed record PlanResult(
    IReadOnlyList<int> Cells,
    int StartTick,
    double Cost,
    int Expanded,
    bool Found)
{
    /// <summary>A plan that made progress toward the goal without reaching it.</summary>
    public bool IsPartial => !Found && Cells.Count > 0;

    /// <summary>True when no plan could be made at all, not even waiting in place.</summary>
    public bool IsStuck => Cells.Count == 0;

    /// <summary>The last tick the plan covers.</summary>
    public int LastTick => Cells.Count > 0 ? StartTick + Cells.Count - 1 : StartTick;

    public static PlanResult Stuck(int startTick, int expanded) =>
        new([], startTick, 0.0, expanded, Found: false);
}
