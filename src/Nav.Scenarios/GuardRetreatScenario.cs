namespace Nav.Scenarios;

/// <summary>
/// The guard that does not die beside the cannon, built and left standing:
/// the map, the two sides, the kits, the pads, the station, the doctrines, and
/// a world with fog on -- with no tick played and nothing written down.
/// </summary>
/// <remarks>
/// The C&amp;C behaviour this project was started over, as a WORLD rather than as
/// a recording of one. A demo plays it and narrates a trace; a viewer plays it
/// and draws it. Neither is the other's dependency, and both get the same eight
/// guards on the same map, because there is one construction and this is it.
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
/// is hurt, so a unit past its threshold waits for a place.
/// </para>
/// <para>
/// <b>Nothing is scripted but the waves.</b> There is no <c>SetHealth</c>.
/// Every casualty on either side is a consequence of where a unit stood, what
/// it carried, and what chose to shoot it.
/// </para>
/// <para>
/// <b>The waves are the one thing left to the caller.</b> <see cref="SendWave"/>
/// takes a tick and answers whether anything arrives on it, because a wave is
/// timed against a clock and this type does not own one. Everything else is
/// standing before the constructor returns.
/// </para>
/// </remarks>
public sealed class GuardRetreatScenario
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

    private static readonly int[] Arrivals = [0, 160, 320];

    private readonly List<Squad> _waves = [];
    private readonly int _attackStation;

    /// <exception cref="FileNotFoundException">No <c>config/</c> holding the combat or scale table above the binary.</exception>
    public GuardRetreatScenario()
    {
        Grid = Grid.FromMapText(Map);
        Board = new MovementSystem(Grid);
        var combat = Combat.From(Ini.FromFile(ConfigPath("combat.ini")));
        var scale = WorldScale.From(Ini.FromFile(ConfigPath("scale.ini")));

        var station = Grid.Index(24, 16);

        // Opposite corners, and twenty-two cells from the station -- three times
        // what a tank can see. Under fog the guards know these are here only
        // because a pad watches its own ground; nothing on the line can see
        // either of them.
        var padNorth = Grid.Index(2, 2);
        var padSouth = Grid.Index(46, 30);

        // Where a wave goes to stand: the mouth of the north corridor, in reach
        // of the ring's front arc. A wave that holds there is a wave that is
        // shot at from the line and shoots back, which is the whole fight.
        _attackStation = Grid.Index(24, 11);

        // Rank at a kill's worth and three kills' worth, given the credit rates
        // in the config. Self-healing so a full-rank guard mends on the walk;
        // no exposure damage, because the enemy has weapons now.
        World = new DemoWorld(
            Grid,
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
        World.RepairCells.Add(padNorth);
        World.RepairCells.Add(padSouth);

        // Eight guards, starting scattered down the west edge so the march to
        // station is itself worth watching -- and long enough now that they
        // arrive having seen almost nothing of the map they crossed.
        int[] starts =
        [
            Grid.Index(1, 10), Grid.Index(2, 12), Grid.Index(1, 14), Grid.Index(2, 16),
            Grid.Index(1, 18), Grid.Index(2, 20), Grid.Index(1, 22), Grid.Index(2, 24),
        ];
        for (var i = 0; i < starts.Length; i++)
        {
            var id = Board.AddAgent(starts[i], side: 0);
            World.Enlist(id, GuardKits[i]);
        }

        Guard = new Squad(
            "guard",
            Enumerable.Range(0, Guards),
            new GuardDoctrine(station, new RepairPolicy(RetreatByRank, returnAbove: 0.8, reserve: Reserve)));

        // Listening is part of standing the world up, not part of playing it:
        // the world's whole knowledge of the board comes through the broadcast,
        // and a caller that had to remember to switch it on could take a first
        // doctrine pass with both sides blind.
        World.Listen(Board);
    }

    /// <summary>How many guards hold the line. Their ids are 0 to one below this.</summary>
    public const int Guards = 8;

    /// <summary>How many guards must stay on station however hurt the squad is.</summary>
    public const int Reserve = 5;

    /// <summary>Retreat thresholds by rank: rookie, regular, veteran.</summary>
    /// <remarks>
    /// Ascending, so a veteran is pulled at a scratch and a rookie holds to half
    /// health. See <see cref="RepairPolicy"/> for why that is the right way up.
    /// </remarks>
    public static IReadOnlyList<double> RetreatByRank { get; } = [0.4, 0.55, 0.7];

    /// <summary>Ticks a wave arrives on, ascending.</summary>
    public static IReadOnlyList<int> WaveTicks => Arrivals;

    /// <summary>The map the cells are indices into.</summary>
    public Grid Grid { get; }

    /// <summary>
    /// The board: the movement system every side moves on, with the guards
    /// already placed and the waves still to come.
    /// </summary>
    public MovementSystem Board { get; }

    /// <summary>Health, kits, pads, fire and fog: the world the sides are settled against each tick.</summary>
    public DemoWorld World { get; }

    /// <summary>The line: eight units under a <see cref="GuardDoctrine"/> holding the centre.</summary>
    public Squad Guard { get; }

    /// <summary>Waves that have arrived, in the order they did. Empty until the first <see cref="SendWave"/>.</summary>
    public IReadOnlyList<Squad> Waves => _waves;

    /// <summary>
    /// Puts the wave due on <paramref name="tick"/> on the board, or answers null
    /// on a tick no wave is due.
    /// </summary>
    /// <remarks>
    /// The caller owns the clock, so it asks every tick rather than being told.
    /// The wave is a squad like the line is, with its own station and no pad to
    /// fall back to; what happens to it afterwards is doctrine, not script.
    /// </remarks>
    /// <returns>
    /// The wave, standing on the board and under no orders yet. What either side
    /// can see of it is settled at the end of the tick like everything else.
    /// </returns>
    public Squad? SendWave(int tick)
    {
        var index = Array.IndexOf(Arrivals, tick);
        if (index < 0)
        {
            return null;
        }

        var ids = new List<int>();
        for (var k = 0; k < Wave.Length; k++)
        {
            var id = Board.AddAgent(Grid.Index(21 + k, 0), side: 1);
            World.Enlist(id, Wave[k]);
            ids.Add(id);
        }

        var wave = new Squad(
            $"wave {index + 1}", ids,
            new GuardDoctrine(_attackStation, retreatBelow: 0.0, returnAbove: 0.5));
        _waves.Add(wave);
        return wave;
    }

    /// <summary>
    /// A file under the repository's <c>config/</c>, found by walking up from
    /// the binary, so a scenario run from the root and one run from bin read the
    /// same numbers. Throws rather than falling back: a world built on numbers
    /// nobody chose is the failure the config files exist to prevent.
    /// </summary>
    /// <exception cref="FileNotFoundException">No <c>config/</c> holding the file above the binary.</exception>
    private static string ConfigPath(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config", file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"No config/{file} above {AppContext.BaseDirectory}.", file);
    }
}
