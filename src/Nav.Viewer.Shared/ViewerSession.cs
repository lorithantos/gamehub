using System.Diagnostics.CodeAnalysis;

using Nav.Core;

namespace Nav.Viewer;

/// <summary>
/// The single owner of what is loaded and what the simulation says about it.
/// </summary>
/// <remarks>
/// Before this type existed, "what is loaded" had no owner: it was smeared
/// across constructor arguments, two near-duplicate <c>Program.Main</c>s, and
/// <c>ViewerApp</c>'s private fields — and the replay-restart code was session
/// management hand-rolled inside an input handler. The session is the reference
/// point for everyone else: the app translates input into these commands and
/// draws from these properties; the hosts present what the app draws.
/// <para>
/// The line through the middle is deliberate. Content and simulation live here:
/// the grid, the scenario, the movement system, the pending recorded orders,
/// tick, running, and the selection the commands operate on. Presentation does
/// not: layout, the terrain image, frame blending, and the drag rectangle are
/// the app's; windows and renderers are the hosts'. Wall-clock accumulation is
/// presentation too — the app decides <em>when</em> to call <see cref="Tick"/>;
/// the session decides only whether time may run and what a tick means.
/// </para>
/// <para>
/// Loading is where refuse-don't-repair lives, so loading is a session concern:
/// <see cref="TryLoad"/> is the one implementation of the refusal path the two
/// host executables used to carry copies of.
/// </para>
/// </remarks>
public sealed class ViewerSession
{
    /// <summary>Units placed on a map opened without a scenario.</summary>
    public const int DefaultSquad = 24;

    private const double DefaultTickSeconds = 1.0 / 60.0;

    private readonly List<int> _selection = [];

    // Not readonly: restarting a replay rebuilds both.
    private MovementSystem _system;
    private Queue<ScenarioOrder> _orders;

    private ViewerSession(Grid grid, string mapName, RecordedScenario? scenario, int squad)
    {
        Grid = grid;
        MapName = mapName;
        Scenario = scenario;

        if (scenario is not null)
        {
            // A replay loads with the clock STOPPED at tick zero, so the
            // recorded placements can be looked at before Space runs them.
            (_system, _orders) = BuildReplay();
            Running = false;
        }
        else
        {
            _system = new MovementSystem(grid);
            _orders = new Queue<ScenarioOrder>();
            Running = true;

            // A squad to look at before the first click, on any map.
            var placed = 0;
            for (var cell = 0; cell < grid.CellCount && placed < squad; cell++)
            {
                if (!grid.IsPassable(cell))
                {
                    continue;
                }

                _system.AddAgent(cell);
                placed++;
            }
        }

        if (_system.Agents.Count > 0)
        {
            _selection.Add(0);
        }
    }

    public Grid Grid { get; }

    /// <summary>What the title bar and error messages call this content.</summary>
    public string MapName { get; }

    public RecordedScenario? Scenario { get; }

    public bool IsReplay => Scenario is not null;

    public double TickSeconds => Scenario?.TickSeconds ?? DefaultTickSeconds;

    public bool Running { get; private set; }

    public int CurrentTick => _system.CurrentTick;

    public IReadOnlyList<AgentState> Agents => _system.Agents;

    public TickReport LastTick => _system.LastTick;

    /// <summary>The units orders go to, in id order.</summary>
    public IReadOnlyList<int> Selection => _selection;

    public IReadOnlyList<AgentPlan> CurrentPlans() => _system.CurrentPlans();

    public static ViewerSession FromMap(Grid grid, string mapName, int squad = DefaultSquad)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        return new ViewerSession(grid, mapName, scenario: null, squad);
    }

    public static ViewerSession FromScenario(Grid grid, string mapName, RecordedScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        ArgumentNullException.ThrowIfNull(scenario);
        return new ViewerSession(grid, mapName, scenario, squad: 0);
    }

    /// <summary>
    /// Parsed options to a ready session, or the reason there is none. The one
    /// implementation of the load-refusal path, shared by both hosts.
    /// </summary>
    public static bool TryLoad(
        ViewerOptions options, [NotNullWhen(true)] out ViewerSession? session, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        session = null;
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

        try
        {
            session = scenario is null
                ? FromMap(grid, mapName)
                : FromScenario(grid, mapName, scenario);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // A scenario agent on a wall or off the map. The message names the
            // cell; refusing beats a window of units that were never placed.
            error = ex.Message;
            return false;
        }

        return true;
    }

    public void SetRunning(bool running) => Running = running;

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
    public void OrderEveryone(int goalCell) =>
        _system.Order([.. Enumerable.Range(0, _system.Agents.Count)], goalCell);

    /// <summary>
    /// One simulation step: recorded orders due at this tick are issued exactly
    /// as the headless playback issues them, then the world advances. Runs when
    /// asked even while <see cref="Running"/> is false — single-stepping a
    /// paused world is a legitimate thing for a caller to do.
    /// </summary>
    public void Tick()
    {
        while (_orders.TryPeek(out var order) && order.Tick <= _system.CurrentTick)
        {
            _orders.Dequeue();
            _system.Order(order.Agents, Grid.Index(order.X, order.Y));
        }

        _system.Tick();
    }

    /// <summary>
    /// Reloads the recording: tick zero, clock stopped, orders re-queued,
    /// selection back to unit 0. Refused on a session with no scenario.
    /// </summary>
    public void Restart()
    {
        if (Scenario is null)
        {
            throw new InvalidOperationException("nothing to restart: this session has no scenario.");
        }

        (_system, _orders) = BuildReplay();
        _selection.Clear();
        _selection.Add(0);
        Running = false;
    }

    private (MovementSystem System, Queue<ScenarioOrder> Orders) BuildReplay()
    {
        var system = new MovementSystem(Grid);
        foreach (var agent in Scenario!.Agents)
        {
            system.AddAgent(Grid.Index(agent.X, agent.Y));
        }

        return (system, new Queue<ScenarioOrder>(Scenario.Orders));
    }
}
