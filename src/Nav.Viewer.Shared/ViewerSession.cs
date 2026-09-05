using System.Diagnostics.CodeAnalysis;

using Nav.Core;

namespace Nav.Viewer;

/// <summary>
/// The single owner of what is loaded and what the simulation says about it.
/// </summary>
/// <remarks>
/// The reference point for everyone else. The app translates input into these
/// commands and draws from these properties; the hosts present what the app
/// draws. See <c>docs/viewer.md</c>.
/// <para>The line through the middle is deliberate:</para>
/// <list type="bullet">
/// <item><description><b>Here</b> — grid, scenario, movement system, pending
/// orders, tick, running, selection.</description></item>
/// <item><description><b>The app's</b> — layout, terrain image, frame blending,
/// drag rectangle, and wall-clock accumulation.</description></item>
/// <item><description><b>The hosts'</b> — windows and renderers.</description></item>
/// </list>
/// <para>
/// So the app decides <em>when</em> to call <see cref="Tick"/>; the session
/// decides only whether time may run and what a tick means.
/// </para>
/// <para>
/// Loading is where refuse-don't-repair lives, so loading is a session concern.
/// <see cref="TryLoad"/> and <see cref="TryLoadFile"/> are the one
/// implementation of the refusal path.
/// </para>
/// <para>
/// A refused load changes <b>nothing</b>: the candidate world is built off to
/// the side and adopted only whole.
/// </para>
/// <para>
/// Every successful load bumps <see cref="Version"/>, which is how everyone
/// downstream knows their derived state is stale.
/// </para>
/// </remarks>
public sealed class ViewerSession
{
    /// <summary>Units placed on a map opened without a scenario.</summary>
    public const int DefaultSquad = 24;

    private static readonly double DefaultTickSeconds = WorldScale.Default.SecondsPerTick;

    private readonly List<int> _selection = [];

    /// <summary>
    /// How a live world is built, or null for a map or a recording. Kept rather
    /// than spent, because <see cref="Restart"/> is the second call to it.
    /// </summary>
    /// <remarks>
    /// <b>A factory and not a world, and that is the whole of why this path
    /// exists in this shape.</b> A world composes its map, its sides, their kits
    /// and their doctrine in its constructor and then only ever moves forward;
    /// there is no rewind on it and there should not be one. So the way back to
    /// tick zero is to build another, which means the session has to be able to
    /// ask -- a session handed an instance could load, tick and draw, but R
    /// would be a key that did nothing.
    /// </remarks>
    private Func<IWorld>? _worldFactory;

    // Not readonly: loading, replay-restart and world-restart rebuild them.
    private MovementSystem _system;
    private Queue<ScenarioOrder> _orders;
    private IWorld? _world;

    private ViewerSession(Grid grid, string mapName, RecordedScenario? scenario, int squad)
    {
        Grid = grid;
        MapName = mapName;
        Scenario = scenario;
        (_system, _orders) = BuildBoard(grid, scenario, squad);
        Running = scenario is null;
        ResetSelection();
    }

    private ViewerSession(Func<IWorld> world, string name)
    {
        _worldFactory = world;
        _world = Build(world);
        _orders = new Queue<ScenarioOrder>();
        _system = _world.Board;
        Grid = _world.Grid;
        MapName = name;
        Scenario = null;

        // Running, where a recording loads paused. A recording has an opening
        // worth reading before it bursts to the end; a world's opening IS its
        // first tick -- the guards are scattered and nothing has decided
        // anything yet, and there is nothing to study in that.
        Running = true;
        ResetSelection();
    }

    /// <summary>
    /// The map in play. Replaced whole by a successful load, never edited in
    /// place -- so anything derived from it is stale once <see cref="Version"/>
    /// moves.
    /// </summary>
    public Grid Grid { get; private set; }

    /// <summary>What the title bar and error messages call this content.</summary>
    public string MapName { get; private set; }

    /// <summary>
    /// The recording being replayed, or <c>null</c> for a free map. It supplies
    /// the unit placements, the queue of orders <see cref="Tick"/> issues, and
    /// <see cref="TickSeconds"/>.
    /// </summary>
    public RecordedScenario? Scenario { get; private set; }

    /// <summary>
    /// Bumped by every successful load. Anything derived from the content —
    /// terrain image, layout, window size — is stale when this moved.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// True when a <see cref="Scenario"/> is loaded. That is also why the clock
    /// started stopped, and it is the condition <see cref="Restart"/> requires.
    /// </summary>
    public bool IsReplay => Scenario is not null;

