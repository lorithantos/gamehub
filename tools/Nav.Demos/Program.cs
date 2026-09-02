using System.Globalization;

namespace Nav.Demos;

/// <summary>
/// Plays every demo and writes a trace per demo, for watching afterwards.
/// </summary>
/// <remarks>
/// Headless and deterministic, like everything else here: the same run every
/// time, so a trace is worth diffing and an animation of it is worth trusting.
/// </remarks>
internal static class Program
{
    private static readonly Demo[] Demos = [new GuardRetreatDemo(), new PatrolBaitDemo()];

    private static int Main(string[] args)
    {
        var directory = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "demos");
        Directory.CreateDirectory(directory);

        foreach (var demo in Demos)
        {
            var path = Path.Combine(directory, $"{demo.Name}.trace.jsonl");
            Run run;
            using (var writer = new StreamWriter(path))
            {
                run = demo.Play(writer);
            }

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"{demo.Name,-14} {run.Ticks,4} ticks  {run.Headline}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {path}"));

            // The replay is refreshed from the trace that was just written, in
            // the same pass, so the page cannot be left showing an older run.
            // It used to be possible to change a demo and have the page go on
            // playing the previous one with nothing saying so.
            var page = ReplayPageFor(demo.Name);
            if (ReplayPage.Refresh(page, path) is { } shape)
            {
                // The shape is printed because the packer owns the data and not
                // the words. A page whose prose still describes four units is
                // something only a reader catches; putting the run's shape here
                // at least puts it in front of whoever changed the demo.
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {page}"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  replay refreshed: {shape} -- check the page's prose still says this"));
            }
        }

        return 0;
    }

    /// <summary>
    /// The hand-built replay page for a demo, found by walking up from the
    /// binary until a <c>web/</c> holding it turns up.
    /// </summary>
    /// <remarks>
    /// Walked rather than taken from the working directory because the page is
    /// SOURCE and is rewritten in place: running the demos from the repository
    /// root and running them from bin have to reach the same file, or one of
    /// them quietly refreshes nothing. A name with no page returns a path that
    /// does not exist, and <see cref="ReplayPage.Refresh"/> reports false --
    /// a demo is allowed to have no replay.
    /// </remarks>
    private static string ReplayPageFor(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "web", $"{name}.html");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "web", $"{name}.html");
    }
}
