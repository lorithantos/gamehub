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
    private const string World = "World";
    private const string Rates = "Rates";
    private const string Ranks = "Rank";

    private readonly DemoWorld _world;

    /// <param name="world">The world to read. Never written to.</param>
    public DemoWorldDebugView(DemoWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

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
    public IDebugView DebugFor(int agent) => new UnitDebugView(_world, agent);

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
    private sealed class UnitDebugView(DemoWorld world, int id) : IDebugView
    {
        private const string Unit = "Unit";
        private const string Loadout = "Kit";
        private const string Fight = "Fight";
        private const string Perception = "Perception";

        [Observes]
        public IReadOnlyList<DebugRow> Describe()
        {
            if (id < 0)
            {
                return [new DebugRow(Unit, "id", Number(id), "no such unit; ids start at 0")];
            }

            var side = world.SideOf(id);
            var kit = world.KitOf(id);
            var health = world.HealthOf(id);
            var hitPoints = world.HitPointsOf(id);
            var (rank, rankNote) = Rank();
            var (points, pointsNote) = Contribution();

            var rows = new List<DebugRow>
            {
                new(Unit, "id", Number(id)),

                new(Unit, "side", Number(side), "whose eyes the perception rows below are read through"),

                health <= 0.0
                    ? new(Unit, "health", "0", $"none of its {Amount(hitPoints)} hit points left; it is down")
                    : new(
                        Unit,
                        "health",
                        Percent(health),
                        $"of full: {Amount(health * hitPoints)} of {Amount(hitPoints)} hit points"),

                new(Unit, "armour", world.ArmourOf(id), "the class a shot at it is judged against"),

                new(Unit, "rank", rank, rankNote),

                new(Unit, "contribution", points, pointsNote),

                world.ExposureTicksOf(id) == 0
                    ? new(Unit, "exposed", "0 ticks", "it has never stood within reach of a scripted threat")
                    : new(
                        Unit,
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