    /// <summary>
    /// True when this session is driving a live world rather than a map or a
    /// recording: <see cref="Tick"/> is that world's own step, and there are no
    /// recorded orders to issue.
    /// </summary>
    public bool IsLiveWorld => _world is not null;

    /// <summary>
    /// Whether <see cref="Restart"/> would work: there is a recording to reload
    /// or a world to build again. False on a free map, where the nearest thing
    /// to a reset is ordering everybody home.
    /// </summary>
    /// <remarks>
    /// A separate question from <see cref="IsReplay"/> on purpose. The two
    /// answered the same until a world could be restarted as well, and a caller
    /// asking "can I restart" through "is this a recording" would have been
    /// right by coincidence.
    /// </remarks>
    public bool CanRestart => Scenario is not null || _worldFactory is not null;

    /// <summary>
    /// Simulated seconds one tick represents: the scenario's recorded rate, or
    /// <see cref="WorldScale.Default"/>'s quarter second for a free map — which
    /// the status line reads as four ticks a second. It sets how fast a running
    /// clock feeds ticks, not what a tick does -- the simulation is the same
    /// either way.
    /// </summary>
    public double TickSeconds => Scenario?.TickSeconds ?? DefaultTickSeconds;

    /// <summary>
    /// Whether the driver may advance time on its own. It gates the clock, not
    /// the mechanics: a paused session still accepts <see cref="Tick"/>,
    /// selection and orders, which is what makes single-stepping possible.
    /// </summary>
    public bool Running { get; private set; }

    /// <summary>
    /// Ticks elapsed since the world was built. Back to zero after a load or a
    /// <see cref="Restart"/>, because both build a new world.
    /// </summary>
    public int CurrentTick => _system.CurrentTick;

    /// <summary>
    /// Every unit as it stands right now, indexed by agent id. A fresh snapshot
    /// on each read, not a live view -- one taken before <see cref="Tick"/> still
    /// describes the tick it was taken in, which is what frame blending needs.
    /// </summary>
    public IReadOnlyList<AgentState> Agents => _system.Agents;

    /// <summary>
    /// What the most recent <see cref="Tick"/> cost: nodes expanded, searches
    /// started, finished and abandoned, and how many agents ended it still
    /// queued for a planning slot. Replaced by every tick, not accumulated.
    /// </summary>
    [Observes]
    public TickReport LastTick => _system.LastTick;

    /// <summary>The units orders go to, in id order.</summary>
    public IReadOnlyList<int> Selection => _selection;

    /// <summary>
    /// The routes as they currently stand. Only agents that have a plan appear,
    /// so this is usually shorter than <see cref="Agents"/> and is keyed by id
    /// rather than indexed by it.
    /// </summary>
    [Observes]
    public IReadOnlyList<AgentPlan> CurrentPlans() => _system.CurrentPlans();

    /// <summary>Each live group's leader, for the viewer's mark.</summary>
    [Observes]
    public IReadOnlyList<int> Leaders => _system.Leaders;

    /// <summary>
    /// What the movement layer knows about one unit, for an instrument to show a
    /// human. Any id is answered, including one that was never issued.
    /// </summary>
    /// <remarks>
    /// A passthrough for the same reason <see cref="CurrentPlans"/> and
    /// <see cref="Leaders"/> are: the system stays private, and a caller gets the
    /// one narrow surface it needs rather than a handle it could plan or order
    /// with. Nothing may branch on what comes back -- see
    /// <see cref="IDebugView"/>.
    /// </remarks>
    [Observes]
    public IDebugView DebugFor(int agent) => _system.DebugFor(agent);

    /// <summary>Distance fields cached, of <see cref="MovementSystem.FieldCapacity"/>.</summary>
    [Observes]
    public int LiveFields => _system.LiveFields;

