namespace Nav.Demos;

/// <summary>
/// The guard that does not die beside the cannon, under fire that shoots
/// back: six units hold a position against three waves of attackers, earn
/// rank from the damage they deal, and rotate through repair in the order
/// their rank says -- without ever leaving the position unheld.
/// </summary>
/// <remarks>
/// The C&amp;C behaviour this project was started over.
/// <para>
/// The world is <see cref="GuardRetreatScenario"/> and lives in the library,
/// so the same fight can be watched live in a viewer. What is left here is the
/// watching: the clock, the narration, and what the run amounted to.
/// </para>
/// <para>
/// <b>Both sides are doctrine.</b> The attackers are a <see cref="GuardDoctrine"/>
/// too: each wave is ordered to a station in the north corridor within reach
/// of the line and holds it, every unit shooting whatever in range can hurt it
/// fastest, with no pad to fall back to and a retreat threshold of zero. The
/// only scripted thing is when a wave arrives.
/// </para>
/// <para>
/// <b>Rank is earned from damage, not from standing.</b> A guard that shells
/// a rocket bike banks contribution; landing the killing blow banks more; and
/// the retreat threshold rises with the rank, so the guard that has done the
/// most is the one pulled at a scratch. The reserve keeps four standing whoever
/// is hurt, so a unit past its threshold waits for a place -- the overrun the
/// headline reports is that wait, in health.
/// </para>
/// <para>
/// <b>Nothing is scripted but the waves.</b> There is no <c>SetHealth</c>.
/// Every casualty on either side is a consequence of where a unit stood, what
/// it carried, and what chose to shoot it.
/// </para>
/// </remarks>
internal sealed class GuardRetreatDemo : Demo
{
    public override string Name => "guard-retreat";

    public override string Description =>
        "Eight guards hold a position against three waves that shoot back, under fog. Rank is earned from "
            + "damage dealt, and rank decides who rotates through repair and who holds.";

    public override int Ticks => 520;

    public override Run Play(TextWriter trace)
    {
        var scenario = new GuardRetreatScenario();
        var grid = scenario.Grid;
        var system = scenario.Board;
        var world = scenario.World;
        var guard = scenario.Guard;
        const int guards = GuardRetreatScenario.Guards;
        var retreatByRank = GuardRetreatScenario.RetreatByRank;

        // RepairCells, not RepairPoints: the header describes the MAP, and the
        // replay draws what is there rather than what a side has noticed. Under
        // fog the two differ, and asking a perception here would have written a
        // header with no pads at all, because nothing has been observed yet.
        // No exposure radius. The replay drew a dashed ring at that radius round
        // every enemy and the legend called it "rank is earned inside it", which
        // was true of the model exposure-rank replaced and is not true of this
        // one: rank comes from damage dealt, and reach is per kit, four to six
        // rather than a uniform six. A circle that means nothing is worse than
        // no circle. What belongs here instead is each unit's own weapon range,
        // which needs the kit in the trace.
        DemoTrace.WriteHeader(
            trace, Name, Description, grid, world.RepairCells, Ticks);

        var wasAway = new bool[guards];
        var wasArrived = new bool[guards];
        var ticksAway = new int[guards];
        var wasRank = new int[guards];
        var mostAwayAtOnce = 0;
        var worstOverrun = 0.0;
        var attackersSent = 0;
        var attackersDestroyed = 0;
        var guardsLost = 0;

        // A LIST, not a string: several things happen in one tick and a single
        // slot loses whichever happened first.
        var events = new List<string>();

        for (var tick = 0; tick < Ticks; tick++)
        {
            events.Clear();

            if (scenario.SendWave(tick) is { } wave)
            {
                attackersSent += wave.Members.Count;
                events.Add($"{wave.Name} enters from the north: two rocket bikes, three riflemen, a buggy");
            }

            guard.Advance(system, world.ViewFor(0));
            foreach (var squad in scenario.Waves)
            {
                squad.Advance(system, world.ViewFor(1));
            }

            system.Tick();
            world.Settle();

            foreach (var (victim, killer) in world.Fallen)
            {
                system.Remove(victim);
                var kit = world.KitOf(victim)!.Name;
                if (world.SideOf(victim) == 0)
                {
                    guardsLost++;
                    events.Add($"guard {victim}, a {kit}, is destroyed by unit {killer}");
                }
                else
                {
                    attackersDestroyed++;
                    events.Add($"unit {victim}, a {kit}, is destroyed by guard {killer}");
                }
            }

            var agents = system.Agents;
            foreach (var agent in agents)
            {
                if (agent.Id >= guards || !agent.Alive)
                {
                    continue;
                }

                var rank = world.RankOf(agent.Id);
                if (rank != wasRank[agent.Id])
                {
                    events.Add(rank == 1
                        ? $"guard {agent.Id} has done enough damage to be a regular"
                        : $"guard {agent.Id} is a veteran now");
                    wasRank[agent.Id] = rank;
                }

                if (agent.Away && !wasAway[agent.Id])
                {
                    // Rank and health together, because this is the line a
                    // viewer stops on to ask why THIS unit and not that one --
                    // and the OVERRUN, the reserve's price stated as a number:
                    // health spent standing past a threshold waiting for a place.
                    var health = world.HealthOf(agent.Id);
                    var overrun = retreatByRank[Math.Min(rank, retreatByRank.Count - 1)] - health;
                    worstOverrun = Math.Max(worstOverrun, overrun);
                    events.Add(overrun > 0.02
                        ? $"guard {agent.Id} falls back at {health:F2}, rank {rank} -- {overrun:F2} past its threshold, waiting for a place"
                        : $"guard {agent.Id} falls back to repair at {health:F2}, rank {rank}");
                }
                else if (!agent.Away && wasAway[agent.Id])
                {
                    events.Add($"guard {agent.Id} rejoins the line at {world.HealthOf(agent.Id):F2}");
                }
                else if (agent.Away && agent.Arrived && !wasArrived[agent.Id])
                {
                    events.Add($"guard {agent.Id} reaches the pad");
                }

                if (agent.Away)
                {
                    ticksAway[agent.Id]++;
                }

                wasAway[agent.Id] = agent.Away;
                wasArrived[agent.Id] = agent.Arrived;
            }

            mostAwayAtOnce = Math.Max(mostAwayAtOnce, agents.Count(a => a.Id < guards && a.Alive && a.Away));

            if (events.Count == 0 && tick == 0)
            {
                events.Add("the squad is ordered to hold the centre");
            }

            var note = events.Count == 0 ? null : string.Join("; ", events);

            DemoTrace.WriteTick(trace, grid, tick, agents, world, guard.Anchor, note);
        }

        // What this doctrine is judged on: did the position hold, at what cost,
        // and what did the line earn. Time off the line and the overrun are the
        // reserve's price; the ranks are what standing there bought.
        var final = system.Agents;
        var standing = final.Count(a => a.Id < guards && a.Alive);
        var veterans = final.Count(a => a.Id < guards && a.Alive && world.RankOf(a.Id) >= 2);
        var everLeft = ticksAway.Count(t => t > 0);

        return new Run(
            Ticks, final, world,
            $"{standing}/{guards} guards standing, {guardsLost} lost; {attackersDestroyed}/{attackersSent} attackers destroyed; "
                + $"{veterans} veterans; {everLeft} rotated through repair, never more than {mostAwayAtOnce} away "
                + $"against a reserve of {GuardRetreatScenario.Reserve}; worst overrun {worstOverrun:F2}");
    }
}
