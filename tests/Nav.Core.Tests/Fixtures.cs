namespace Nav.Core.Tests;

/// <summary>
/// Paths to the benchmark files copied beside the test binary by the csproj.
/// </summary>
internal static class Fixtures
{
    public static string ArenaMap { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena.map");

    public static string ArenaScenario { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena.map.scen");

    /// <summary>The six committed scenarios, each aimed at a distinct failure mode.</summary>
    public static readonly string[] ScenarioNames =
        ["headon", "chokepoint", "group", "crossing", "standing", "crosscut"];

    public static string Map(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Scenario(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "scenarios", $"{name}.scenario");

    /// <summary>Loads a scenario and the map it names.</summary>
    public static (RecordedScenario Scenario, Grid Grid) Load(string name)
    {
        var scenario = RecordedScenario.FromFile(Scenario(name));
        return (scenario, Grid.FromMapFile(Map(scenario.MapName)));
    }
}