    /// <summary>
    /// A map with no recording: <paramref name="squad"/> units on the first
    /// passable cells, unit 0 selected, and the clock already running -- there is
    /// no recorded opening to look at first.
    /// </summary>
    public static ViewerSession FromMap(Grid grid, string mapName, int squad = DefaultSquad)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        return new ViewerSession(grid, mapName, scenario: null, squad);
    }

    /// <summary>
    /// A replay of <paramref name="scenario"/>: its placements, its orders queued
    /// at their recorded ticks, <b>paused at tick zero</b> so the setup can be
    /// read -- and stepped -- before it bursts to the end.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="MapFormatException"/> when <paramref name="grid"/> is not
    /// the size the scenario was recorded against, and
    /// <see cref="ArgumentOutOfRangeException"/> when a recorded placement is off
    /// the map or on a wall. <see cref="TryLoad"/> is the path that turns either
    /// into a named refusal rather than a stack trace.
    /// </remarks>
    public static ViewerSession FromScenario(Grid grid, string mapName, RecordedScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentNullException.ThrowIfNull(scenario);

        // The same guard playback runs, for the same reason. The viewer usually
        // resolves the map from the scenario's own name, so this fires when the
        // map has been edited since the recording rather than when someone paired
        // the wrong two files -- which is the likelier mistake of the two.
        scenario.EnsureMatches(grid);

        return new ViewerSession(grid, mapName, scenario, squad: 0);
    }

    /// <summary>
    /// A session driving a live world: <paramref name="world"/> is called once
    /// now for the world to play, and again by every <see cref="Restart"/>.
    /// Starts running, because a world's setup is its first tick.
    /// </summary>
    /// <remarks>
    /// <b>The session drives it and cannot look inside it.</b> Everything on
    /// <see cref="IWorld"/> is a Nav.Core type, which is the only reason this
    /// path can be here at all -- a host that also wants the world's own numbers
    /// on screen keeps its own handle on what the factory built and hands the
    /// viewer a debug source for it. See <c>IWorldDebugView</c>.
    /// <para>
    /// <paramref name="name"/> is what the title bar calls it; the world has no
    /// map file to be named after.
    /// </para>
    /// </remarks>
    /// <param name="world">
    /// Builds a world standing at tick zero. Called immediately, and once more
    /// per restart; a factory that hands back the same instance twice would make
    /// R a key that did nothing.
    /// </param>
    /// <param name="name">What the title bar calls it, e.g. the world's name.</param>
    public static ViewerSession FromWorld(Func<IWorld> world, string name)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ViewerSession(world, name);
    }

    /// <summary>
    /// Parsed options to a ready session, or the reason there is none. The one
    /// implementation of the load-refusal path, shared by both hosts.
    /// </summary>
    /// <remarks>
    /// Content read from disk, only. <c>--world</c> is resolved by whoever
    /// composed the application, because a world cannot be named from here --
    /// see <see cref="FromWorld"/>.
    /// </remarks>
    public static bool TryLoad(
        ViewerOptions options, [NotNullWhen(true)] out ViewerSession? session, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        session = null;

        if (!TryReadContent(options, out var content, out error))
        {
            return false;
        }

        try
        {
            var (grid, mapName, scenario) = content.Value;
            session = scenario is null
                ? FromMap(grid, mapName)
                : FromScenario(grid, mapName, scenario);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or MapFormatException)
        {
            // A scenario agent on a wall or off the map, or a map that is not the
            // size the scenario was recorded against. The message names the cell
            // or the mismatch; refusing beats a window of units that were never
            // placed, or a replay quietly running on the wrong ground.
            error = ex.Message;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Loads a file into the running session: <c>.scenario</c> replays, anything
    /// else is read as a map. On success the whole world is replaced and
    /// <see cref="Version"/> bumps; on refusal nothing changes and the reason
    /// comes back.
    /// </summary>
    public bool TryLoadFile(string path, [NotNullWhen(false)] out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var isScenario = string.Equals(Path.GetExtension(path), ".scenario", StringComparison.OrdinalIgnoreCase);
        var options = isScenario
            ? new ViewerOptions(null, null, false, path)
            : new ViewerOptions(path, null, false);

        if (!TryReadContent(options, out var content, out error))
        {
            return false;
        }

        var (grid, mapName, scenario) = content.Value;

        // Build the candidate world completely before touching anything, so a
        // refusal leaves the current session exactly as it was.
        MovementSystem system;
        Queue<ScenarioOrder> orders;
        try
        {
            (system, orders) = BuildBoard(grid, scenario, DefaultSquad);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or MapFormatException)
        {
            error = ex.Message;
            return false;
        }

        Grid = grid;
        MapName = mapName;
        Scenario = scenario;
        _system = system;
        _orders = orders;

        // A dropped file replaces the content WHOLE, and on a session that was
        // driving a live world that has to include the world and the way back to
        // it. Keeping the factory would leave R rebuilding the guard fight over
        // the map somebody had just opened.
        _world = null;
        _worldFactory = null;
        Running = scenario is null;
        ResetSelection();
        Version++;
        return true;
    }

    /// <summary>
    /// Starts or stops the clock. It advances nothing by itself: <see cref="Tick"/>
    /// remains the only thing that moves the world, whichever way this is set.
    /// </summary>
    public void SetRunning(bool running) => Running = running;

    /// <summary>
    /// The pause gesture as a command -- <see cref="Running"/> flipped, so a host
    /// binding a key to it needs no idea which state the session is in.
    /// </summary>
    public void ToggleRunning() => Running = !Running;

    /// <summary>Replaces the selection. Ids are kept in ascending order.</summary>
    public void Select(IEnumerable<int> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        _selection.Clear();
        foreach (var id in agents.Order())
        {
            if (id < 0 || id >= _system.Agents.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(agents), id, "no such agent");
            }

            _selection.Add(id);
        }
    }

    /// <summary>Sends the current selection to a cell. No selection, no order.</summary>
    public void OrderSelection(int goalCell)
    {
        if (_selection.Count > 0)
        {
            _system.Order([.. _selection], goalCell);
        }
    }

    /// <summary>Sends every unit to a cell, selected or not.</summary>
    /// <remarks>
    /// The living ones. <see cref="MovementSystem.Order(IReadOnlyList{int}, int)"/> throws on a removed
    /// agent rather than quietly moving a corpse, and this hands it every id there
    /// has ever been -- so one casualty would turn the regroup key into an
    /// exception for the rest of the session.
    /// </remarks>
    public void OrderEveryone(int goalCell) =>
        _system.Order([.. _system.Agents.Where(a => a.Alive).Select(a => a.Id)], goalCell);

    /// <summary>
    /// Takes a unit out of the world: what a casualty is to this layer. It keeps
    /// its id and its last cell, so everything indexing agents by id goes on
    /// working, and <see cref="AgentState.Alive"/> is how the viewer tells a body
    /// from a unit.
    /// </summary>
    /// <remarks>
    /// Dropped from the selection as well, because the selection is what orders
    /// are aimed at and <see cref="MovementSystem.Order(IReadOnlyList{int}, int)"/> refuses a removed
    /// agent -- leaving a corpse selected would arm a right-click to throw.
    /// </remarks>
    public void Remove(int agent)
    {
        _system.Remove(agent);
        _selection.Remove(agent);
    }

    /// <summary>
    /// One simulation step: recorded orders due at this tick are issued exactly
    /// as the headless playback issues them, then the world advances. Runs when
    /// asked even while <see cref="Running"/> is false — single-stepping a
    /// paused world is a legitimate thing for a caller to do.
    /// </summary>
    /// <remarks>
    /// <b>A live world plays its OWN tick, and nothing of the above happens to
    /// it.</b> What runs inside one and in what order is the world's design --
    /// see <see cref="IWorld.Step"/> -- so this hands over the tick number and
    /// stays out of it. There are no recorded orders on that path either: a
    /// world is not a recording of anything.
    /// <para>
    /// The number handed over is the board's tick BEFORE the step, so the first
    /// call is tick zero and each one after is one more -- the same count a
    /// headless run gets from its loop counter, which is what makes the fight
    /// watched here and the fight narrated into a trace the same fight. Anything
    /// a world schedules against the clock, a wave arriving among them, is due
    /// on the number it is given.
    /// </para>
    /// </remarks>
    public void Tick()
    {
        if (_world is { } world)
        {
            world.Step(_system.CurrentTick);
            return;
        }

        while (_orders.TryPeek(out var order) && order.Tick <= _system.CurrentTick)
        {
            _orders.Dequeue();
            _system.Order(order.Agents, Grid.Index(order.X, order.Y));
        }

        _system.Tick();
    }

    /// <summary>
    /// Back to tick zero: the recording reloaded with its orders re-queued and
    /// the clock stopped, or a live world built again from the factory and left
    /// running. Selection back to unit 0 either way. Refused on a free map,
    /// which has nothing to go back to -- see <see cref="CanRestart"/>.
    /// </summary>
    /// <remarks>
    /// <b>It is the construction run again, not a rewind.</b> Each path leaves
    /// the session in the state a freshly built one of its kind is in, down to
    /// whether the clock is running, because "restart" meaning something other
    /// than "as it was at the start" is a trap for whoever presses R.
    /// <para>
    /// A world restart bumps <see cref="Version"/> where a replay restart does
    /// not: a new world is a new map, a new board and a new roster, and
    /// everything derived from those upstream -- terrain, layout, the cells
    /// units are drawn moving from -- is stale in a way a re-queued recording on
    /// the same grid never is.
    /// </para>
    /// </remarks>
    public void Restart()
    {
        if (_worldFactory is { } factory)
        {
            var replacement = Build(factory);
            _world = replacement;
            _system = replacement.Board;
            _orders = new Queue<ScenarioOrder>();
            Grid = replacement.Grid;
            Running = true;
            ResetSelection();
            Version++;
            return;
        }

        if (Scenario is null)
        {
            throw new InvalidOperationException("nothing to restart: this session has no scenario.");
        }

        (_system, _orders) = BuildBoard(Grid, Scenario, squad: 0);
        Running = false;
        ResetSelection();
    }

    /// <summary>
    /// The factory's answer, or the refusal that it handed back nothing.
    /// </summary>
    /// <remarks>
    /// A null world would otherwise be a <c>NullReferenceException</c> from
    /// inside <see cref="Tick"/> a hundred frames later, naming nothing. Both
    /// the first build and every restart come through here.
    /// </remarks>
    private static IWorld Build(Func<IWorld> factory) =>
        factory() ?? throw new InvalidOperationException("the world factory handed back null.");

    /// <summary>
    /// Everything that can refuse about reading content from disk, in one place:
    /// unreadable files, malformed maps and scenarios, all-wall maps.
    /// </summary>
    private static bool TryReadContent(
        ViewerOptions options,
        [NotNullWhen(true)] out (Grid Grid, string MapName, RecordedScenario? Scenario)? content,
        [NotNullWhen(false)] out string? error)
    {
        content = null;
        error = null;

        Grid grid;
        string mapName;
        RecordedScenario? scenario = null;
        try
        {
            if (options.ScenarioPath is { } scenarioFile)
            {
                scenario = RecordedScenario.FromFile(scenarioFile);
                var mapFile = options.MapPath
                    ?? ViewerOptions.ResolveScenarioMap(scenarioFile, scenario.MapName);
                grid = Grid.FromMapFile(mapFile);
                mapName = $"{Path.GetFileName(scenarioFile)} on {Path.GetFileName(mapFile)}";
            }
            else if (options.MapPath is { } path)
            {
                grid = Grid.FromMapFile(path);
                mapName = Path.GetFileName(path);
            }
            else
            {
                grid = Grid.FromMapText(SampleMaps.CornerCutTrap);
                mapName = "(embedded fixture)";
            }
        }
        catch (Exception ex) when (ex is MapFormatException or IOException or UnauthorizedAccessException)
        {
            // The loaders refuse precisely and name the line. Reporting that
            // beats a stack trace, and beats opening a window onto nothing.
            error = ex.Message;
            return false;
        }

        // A map of nothing but walls parses perfectly well -- "@@@" is valid --
        // and then there is no cell to put a unit on.
        if (grid.PassableCount == 0)
        {
            error = $"{mapName} has no passable cell; there is nothing to walk on.";
            return false;
        }

        content = (grid, mapName, scenario);
        return true;
    }

    /// <summary>
    /// A world for the content: recorded placements and the order queue for a
    /// scenario — which loads with the clock stopped, so the setup can be looked
    /// at before Space runs it — or a default squad on the first passable cells
    /// of a free map.
    /// </summary>
    private static (MovementSystem System, Queue<ScenarioOrder> Orders) BuildBoard(
        Grid grid, RecordedScenario? scenario, int squad)
    {
        var system = new MovementSystem(grid);

        if (scenario is not null)
        {
            // Every path that builds a world passes through here -- startup,
            // mid-session load, and restart -- which is why the check lives here
            // and not at the three call sites. It used to sit on FromScenario,
            // where only startup reached it, so a dropped file was accepted on a
            // map the recording had never seen.
            scenario.EnsureMatches(grid);

            foreach (var agent in scenario.Agents)
            {
                system.AddAgent(grid.Index(agent.X, agent.Y));
            }

            return (system, new Queue<ScenarioOrder>(scenario.Orders));
        }

        var placed = 0;
        for (var cell = 0; cell < grid.CellCount && placed < squad; cell++)
        {
            if (!grid.IsPassable(cell))
            {
                continue;
            }

            system.AddAgent(cell);
            placed++;
        }

        return (system, new Queue<ScenarioOrder>());
    }

    private void ResetSelection()
    {
        _selection.Clear();
        if (_system.Agents.Count > 0)
        {
            _selection.Add(0);
        }
    }
}
