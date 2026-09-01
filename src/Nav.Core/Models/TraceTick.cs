namespace Nav.Core.Models;

/// <summary>
/// The world as one tick saw it: state <em>after</em> the orders due at
/// <paramref name="Tick"/> were issued, <em>before</em> the tick advanced —
/// the same instant the trajectory check records. <paramref name="Report"/>
/// is the previous tick's spend, all zeros on tick 0.
/// </summary>
public sealed record TraceTick(int Tick, IReadOnlyList<AgentState> Agents, TickReport Report);
