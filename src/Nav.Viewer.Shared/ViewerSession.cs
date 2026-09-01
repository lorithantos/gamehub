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
/// <see cref="TryLoad"/> at startup and <see cref="TryLoadFile"/> mid-session
/// are the one implementation of the refusal path. A refused load changes
/// <b>nothing</b>: the candidate world is built off to the side and adopted only
/// whole. Every successful load bumps <see cref="Version"/>, which is how
/// everyone downstream knows their derived state — terrain image, layout,
/// window size — is stale.
/// </para>
/// </remarks>
public sealed class ViewerSession
{
    /// <summary>Units placed on a map opened without a scenario.</summary>
    public const int DefaultSquad = 24;

    private const double DefaultTickSeconds = 1.0 / 60.0;

    private readonly List<int> _selection = [];

    // Not readonly: loading and replay-restart rebuild them.
    private MovementSystem _system;
    private Queue<ScenarioOrder> _orders;

    private ViewerSession(Grid grid, string mapName, RecordedScenario? scenario, int squad)
    {
        Grid = grid;
        MapName = mapName;
        Scenario = scenario;
        (_system, _orders) = BuildWorld(grid, scenario, squad);
        Running = scenario is null;
        ResetSelection();
    }

    public Grid Grid { get; private set; }

    /// <summary>What the title bar and error messages call this content.</summary>
    public string MapName { get; private set; }

    public RecordedScenario? Scenario { get; private set; }

    /// <summary>
    /// Bumped by every successful load. Anything derived from the content —
    /// terrain image, layout, window size — is stale when this moved.
    /// </summary>
    public int Version { get; private set; }

    public bool IsReplay => Scenario is not null;

    public double TickSeconds => Scenario?.TickSeconds ?? DefaultTickSeconds;

    public bool Running { get; private set; }

    public int CurrentTick => _system.CurrentTick;

    public IReadOnlyList<AgentState> Agents => _system.Agents;

    public TickReport LastTick => _system.LastTick;

    /// <summary>The units orders go to, in id order.</summary>
    public IReadOnlyList<int> Selection => _selection;

    public IReadOnlyList<AgentPlan> CurrentPlans() => _system.CurrentPlans();

    /// <summary>Each live group's leader, for the viewer's mark.</summary>
    public IReadOnlyList<int> Leaders => _system.Leaders;

    /// <summary>Distance fields cached, of <see cref="MovementSystem.FieldCapacity"/>.</summary>
    public int LiveFields => _system.LiveFields;

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
        catch (ArgumentOutOfRangeException ex)
        {
            // A scenario agent on a wall or off the map. The message names the
            // cell; refusing beats a window of units that were never placed.
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
            (system, orders) = BuildWorld(grid, scenario, DefaultSquad);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            error = ex.Message;
            return false;
        }

        Grid = grid;
        MapName = mapName;
        Scenario = scenario;
        _system = system;
        _orders = orders;
        Running = scenario is null;
        ResetSelection();
        Version++;
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

        (_system, _orders) = BuildWorld(Grid, Scenario, squad: 0);
        Running = false;
        ResetSelection();
    }

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
    private static (MovementSystem System, Queue<ScenarioOrder> Orders) BuildWorld(
        Grid grid, RecordedScenario? scenario, int squad)
    {
        var system = new MovementSystem(grid);

        if (scenario is not null)
        {
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
