namespace Nav.Core.Models;

/// <summary>
/// One agent's id paired with the cells it occupies over time.
/// </summary>
/// <param name="Agent">
/// The id a <see cref="Conflict"/> names. It travels with the plan because
/// position in the list is not identity -- stuck agents are dropped before the
/// check runs.
/// </param>
/// <param name="Plan">
/// Cells against ticks. Read only through <see cref="PlanResult.CellAt"/>, so an
/// intended plan and an actual trajectory are interchangeable here; that is what
/// lets <see cref="ScenarioPlayback"/> check what happened rather than what was
/// meant.
/// </param>
public readonly record struct AgentPlan(int Agent, PlanResult Plan);
