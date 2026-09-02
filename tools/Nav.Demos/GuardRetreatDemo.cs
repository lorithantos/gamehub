namespace Nav.Demos;

/// <summary>
/// The guard that does not die beside the cannon: six units hold a position an
/// enemy is wearing down, earn rank for standing in it, and rotate through
/// repair in the order their rank says -- without ever leaving it unheld.
/// </summary>
/// <remarks>
/// The C&amp;C behaviour this project was started over.
/// <para>
/// <b>Nothing here is scripted.</b> There is no <c>SetHealth</c> in this demo.
/// Standing within the enemy's reach costs 0.004 a tick and earns rank at the
/// same time, so every casualty is a consequence of where a unit stood and how
/// long it stayed, and every decision below is the doctrine meeting a situation
/// nobody arranged for it. The one scripted thing is the enemy, and it does one
/// thing once: at tick 100 it advances a single cell, from a position whose
/// reach covered the ring's front arc to one that covers all six. It hurts
/// nobody by moving. The line simply stops having a sheltered half.
/// </para>
/// <para>
/// <b>Being good at the job is what gets you sent to the rear.</b> Units 0, 2
/// and 3 are in reach from the start, so they are the ones who bleed AND the
/// ones who are promoted, and at ticks 57 and 59 their retreat threshold rises
/// from 0.40 to 0.55 along with the rank. They fall back at 0.54. As rookies
/// they would have stood there for another fourteen hundredths of health first.
/// The promotion is what pulled them out.
/// </para>
/// <para>
/// <b>The reserve has a price, and the demo now puts a number on it.</b> A unit
/// is meant to leave the moment it falls under its own threshold; it actually
/// leaves when a place comes free, and the gap is health spent standing in the
/// line waiting for one. Unit 3 goes at 0.44, eleven hundredths past its
/// threshold, because two were already away. Then the real one: <b>unit 5 goes
/// at 0.42 against a threshold of 0.70 -- 0.28 past it</b> -- because by then it
/// was the highest-ranked unit in the queue, and the reserve spends its places
/// on the LOWEST rank first. Two rookies at 0.51 and 0.50 went ahead of it.
/// </para>
/// <para>
/// That is the doctrine arriving on its own rather than being staged. A
/// veteran's place is the line: it earns faster where the enemy is, at full
/// rank it mends itself and needs a pad least, and its standing there is what
/// makes the position survivable for the rookies beside it. Here that rule
/// costs one specific veteran 0.28 of health, and the demo neither arranged it
/// nor hid it.
/// </para>
/// <para>
/// <b>Then it settles into a rotation, which is the whole point.</b> Veterans
/// cycle at their 0.70 threshold on trips of about twenty ticks -- out, mend,
/// back -- while the line goes on holding. All six finish as veterans, all six
/// earned it under fire, and the position was never held by fewer than four.
/// That last number is the original failure prevented from the other direction.
/// </para>
/// </remarks>
internal sealed class GuardRetreatDemo : Demo
{
    private const string Map =
        """
        type octile
        height 17
        width 25
        map
        .........................
        .........................
        ....@@@@@@@@.....@@@@....
        ....@......@.....@..@....
        ....@......@.....@..@....
        ....@......@.....@@@@....
        ....@@@@@@@@.............
        .........................
        .........................
        .........................
        ....@@@@.................
        ....@..@.....@@@@@@@@....
        ....@..@.....@......@....
        ....@@@@.....@......@....
        .............@@@@@@@@....
        .........................
        .........................
        """;

    public override string Name => "guard-retreat";

    public override string Description =>
        "Six guards hold a position an enemy is wearing down. Nothing is scripted: exposure both "
            + "earns rank and costs health, and rank decides who rotates through repair and who holds.";

    public override int Ticks => 320;

    /// <summary>Retreat thresholds by rank: rookie, regular, veteran.</summary>
    /// <remarks>
    /// Ascending, so a veteran is pulled at a scratch and a rookie holds to half
    /// health. See <see cref="RepairPolicy"/> for why that is the right way up.
    /// </remarks>
    private static readonly double[] RetreatByRank = [0.4, 0.55, 0.7];

