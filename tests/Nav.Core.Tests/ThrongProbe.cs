using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>SPIKE PROBE, not a test. Dumps who holds cell 86 around the collision.</summary>
public sealed class ThrongProbe(ITestOutputHelper output)
{
    [Fact]
    public void WhoHoldsCell86()
    {
        var grid = Grid.FromMapFile(Fixtures.Map("gap.map"));
        var scenario = RecordedScenario.FromFile(Fixtures.Scenario("throng"));
        var system = new MovementSystem(grid, horizon: 32);
        foreach (var a in scenario.Agents)
        {
            system.AddAgent(grid.Index(a.X, a.Y));
        }

        var pending = 0;
        const int Cell = 86;
        for (var tick = 0; tick <= 20; tick++)
        {
            while (pending < scenario.Orders.Count && scenario.Orders[pending].Tick == tick)
            {
                var o = scenario.Orders[pending++];
                system.Order(o.Agents, grid.Index(o.X, o.Y));
            }

            if (tick >= 13)
            {
                var holders = string.Join(" ", Enumerable.Range(tick, 5).Select(t => $"t{t}={system.Table.HolderOf(Cell, t)}"));
                var enders = string.Join(",", system.CurrentPlans().Where(p => p.Plan.Cells[^1] == Cell).Select(p => p.Agent));
                var on = string.Join(",", system.Agents.Where(a => a.Cell == Cell).Select(a => a.Id));
                output.WriteLine($"tick {tick,2}  on86=[{on,-5}]  plansEndingOn86=[{enders,-6}]  holders: {holders}");

                // What each agent's COMMITTED plan says it will be doing, tick by tick.
                foreach (var id in new[] { 13, 16 })
                {
                    var plan = system.CurrentPlans().FirstOrDefault(p => p.Agent == id).Plan;
                    var cells = plan is null
                        ? "(no plan)"
                        : string.Join(" ", Enumerable.Range(tick, 7).Select(t => $"t{t}={plan.CellAt(t)}"));
                    output.WriteLine($"          agent {id}: {cells}");
                }
            }

            if (tick < 20)
            {
                system.Tick();
            }
        }
    }
}
