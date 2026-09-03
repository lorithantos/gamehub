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
    /// <summary>
    /// Big enough that sight is a constraint. A guard on the station cannot see
    /// either pad — twenty-two cells against a tank's seven — so the retreat is
    /// planned to ground the pad itself reveals, and four blockhouses give the
    /// approaches something to bend around.
    /// </summary>
    private const string Map =
        """
        type octile
        height 33
        width 49
        map
        .................................................
        .................................................
        .................................................
        .................................................
        ........@@@@@@@@@@@...........@@@@@@@@@@@........
        ........@.........@...........@.........@........
        ........@.........@...........@.........@........
        ........@.........@...........@.........@........
        ........@@@@@@@@@@@...........@@@@@@@@@@@........
        .................................................
        .................................................
        .................................................
        .................................................
        .................................................
        ....@@@@@@@@@.......................@@@@@@@@@....
        ....@.......@.......................@.......@....
        ....@.......@.......................@.......@....
        ....@.......@.......................@.......@....
        ....@.......@.......................@.......@....
        ....@@@@@@@@@.......................@@@@@@@@@....
        .................................................
        .................................................
        .................................................
        .................................................
        ........@@@@@@@@@@@...........@@@@@@@@@@@........
        ........@.........@...........@.........@........
        ........@.........@...........@.........@........
        ........@.........@...........@.........@........
        ........@@@@@@@@@@@...........@@@@@@@@@@@........
        .................................................
        .................................................
        .................................................
        .................................................
        """;

    private const int Guards = 8;
    private const int Reserve = 5;

    /// <summary>Retreat thresholds by rank: rookie, regular, veteran.</summary>
    /// <remarks>
    /// Ascending, so a veteran is pulled at a scratch and a rookie holds to half
    /// health. See <see cref="RepairPolicy"/> for why that is the right way up.
    /// </remarks>
    private static readonly double[] RetreatByRank = [0.4, 0.55, 0.7];

    /// <summary>What each guard carries, by id. Tanks hold the plate; buggies carry the answer to infantry.</summary>
    /// <remarks>
    /// Two buggies rather than one now that the map is wide. A buggy sees nine
    /// against a tank's seven, so the line's own picture of the approach is
    /// mostly what the buggies are looking at.
    /// </remarks>
    private static readonly string[] GuardKits =
        ["tank", "buggy", "tank", "tank", "buggy", "tank", "tank", "buggy"];

    /// <summary>One wave: two fast anti-armour units, three infantry, and a buggy.</summary>
    private static readonly string[] Wave =
        ["rocketbike", "rocketbike", "rifleman", "rifleman", "rifleman", "buggy"];

    private static readonly int[] WaveTicks = [0, 160, 320];

    public override string Name => "guard-retreat";

    public override string Description =>
        "Eight guards hold a position against three waves that shoot back, under fog. Rank is earned from "
            + "damage dealt, and rank decides who rotates through repair and who holds.";

    public override int Ticks => 520;

    public override Run Play(TextWriter trace)
    {
        var grid = Grid.FromMapText(Map);
        var system = new MovementSystem(grid);
        var combat = Combat.From(Ini.FromFile(ConfigPath("combat.ini")));
        var scale = WorldScale.From(Ini.FromFile(ConfigPath("scale.ini")));

        var station = grid.Index(24, 16);

        // Opposite corners, and twenty-two cells from the station -- three times
        // what a tank can see. Under fog the guards know these are here only
        // because a pad watches its own ground; nothing on the line can see
        // either of them.
        var padNorth = grid.Index(2, 2);
        var padSouth = grid.Index(46, 30);

        // Where a wave goes to stand: the mouth of the north corridor, in reach
        // of the ring's front arc. A wave that holds there is a wave that is
        // shot at from the line and shoots back, which is the whole fight.
        var attackStation = grid.Index(24, 11);

        // Rank at a kill's worth and three kills' worth, given the credit rates
        // in the config. Self-healing so a full-rank guard mends on the walk;
        // no exposure damage, because the enemy has weapons now.
        var world = new DemoWorld(
            grid,
            repairPerTick: 0.03,
            exposureRadius: 6.0,
            rankAt: [50, 150],
            selfHealPerTick: 0.002,
            combat: combat,
            scale: scale,
            fog: true)
        {
            RankPerDamage = combat.RankPerDamage,
            RankPerKill = combat.RankPerKill,
        };
        world.RepairCells.Add(padNorth);
        world.RepairCells.Add(padSouth);

        // Eight guards, starting scattered down the west edge so the march to
        // station is itself worth watching -- and long enough now that they
        // arrive having seen almost nothing of the map they crossed.
        int[] starts =
        [
            grid.Index(1, 10), grid.Index(2, 12), grid.Index(1, 14), grid.Index(2, 16),
            grid.Index(1, 18), grid.Index(2, 20), grid.Index(1, 22), grid.Index(2, 24),
        ];
        for (var i = 0; i < starts.Length; i++)
        {
            var id = system.AddAgent(starts[i], side: 0);
            world.Enlist(id, GuardKits[i]);
        }

        var guard = new Squad(
            "guard",
            Enumerable.Range(0, Guards),
            new GuardDoctrine(station, new RepairPolicy(RetreatByRank, returnAbove: 0.8, reserve: Reserve)));
        var waves = new List<Squad>();

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

        var wasAway = new bool[Guards];
        var wasArrived = new bool[Guards];
        var ticksAway = new int[Guards];
        var wasRank = new int[Guards];
        var mostAwayAtOnce = 0;
        var worstOverrun = 0.0;
        var attackersSent = 0;
        var attackersDestroyed = 0;
        var guardsLost = 0;

        // A LIST, not a string: several things happen in one tick and a single
        // slot loses whichever happened first.
        var events = new List<string>();

        world.Listen(system);

        for (var tick = 0; tick < Ticks; tick++)
        {
            events.Clear();

            var waveIndex = Array.IndexOf(WaveTicks, tick);
            if (waveIndex >= 0)
            {
                var ids = new List<int>();
                for (var k = 0; k < Wave.Length; k++)
                {
                    var id = system.AddAgent(grid.Index(21 + k, 0), side: 1);
                    world.Enlist(id, Wave[k]);
                    ids.Add(id);
                }

                attackersSent += ids.Count;
                waves.Add(new Squad(
                    $"wave {waveIndex + 1}", ids,
                    new GuardDoctrine(attackStation, retreatBelow: 0.0, returnAbove: 0.5)));
                world.Listen(system);
                events.Add($"wave {waveIndex + 1} enters from the north: two rocket bikes, three riflemen, a buggy");
            }

            guard.Advance(system, world.ViewFor(0));
            foreach (var wave in waves)
            {
                wave.Advance(system, world.ViewFor(1));
            }

            system.Tick();
            world.Listen(system);
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
                if (agent.Id >= Guards || !agent.Alive)
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
                    var overrun = RetreatByRank[Math.Min(rank, RetreatByRank.Length - 1)] - health;
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

            mostAwayAtOnce = Math.Max(mostAwayAtOnce, agents.Count(a => a.Id < Guards && a.Alive && a.Away));

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
        var standing = final.Count(a => a.Id < Guards && a.Alive);
        var veterans = final.Count(a => a.Id < Guards && a.Alive && world.RankOf(a.Id) >= 2);
        var everLeft = ticksAway.Count(t => t > 0);

        return new Run(
            Ticks, final, world,
            $"{standing}/{Guards} guards standing, {guardsLost} lost; {attackersDestroyed}/{attackersSent} attackers destroyed; "
                + $"{veterans} veterans; {everLeft} rotated through repair, never more than {mostAwayAtOnce} away "
                + $"against a reserve of {Reserve}; worst overrun {worstOverrun:F2}");
    }
}
