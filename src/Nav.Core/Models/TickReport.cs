namespace Nav.Core.Models;

/// <param name="NodesSpent">Search nodes expanded during the tick.</param>
/// <param name="SearchesStarted">Searches begun during the tick.</param>
/// <param name="SearchesFinished">Searches that produced a plan during the tick.</param>
/// <param name="SearchesAbandoned">Searches discarded because their anchor went stale.</param>
/// <param name="Queued">Agents still waiting for a planning slot at the end of the tick.</param>
public readonly record struct TickReport(
    int NodesSpent,
    int SearchesStarted,
    int SearchesFinished,
    int SearchesAbandoned,
    int Queued);
