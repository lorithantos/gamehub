using System.Globalization;
using System.Text;

using Nav.Core;

namespace Nav.Viewer.Tactics;

/// <summary>
/// The tactics world as rows: what the fight is set up to do, and what it
/// currently believes about one unit.
/// </summary>
/// <remarks>
/// <b>The only place both halves of the seam are visible at once.</b> A world
/// goes in, <see cref="DebugRow"/> comes out, and a panel upstream never learns
/// there was a kit or a sighting involved -- Nav.Viewer.Shared has no reference
/// it could learn through. Every row is written HERE, where the numbers have
/// meanings attached to them, rather than handed up as bare values for a panel
/// to guess at.
/// <para>
/// <b>It is named for what it wraps and its interface is not, which is the
/// right way round.</b> <see cref="IWorldDebugView"/> is one of several sources
/// an application can hand a viewer; this is the one that reads a tactics
/// world, and a caller picking it out of a list is the only thing that has to
/// know that.
/// </para>
/// <para>
/// <b>Everything it reports is as of the last clock edge.</b> The simulation is
/// a synchronous digital system and <c>Settle</c> is its edge; this view has no
/// verb on it, resolves nothing, refreshes nothing, and reports the tick it is
/// answering for as its first row. A view that could bring the world up to date
/// on the way past would be a view that changed the run whenever somebody
/// looked.
/// </para>
/// <para>
/// Perception is read through <see cref="IPerceptionView"/> rather than through
/// the world's own doctrine queries. Both are pure reads today; the view is the
/// type an instrument is MEANT to hold, and holding it is what keeps that true
/// if the resolve ever moves off the edge again.
/// </para>
/// </remarks>
public sealed class DemoWorldDebugView : IWorldDebugView
{
    // Named once in DemoWorldGroups, so a composition root laying these out
    // cannot go on quoting a heading this view has stopped emitting.
    private const string World = DemoWorldGroups.World;
    private const string Rates = DemoWorldGroups.Rates;
    private const string Ranks = DemoWorldGroups.Rank;

    private readonly DemoWorld _world;
    private readonly ISquadView[] _squads;

