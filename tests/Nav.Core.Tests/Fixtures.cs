namespace Nav.Core.Tests;

/// <summary>
/// Paths to the benchmark files copied beside the test binary by the csproj.
/// </summary>
internal static class Fixtures
{
    public static string ArenaMap { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena.map");

    public static string ArenaScenario { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "arena.map.scen");
}