    public override Run Play(TextWriter trace)
    {
        var grid = Grid.FromMapText(Map);
        var system = new MovementSystem(grid);

        var station = grid.Index(12, 8);
        var padNorth = grid.Index(2, 1);
        var padSouth = grid.Index(22, 15);

        // The enemy sits in the corridor between the two northern compounds,
        // looking down at the guard position. It never fires and never moves:
        // its whole job is to be somewhere, so that standing on the near side of
        // the line costs something and standing on the far side does not.
        var enemy = grid.Index(12, 3);

        // Five step costs of reach. The parked ring for six spans three cells,
        // and at this distance the boundary falls between its front arc and its
        // back -- the three nearest the corridor earn, the three behind them do
        // not. Ranks at 50 and 140 exposed ticks, so the promotions land inside
        // this demo's 320 rather than being asserted in a footnote.
        var world = new DemoWorld(
            grid,
            repairPerTick: 0.03,
            exposureRadius: 5.0,
            rankAt: [50, 140],
            damagePerTick: 0.004,
            selfHealPerTick: 0.002);
        world.RepairCells.Add(padNorth);
        world.RepairCells.Add(padSouth);
        world.HostileCells.Add(enemy);

        // Six guards, starting scattered on the left so the march to station is
        // itself worth watching. Six rather than four because the parking ring
        // for four is a plus one cell across, and a formation that tight cannot
        // be split by anything measured in distance -- every unit is the same
        // distance from the enemy, so nobody could out-earn anybody.
        int[] starts =
        [
            grid.Index(1, 4), grid.Index(1, 6), grid.Index(2, 9),
            grid.Index(1, 11), grid.Index(2, 13), grid.Index(1, 15),
        ];
        foreach (var cell in starts)
        {
            system.AddAgent(cell);
        }

        // Four of six stay standing whatever happens.
        const int Reserve = 4;
        var squad = new Squad(
            "guard",
            [0, 1, 2, 3, 4, 5],
            new GuardDoctrine(station, new RepairPolicy(RetreatByRank, returnAbove: 0.8, reserve: Reserve)));

        DemoTrace.WriteHeader(
            trace, Name, Description, grid, world.RepairPoints, Ticks, exposureRadius: world.ExposureRadius);

        var wasAway = new bool[6];
        var wasArrived = new bool[6];
        var ticksAway = new int[6];
        var wasRank = new int[6];
        var mostAwayAtOnce = 0;
        var worstOverrun = 0.0;

        // A LIST, not a string. Two units are promoted within a tick of each
        // other and two are detached in the same pass, and a single slot loses
        // whichever happened first -- which is how the first run of this demo
        // came to narrate three veterans it had only announced two of.
        var events = new List<string>();

        for (var tick = 0; tick < Ticks; tick++)
        {
            events.Clear();

            // NOTHING IS SCRIPTED. There is no SetHealth in this demo any more.
            // The enemy's reach costs 0.004 a tick to whoever is standing in it
            // and earns them rank at the same time, so every casualty below is a
            // consequence of where a unit chose to stand and how long it stayed.
            // Whatever the doctrine does with that, it is doing to a situation
            // nobody arranged for it.
            //
            // The one thing that IS scripted is the enemy, and it is scripted to
            // do one thing once: advance a single cell. From (12,3) its reach
            // covered the ring's front arc and not its back; from (12,4) it
            // covers all six. Nobody is hurt by the move and nothing is done to
            // any unit -- the line simply stops having a sheltered half, and
            // every consequence of that is the doctrine's.
            if (tick == 100)
            {
                world.HostileCells[0] = grid.Index(12, 4);
                events.Add("the enemy advances a cell -- the whole line is in reach now");
            }

            squad.Advance(system, world);
            system.Tick();

            var agents = system.Agents;
            world.Settle(agents);

            // Narrate the doctrine's decisions as they become visible.
            foreach (var agent in agents)
            {
                // Promotion first: it is the only thing happening for the first
                // half, and a viewer who does not see it earned will not believe
                // it later when it decides who leaves.
                var rank = world.RankOf(agent.Id);
                if (rank != wasRank[agent.Id])
                {
                    events.Add(rank == 1
                        ? $"unit {agent.Id} has held the near side long enough to be a regular"
                        : $"unit {agent.Id} is a veteran now");
                    wasRank[agent.Id] = rank;
                }

                if (agent.Away && !wasAway[agent.Id])
                {
                    // Rank and health together, because this is the line a
                    // viewer stops on to ask why THIS unit and not that one.
                    //
                    // And the OVERRUN, which is the price of the reserve stated
                    // as a number. A unit is meant to leave the moment it falls
                    // under its own rank's threshold; it actually leaves when a
                    // place comes free, and the gap is health it spent standing
                    // in the line waiting for one. Nothing chose that gap.
                    var health = world.HealthOf(agent.Id);
                    var overrun = RetreatByRank[Math.Min(rank, RetreatByRank.Length - 1)] - health;
                    worstOverrun = Math.Max(worstOverrun, overrun);
                    events.Add(overrun > 0.02
                        ? $"unit {agent.Id} falls back at {health:F2}, rank {rank} -- {overrun:F2} past its threshold, waiting for a place"
                        : $"unit {agent.Id} falls back to repair at {health:F2}, rank {rank}");
                }
                else if (!agent.Away && wasAway[agent.Id])
                {
                    events.Add($"unit {agent.Id} rejoins the line at {world.HealthOf(agent.Id):F2}");
                }
                else if (agent.Away && agent.Arrived && !wasArrived[agent.Id])
                {
                    events.Add($"unit {agent.Id} reaches the pad");
                }

                if (agent.Away)
                {
                    ticksAway[agent.Id]++;
                }

                wasAway[agent.Id] = agent.Away;
                wasArrived[agent.Id] = agent.Arrived;
            }

            mostAwayAtOnce = Math.Max(mostAwayAtOnce, agents.Count(a => a.Away));

            if (events.Count == 0 && tick == 0)
            {
                events.Add("the squad is ordered to hold the centre, with the enemy in the north corridor");
            }

            var note = events.Count == 0 ? null : string.Join("; ", events);

            DemoTrace.WriteTick(trace, grid, tick, agents, world, squad.Anchor, note);
        }

        // What this doctrine is judged on, and none of it is final health: a unit
        // that comes back at 0.8 is the intended outcome, and counting units "at
        // full health" would score that as a failure. Time off the line, the rank
        // the line earned, and the overrun -- the last being the reserve's price,
        // health spent standing past a threshold because no place was free.
        var final = system.Agents;
        var veterans = final.Count(a => world.RankOf(a.Id) >= 2);
        var everLeft = ticksAway.Count(t => t > 0);
        var worstStanding = final
            .Where(a => !a.Away)
            .Select(a => world.HealthOf(a.Id))
            .DefaultIfEmpty(1.0)
            .Min();

        return new Run(
            Ticks, final, world,
            $"{veterans}/{final.Count} veterans, all earned under fire; {everLeft} rotated through repair, "
                + $"never more than {mostAwayAtOnce} away against a reserve of {Reserve}; "
                + $"worst overrun {worstOverrun:F2} past a threshold, worst unit standing at {worstStanding:F2}");
    }
}
