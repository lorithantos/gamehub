using Nav.Core;

namespace Nav.Viewer.Raylib;

/// <summary>
/// Entry point for the raylib host.
/// </summary>
/// <remarks>
/// An explicit class with an explicit <c>Main</c>, not top-level statements. Two
/// reasons, and the second is the better one:
/// <list type="number">
/// <item><description>The WPF host needs <c>[STAThread]</c>, which top-level
/// statements cannot carry.</description></item>
/// <item><description>Code in top-level statements is invisible to the Roslyn
/// code graph, so every call the viewer made showed up nowhere.</description></item>
/// </list>
/// <para>
/// What is left here is only wiring: parse, load, size, construct, run.
/// </para>
/// </remarks>
internal static class Program
{
    private const int MaxMapPixels = 1000;
    private const int StatusHeight = 26;

    private static int Main(string[] args)
    {
        if (!ViewerOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(ViewerOptions.UsageText);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(ViewerOptions.UsageText);
            return 0;
        }

        // This host builds no worlds -- it is deliberately behind the WPF one --
        // and the option is shared, so the line parses here and would otherwise
        // open the default fixture map instead. Silently. Refusing at the same
        // exit code as any other usage error, and naming the host that does run
        // it, is the difference between being told no and being handed an
        // unrelated map with no explanation.
        //
        // The usage text is NOT printed beneath this one. It advertises --world
        // as something that works, which is true of the pair of hosts and false
        // of this one; the message already carries the command that works.
        if (options.World is { } world)
        {
            Console.Error.WriteLine(ViewerOptions.WorldNeedsTheWpfHost(world));
            return 2;
        }

        // The session owns loading and every refusal in it: unreadable files,
        // malformed maps and scenarios, all-wall maps, agents placed on walls.
        // The message names the problem; printing it beats a stack trace, and
        // beats opening a window onto nothing.
        if (!ViewerSession.TryLoad(options, out var session, out var loadError))
        {
            Console.Error.WriteLine(loadError);
            return 1;
        }

        // The app derives its own layout from this budget, now and on every
        // mid-session load; both hosts use the same budget, so the two windows
        // cannot disagree about geometry by accident.
        var app = new ViewerApp(session, MaxMapPixels, MaxMapPixels - StatusHeight);
        using var host = new RaylibHost(app.Layout, StatusHeight, options.MaxFrames);
        host.Run(app);

        return 0;
    }
}
