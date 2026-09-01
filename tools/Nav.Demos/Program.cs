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
    private static readonly Demo[] Demos = [new GuardRetreatDemo()];

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

            Console.WriteLine(DemoTrace.Summarise(demo.Name, run.Ticks, run.Agents, run.World));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {path}"));
        }

        return 0;
    }
}
