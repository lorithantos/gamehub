using System.IO;

using Nav.Core;

namespace Nav.Viewer.Wpf;

/// <summary>
/// Entry point for the WPF / Direct3D 11 host.
/// </summary>
/// <remarks>
/// Deliberately a near-copy of the raylib host's Main. The two share everything
/// with a decision in it and duplicate only the wiring that reads it.
/// <para>
/// Hoisting the remaining forty lines into the shared project would be a change
/// to <c>Nav.Viewer.Shared</c>, and the point is to find out whether a second
/// host needs one. Measure first; tidy afterwards.
/// </para>
/// </remarks>
internal static class Program
{
    private const int MaxMapPixels = 1000;
    private const int StatusHeight = 26;

    [STAThread]
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

        // The session owns loading and every refusal in it; both hosts print
        // the same message for the same problem because there is one loader.
        if (!ViewerSession.TryLoad(options, out var session, out var loadError))
        {
            Console.Error.WriteLine(loadError);
            return 1;
        }

        // The same budget the raylib host uses, so the two windows are the
        // same size for the same map by construction rather than by agreement.
        var app = new ViewerApp(session, MaxMapPixels, MaxMapPixels - StatusHeight);
        using var host = new WpfHost(app.Layout, options.MaxFrames);
        host.Run(app);

        return 0;
    }
}
