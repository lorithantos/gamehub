namespace Nav.Demos;

/// <summary>
/// The guard that does not die beside the cannon: six units hold a position in
/// sight of an enemy, earn rank for standing on the near side of it, and fall
/// back to repair in the order their rank says -- without ever leaving the
/// position unheld.
/// </summary>
/// <remarks>
/// The C&amp;C behaviour this project was started over. Watch for five things,
/// in the order they happen.
/// <para>
/// <b>The guard takes its station and stops.</b> Then nothing happens to
/// anybody for half the demo, and that is not dead time: three of the six are
/// parked within reach of the enemy in the north corridor and the other three
/// are not, so at ticks 57 and 59 the near side becomes regulars and at 147 and
/// 149 it becomes veterans. Rank is not handed out here. It is earned by
/// standing somewhere, and a viewer can watch it being earned.
/// </para>
/// <para>
/// <b>The damage is middling on purpose.</b> Every casualty is hurt to around
/// half, which is above the threshold this demo used to run with -- under the
/// old numbers none of them would have moved, because the guard only left when
/// it was nearly dead and the retreat was never a decision. The played doctrine
/// is <em>retreat at middling damage, return as soon as it is worth it</em>.
/// Unit 2 is away for 21 ticks and comes back at 0.80.
/// </para>
/// <para>
/// <b>Then the pair, and it looks wrong.</b> At tick 180 a veteran is scratched
/// to 0.65 and leaves at once. At 184 a rookie is hurt WORSE, to 0.5, and
/// stands there. The healthier unit walks off the line while the hurt one holds
/// it. That is the doctrine: rank raises the retreat threshold, because the
/// reason to have ranks is that the good unit is the one you cannot replace.
/// </para>
/// <para>
/// <b>And then the reserve.</b> At 240 three more are hurt at once and only two
/// may go, because four of six must stay standing. The two that go are taken by
/// rank -- the veteran at 0.30 before either rookie at 0.25 -- so unit 5 waits
/// 36 ticks at a quarter health for a place to come free. A line that emptied
/// itself to the repair pads would be the original failure arrived at from the
/// other direction, and this is what stops it.
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
        "Six guards hold a position under an enemy's eye; rank is earned on the near side, "
            + "and decides who falls back to repair and who holds the line.";

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
        var world = new DemoWorld(grid, repairPerTick: 0.03, exposureRadius: 5.0, rankAt: [50, 140]);
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

        DemoTrace.WriteHeader(trace, Name, Description, grid, world.RepairPoints, Ticks);

        var wasAway = new bool[6];
        var wasArrived = new bool[6];
        var ticksAway = new int[6];
        var wasRank = new int[6];
        var mostAwayAtOnce = 0;

        // A LIST, not a string. Two units are promoted within a tick of each
        // other and two are detached in the same pass, and a single slot loses
        // whichever happened first -- which is how the first run of this demo
        // came to narrate three veterans it had only announced two of.
        var events = new List<string>();

        for (var tick = 0; tick < Ticks; tick++)
        {
            events.Clear();

            // Nothing happens to anybody for the first half. That is not dead
            // time: the front arc is standing in reach of the corridor earning
            // rank, and the promotions are the setup for every decision below.
            //
            // THE PAIR. A veteran hurt LESS than a rookie, four ticks apart, and
            // the veteran is the one that goes. 0.65 is a scratch and it is
            // under the veteran's 0.7; 0.5 is worse and it is over the rookie's
            // 0.4. On screen the healthier unit walks away from the line while
            // the hurt one stands in it, which is the doctrine and not a bug in
            // it: the veteran is the one that cannot be replaced.
            if (tick == 180)
            {
                world.SetHealth(2, 0.65);
                events.Add("unit 2 -- a veteran -- is scratched to 0.65");
            }
            else if (tick == 184)
            {
                world.SetHealth(4, 0.5);
                events.Add("unit 4 -- a rookie -- is hurt worse, to 0.5, and holds anyway");
            }

            // THE RESERVE. Now hurt most of the line at once. Four must stay
            // standing whatever happens, so the squad cannot empty; and the
            // places it does have go by rank, so the veteran at 0.3 leaves while
            // the rookies at 0.25 -- hurt worse -- keep the position.
            else if (tick == 240)
            {
                world.SetHealth(0, 0.3);
                world.SetHealth(1, 0.25);
                world.SetHealth(5, 0.25);
                events.Add("the line takes fire: three more hurt at once");
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
                    events.Add(
                        $"unit {agent.Id} falls back to repair at {world.HealthOf(agent.Id):F2}, rank {rank}");
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

        // Time off the line is the number this doctrine is judged on, not final
        // health: a unit that comes back at 0.8 having been gone thirty ticks is
        // the intended outcome, and counting units "at full health" would score
        // that as a failure. The rank split is the other half -- it is what
        // every retreat decision above was made against.
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
            $"{veterans}/{final.Count} earned veteran on the near side; {everLeft} went to repair, "
                + $"{mostAwayAtOnce} away at the worst moment against a reserve of {Reserve}; "
                + $"worst unit still standing at {worstStanding:F2}");
    }
}