    /// <param name="world">The world to read. Never written to.</param>
    /// <param name="squads">
    /// The squads on the board, as their doctrines see them: one snapshot each,
    /// taken by whoever composed this view. Null or empty is a world nobody has
    /// put a squad on, and the squad rows say so rather than going missing.
    /// </param>
    /// <remarks>
    /// <b>Views, not squads.</b> What a doctrine is handed is
    /// <see cref="ISquadView"/>, so that is what the panel reads: the rows below
    /// cannot say anything a doctrine could not have seen, and they cannot move
    /// anything either, because the movement half of the seam is not on the type.
    /// <para>
    /// A snapshot goes stale the moment the world ticks, which is why the host
    /// builds this view again for every read rather than keeping one. See
    /// <c>LiveWorldSource</c>.
    /// </para>
    /// </remarks>
    public DemoWorldDebugView(DemoWorld world, IReadOnlyList<ISquadView>? squads = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _squads = squads is null ? [] : [.. squads];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>EVERYTHING THIS SOURCE PUTS ON A PANEL, including the rows it answers
    /// through <see cref="DebugFor"/>.</b> A composer holds the source and lays
    /// out one section for it; the per-unit view is this source answering about a
    /// unit rather than a second party, and a list that named only the three
    /// headings below would leave the five a reader spends most of their time
    /// looking at unordered.
    /// <para>
    /// So <see cref="Describe"/> reaches three of these and the per-unit view
    /// reaches the other five. Both are subsets, which is what
    /// <see cref="IDebugView.Groups"/> promises.
    /// </para>
    /// </remarks>
    [Observes]
    public IReadOnlyList<string> Groups => DemoWorldGroups.All;

    /// <inheritdoc/>
    /// <remarks>
    /// The setup rather than the state: the rates a tick applies, the rank table
    /// a unit is measured against, and how much of the board there is to fight
    /// over. All of it is config the world was built with, so none of it moves
    /// between edges -- but a panel showing a unit losing health at some rate
    /// wants the rate on screen beside it, and none of these numbers is
    /// reachable from a unit's own rows.
    /// </remarks>
    [Observes]
    public IReadOnlyList<DebugRow> Describe()
    {
        var view = _world.View;
        var (ranks, ranksNote) = RankTable();

        return new List<DebugRow>
        {
            view.AsOf < 0
                ? new(World, "as of", "no edge yet", "nothing has settled, so there is nothing to be as of")
                : new(World, "as of", $"tick {Number(view.AsOf)}", "the edge every answer here is taken at"),

            _world.Fog
                ? new(World, "fog", "on", "a side is limited to what its own units and pads can see")
                : new(World, "fog", "off", "every side is told about every unit, and so remembers none"),

            _world.HostileCells.Count == 0
                ? new(World, "threats", "none", "no scripted threat is on the board")
                : new(
                    World,
                    "threats",
                    $"{Number(_world.HostileCells.Count)} cells",
                    "scripted, and hostile to every side"),

            _world.RepairCells.Count == 0
                ? new(World, "pads", "none", "there is nowhere to be repaired")
                : new(
                    World,
                    "pads",
                    $"{Number(_world.RepairCells.Count)} cells",
                    $"repair cells, each lighting {Amount(_world.PadSight)} steps of map around itself"),

            _world.Fallen.Count == 0
                ? new(World, "fallen", "none", "nobody reached zero health on the last edge")
                : new(
                    World,
                    "fallen",
                    Number(_world.Fallen.Count),
                    "reached zero health on the last edge"),

            new(Rates, "repair", Amount(_world.RepairPerTick), "health a tick standing on a pad"),

            _world.DamagePerTick == 0.0
                ? new(Rates, "damage", "0", "standing exposed costs nothing; damage comes from fire alone")
                : new(
                    Rates,
                    "damage",
                    Amount(_world.DamagePerTick),
                    "health a tick standing exposed to a threat"),

            _world.SelfHealPerTick == 0.0
                ? new(Rates, "self-heal", "0", "nobody heals except on a pad")
                : new(
                    Rates,
                    "self-heal",
                    Amount(_world.SelfHealPerTick),
                    "health a tick at the top of the rank table, wherever it stands"),

            new(
                Rates,
                "exposure radius",
                $"{Amount(_world.ExposureRadius)} steps",
                "to a threat counts as exposed"),

            new(Ranks, "table", ranks, ranksNote),

            new(
                Ranks,
                "per damage",
                Amount(_world.RankPerDamage),
                "contribution for a unit's worth of damage dealt"),

            new(
                Ranks,
                "per kill",
                Amount(_world.RankPerKill),
                "contribution for the killing blow, up 30% for each rank the victim had"),
        };
    }

    /// <inheritdoc/>
    [Observes]
    public IDebugView DebugFor(int agent) => new UnitDebugView(_world, _squads, agent);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Amount(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:0.##}");

    /// <summary>The thresholds as one line, because they only mean anything in order.</summary>
    /// <remarks>
    /// How many ranks there are is the value; where they sit is the note. The
    /// list grows with the table and the count does not, and a column has to be
    /// sized for one of the two.
    /// </remarks>
    private (string Value, string Note) RankTable()
    {
        var table = _world.RankAt;
        if (table.Count == 0)
        {
            return ("empty", "rank never rises, and so nobody is ever a veteran");
        }

        var text = new StringBuilder();
        for (var rank = 0; rank < table.Count; rank++)
        {
            if (rank > 0)
            {
                text.Append(", ");
            }

            text.Append(Amount(table[rank]));
        }

        return ($"{Number(table.Count)} ranks", $"above rookie, at {text} contribution");
    }

    /// <summary>
    /// One unit's state, read when <see cref="IDebugView.Describe"/> is called
    /// and never before.
    /// </summary>
    /// <remarks>
    /// It holds an id and a world, so a panel that keeps one costs nothing while
    /// it is not being drawn -- and it cannot go on describing a unit as it was,
    /// because it never took a copy to be wrong with.
    /// <para>
    /// The world keeps no roster: it learns units from the movement events it is
    /// listening to, and answers for an id it has never heard of with the same
    /// defaults it answers for an unenlisted one. So a NEGATIVE id is refused by
    /// name here and everything else is answered honestly -- an id with no kit
    /// and no side reads as exactly that, which is what the world knows and all
    /// it knows.
    /// </para>
    /// </remarks>
    private sealed class UnitDebugView(DemoWorld world, IReadOnlyList<ISquadView> squads, int id) : IDebugView
    {
        private const string Squad = DemoWorldGroups.Squad;
        private const string Condition = DemoWorldGroups.Condition;
        private const string Loadout = DemoWorldGroups.Kit;
        private const string Fight = DemoWorldGroups.Fight;
        private const string Perception = DemoWorldGroups.Perception;

        /// <inheritdoc/>
        /// <remarks>
        /// The five a UNIT is described under, and not the three the world is:
        /// this view answers for one unit and never writes a row about the board.
        /// A negative id reaches one of these and a unit in no squad reaches four,
        /// which is a subset and not a missing heading.
        /// </remarks>
        [Observes]
        public IReadOnlyList<string> Groups => Vocabulary;

        /// <summary>The five, shared: a vocabulary is not per unit.</summary>
        private static readonly IReadOnlyList<string> Vocabulary =
            [Squad, Condition, Loadout, Fight, Perception];

        [Observes]
        public IReadOnlyList<DebugRow> Describe()
        {
            if (id < 0)
            {
                return [new DebugRow(Condition, "id", Number(id), "no such unit; ids start at 0")];
            }

            var side = world.SideOf(id);
            var kit = world.KitOf(id);
            var health = world.HealthOf(id);
            var hitPoints = world.HitPointsOf(id);
            var (rank, rankNote) = Rank();
            var (points, pointsNote) = Contribution();

            var rows = new List<DebugRow>(SquadRows())
            {
                new(Condition, "id", Number(id)),

                new(Condition, "side", Number(side), "whose eyes the perception rows below are read through"),

                health <= 0.0
                    ? new(Condition, "health", "0", $"none of its {Amount(hitPoints)} hit points left; it is down")
                    : new(
                        Condition,
                        "health",
                        Percent(health),
                        $"of full: {Amount(health * hitPoints)} of {Amount(hitPoints)} hit points"),

                new(Condition, "armour", world.ArmourOf(id), "the class a shot at it is judged against"),

                new(Condition, "rank", rank, rankNote),

                new(Condition, "contribution", points, pointsNote),

                world.ExposureTicksOf(id) == 0
                    ? new(Condition, "exposed", "0 ticks", "it has never stood within reach of a scripted threat")
                    : new(
                        Condition,
                        "exposed",
                        $"{Number(world.ExposureTicksOf(id))} ticks",
                        $"spent within {Amount(world.ExposureRadius)} steps of a scripted threat, " +
                        "and it never falls"),
            };

            if (kit is null)
            {
                rows.Add(new DebugRow(
                    Loadout,
                    "kit",
                    "none",
                    "nobody enlisted it; it can be shot, as unarmoured, and never shoots"));
            }
            else
            {
                var (gun, gunNote) = Gun(kit.Weapon);
                var (sight, sightNote) = Eyes(kit);

                rows.Add(new DebugRow(Loadout, "name", kit.Name));
                rows.Add(new DebugRow(Loadout, "weapon", gun, gunNote));
                rows.Add(new DebugRow(Loadout, "armour", kit.Armour));
                rows.Add(new DebugRow(Loadout, "range", $"{Amount(kit.Range)} steps", "how far it can shoot"));
                rows.Add(new DebugRow(Loadout, "sight", sight, sightNote));
                rows.Add(kit.ShotsPerSecond == 0.0
                    ? new DebugRow(Loadout, "rate of fire", "0", "it cannot shoot")
                    : new DebugRow(
                        Loadout, "rate of fire", Amount(kit.ShotsPerSecond), "shots a second"));
                rows.Add(new DebugRow(
                    Loadout, "hit points", Amount(kit.HitPoints), "to lose at full health"));
            }

            var (target, targetNote) = Target();
            rows.Add(new DebugRow(Fight, "target", target, targetNote));

            var view = world.View;
            rows.Add(view.AsOf < 0
                ? new DebugRow(Perception, "as of", "no edge yet", "this side has not looked")
                : new DebugRow(
                    Perception, "as of", $"tick {Number(view.AsOf)}", $"when side {Number(side)} last looked"));

            rows.Add(view.PeekHostiles(side).Count == 0
                ? new DebugRow(
                    Perception, "can see", "none", $"nothing hostile to side {Number(side)} is in view")
                : new DebugRow(
                    Perception,
                    "can see",
                    $"{Number(view.PeekHostiles(side).Count)} cells",
                    $"hostile to side {Number(side)}"));

            var (memory, memoryNote) = Memory(view, side);
            rows.Add(new DebugRow(Perception, "remembers", memory, memoryNote));

            rows.Add(view.PeekRepairPoints(side).Count == 0
                ? new DebugRow(
                    Perception, "pads in view", "none", $"side {Number(side)} cannot plan a retreat to one")
                : new DebugRow(
                    Perception,
                    "pads in view",
                    Number(view.PeekRepairPoints(side).Count),
                    "it can plan to reach"));

            return rows;
        }

        /// <summary>
        /// The squad this unit belongs to, as its doctrine sees it. The only
        /// rows in this view whose subject is a GROUP.
        /// </summary>
        /// <remarks>
        /// <b>Every row here is something a doctrine branched on.</b> A guard
        /// checks the anchor and marches the squad to its station if there is
        /// none; a repair policy counts who is on station against its reserve,
        /// reads who is already away and where to, and pulls whoever has fallen
        /// under their own rank's threshold. So the anchor, the roster, the
        /// on-station count, the errands and the worst health in the squad are
        /// the inputs to what the reader just watched happen -- and a row that
        /// answered nothing a doctrine asked would be decoration.
        /// <para>
        /// <b>What is NOT repeated here is what the squad can see.</b>
        /// <c>Hostiles</c>, <c>Sightings</c> and <c>RepairPoints</c> on the
        /// squad seam are the side's perception, and the Perception group is
        /// already that same side's, read from the same place. Two copies of one
        /// list under two headings would invite a reader to hunt for the
        /// difference between them.
        /// </para>
        /// <para>
        /// <b>What cannot be shown is the doctrine's own numbers</b> -- the
        /// reserve, the retreat thresholds by rank, the return threshold. They
        /// live on the doctrine, not on the seam it is handed, so a view built
        /// out of <see cref="ISquadView"/> cannot reach them and does not guess.
        /// </para>
        /// </remarks>
        private IReadOnlyList<DebugRow> SquadRows()
        {
            ISquadView? squad = null;
            foreach (var candidate in squads)
            {
                if (candidate.Members.Contains(id))
                {
                    squad = candidate;
                    break;
                }
            }

            // ANSWERED, NOT OMITTED. A group that vanished would read as a fault
            // in the panel; belonging to no squad is a fact about the unit and a
            // common one -- a unit nobody enlisted, a wave already wiped out.
            if (squad is null)
            {
                return
                [
                    new DebugRow(
                        Squad,
                        "squad",
                        "none",
                        squads.Count == 0
                            ? "nobody handed this view a squad, so there is no doctrine here to read"
                            : $"in none of the {Number(squads.Count)} on the board; no doctrine decides for it"),
                ];
            }

            var members = squad.Members;
            var away = squad.Away;

            var rows = new List<DebugRow>
            {
                new(Squad, "squad", squad.Name, "the membership a doctrine advances; it outlives every order"),

                squad.Anchor < 0
                    ? new(
                        Squad,
                        "anchor",
                        "none",
                        "never moved as a group, so a doctrine holding a station has yet to march to it")
                    : new(
                        Squad,
                        "anchor",
                        Cell(squad, squad.Anchor),
                        "where it is stationed: the destination of its last group move"),

                new(Squad, "members", Number(members.Count), "still able to act; a casualty stops being listed"),

                // THE NUMBER THAT EXPLAINS A UNIT LEFT STANDING HURT. A repair
                // policy stops detaching once this reaches its reserve, and
                // there is nowhere else on the panel to read it.
                new(
                    Squad,
                    "on station",
                    Number(members.Count - away.Count),
                    "with the squad rather than away, and what a repair reserve is measured against"),
            };

            rows.Add(away.Count == 0
                ? new DebugRow(Squad, "away", "none", "every member is with the squad")
                : new DebugRow(Squad, "away", Number(away.Count), Errands(squad, away)));

            rows.Add(Weakest(squad, away));
            return rows;
        }

        /// <summary>Where the ones away are headed, so an errand is more than a count.</summary>
        /// <remarks>
        /// A repair policy sends a member to a pad, so naming the destination is
        /// what tells a reader whether one is off being mended or off somewhere
        /// else entirely. A long list is cut short rather than filling a tooltip
        /// nobody can read.
        /// </remarks>
        private static string Errands(ISquadView squad, IReadOnlyList<int> away)
        {
            var text = new StringBuilder("away on errands of their own: ");
            for (var i = 0; i < away.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                if (i == 4)
                {
                    text.Append("and ").Append(Number(away.Count - i)).Append(" more");
                    break;
                }

                text.Append("agent ").Append(Number(away[i]))
                    .Append(" to ").Append(Cell(squad, squad.ErrandOf(away[i])));
            }

            return text.ToString();
        }

        /// <summary>
        /// The member with the least health left: the pressure a repair doctrine
        /// is answering, and the one squad-wide fact no per-unit row can carry.
        /// </summary>
        /// <remarks>
        /// Not the same as who goes next -- a policy spending a scarce pad on the
        /// lowest RANK first will pass over this one -- so the row says what it
        /// is, and the rank beside it lets a reader work out the rest.
        /// </remarks>
        private static DebugRow Weakest(ISquadView squad, IReadOnlyList<int> away)
        {
            var weakest = -1;
            var lowest = double.PositiveInfinity;
            foreach (var member in squad.Members)
            {
                var health = squad.HealthOf(member);
                if (health < lowest)
                {
                    lowest = health;
                    weakest = member;
                }
            }

            return weakest < 0
                ? new DebugRow(Squad, "weakest", "none", "there is nobody left in the squad to be hurt")
                : new DebugRow(
                    Squad,
                    "weakest",
                    $"agent {Number(weakest)} at {Percent(lowest)}",
                    $"rank {Number(squad.RankOf(weakest))}, " +
                    (away.Contains(weakest) ? "already away" : "on station") +
                    "; nobody in the squad is worse off");
        }

        /// <summary>
        /// A cell written the way the movement layer writes one, built from the
        /// two geometry questions the squad seam answers.
        /// </summary>
        private static string Cell(ISquadView squad, int cell) => cell < 0
            ? "-"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{squad.ColumnOf(cell)},{squad.RowOf(cell)} (#{cell})");

        private static string Percent(double fraction) =>
            string.Create(CultureInfo.InvariantCulture, $"{fraction * 100.0:0.#}%");

        private static (string Value, string Note) Gun(Weapon weapon) =>
            weapon.BlastCells == 0
                ? (weapon.Name, $"{Amount(weapon.BaseDamage)} hit points a shot, single target")
                : (weapon.Name,
                   $"{Amount(weapon.BaseDamage)} hit points a shot, " +
                   $"{Number(weapon.BlastCells)} cells of blast with falloff");

        /// <summary>
        /// Sight against reach, because the gap between them is the only thing
        /// either number means on its own.
        /// </summary>
        /// <remarks>
        /// Only the short case is interpreted. Seeing past your own gun is what
        /// every unit does so it can bring the gun to bear at full reach; seeing
        /// less far than you shoot is the one that needs somebody else.
        /// </remarks>
        private static (string Value, string Note) Eyes(Kit kit) => kit.Sight > kit.Range
            ? ($"{Amount(kit.Sight)} steps",
               $"it can see {Amount(kit.Sight - kit.Range)} past its own reach")
            : kit.Sight < kit.Range
                ? ($"{Amount(kit.Sight)} steps",
                   $"{Amount(kit.Range - kit.Sight)} short of its reach; it needs somebody to spot for it")
                : ($"{Amount(kit.Sight)} steps", "exactly its reach");

        private (string Value, string? Note) Rank()
        {
            var rank = world.RankOf(id);
            var top = world.RankAt.Count;
            return world.IsFullRank(id)
                ? ($"{Number(rank)} of {Number(top)}", "top of the table, so it heals itself wherever it stands")
                : ($"{Number(rank)} of {Number(top)}", null);
        }

        private (string Value, string Note) Contribution()
        {
            var points = world.ContributionOf(id);
            var rank = world.RankOf(id);
            var table = world.RankAt;
            return rank < table.Count
                ? ($"{Amount(points)} points",
                   $"banked, {Amount(table[rank] - points)} short of rank {Number(rank + 1)}")
                : ($"{Amount(points)} points", "banked, with nothing above the rank it holds");
        }

        /// <summary>
        /// Who it was shooting at when the last edge ended, which is a per-tick
        /// fact and never a standing order.
        /// </summary>
        private (string Value, string Note) Target()
        {
            var target = world.TargetOf(id);
            return target < 0
                ? ("none", "it was shooting at nobody when the last edge ended")
                : ($"unit {Number(target)}",
                   $"on side {Number(world.SideOf(target))}, at {Percent(world.HealthOf(target))} health");
        }

        /// <summary>
        /// What the side knows rather than what it can see, aged against the edge
        /// it is being read at -- the gap doctrine decides forgetting on.
        /// </summary>
        private static (string Value, string Note) Memory(IPerceptionView view, int side)
        {
            var known = view.PeekSightings(side);
            if (known.Count == 0)
            {
                return ("nothing", $"side {Number(side)} has no memory of an enemy it cannot currently see");
            }

            var stalest = view.AsOf;
            foreach (var sighting in known)
            {
                if (sighting.Tick < stalest)
                {
                    stalest = sighting.Tick;
                }
            }

            var age = view.AsOf - stalest;
            return age == 0
                ? ($"{Number(known.Count)} enemy units", "every one of them in sight this edge")
                : ($"{Number(known.Count)} enemy units", $"the stalest last seen {Number(age)} ticks ago");
        }
    }
}
