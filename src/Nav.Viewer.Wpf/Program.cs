using System.IO;

using Nav.Core;
using Nav.Viewer.Tactics;
using Nav.Worlds;

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

        ViewerSession session;
        IReadOnlyList<IWorldDebugView> sources;
        IVisibilityView? eyes;
        if (options.World is { } world)
        {
            (session, sources, eyes) = Compose(world);
        }
        else
        {
            // The session owns loading and every refusal in it; both hosts print
            // the same message for the same problem because there is one loader.
            if (!ViewerSession.TryLoad(options, out var loaded, out var loadError))
            {
                Console.Error.WriteLine(loadError);
                return 1;
            }

            session = loaded;
            sources = [];

            // A recording has no sides with knowledge of their own, so there is
            // nobody's eyes to borrow and the viewpoint key stays quiet.
            eyes = null;
        }

        // The same budget the raylib host uses, so the two windows are the
        // same size for the same map by construction rather than by agreement.
        var app = new ViewerApp(session, MaxMapPixels, MaxMapPixels - StatusHeight, keys: null, sources, eyes);
        using var host = new WpfHost(app.Layout, options.MaxFrames);
        host.Run(app);

        return 0;
    }

    /// <summary>
    /// A live world wired up: the session that plays it, the source that
    /// describes it into the inspector, and the eyes the board can be drawn
    /// through.
    /// </summary>
    /// <remarks>
    /// <b>Both come out of ONE assignment, and that is the point of the shape.</b>
    /// The session takes a factory because a world cannot be rewound -- R builds
    /// another one -- and the panel needs the tactics world INSIDE whichever one
    /// the session is currently stepping. Wired as two independent lines (a world
    /// built here, handed to the session, and wrapped for the panel) the two
    /// would agree until the first restart and silently stop: the map would draw
    /// the new fight while the panel went on reporting the health, the rank and
    /// the sightings of a world nobody was stepping any more.
    /// <para>
    /// So the factory's body is the only place a world is built, it assigns the
    /// handle as it hands one over, and the source reads that handle at describe
    /// time rather than closing over a world. There is no sequence of calls that
    /// leaves the two looking at different worlds, because there is no second
    /// construction site for one of them to be behind.
    /// </para>
    /// <para>
    /// The name has already been checked by <see cref="ViewerOptions.TryParse"/>
    /// against <see cref="ViewerOptions.KnownWorlds"/>, so the default arm here is
    /// the two lists having drifted apart rather than anything a user typed.
    /// </para>
    /// </remarks>
    private static (ViewerSession Session, IReadOnlyList<IWorldDebugView> Sources, IVisibilityView Eyes)
        Compose(string world)
    {
        switch (world)
        {
            case "guard-retreat":
            {
                GuardRetreatWorld? live = null;
                var session = ViewerSession.FromWorld(() => live = new GuardRetreatWorld(), world);

                // Non-null from here on: the session built one on the way in, and
                // every rebuild goes through the same assignment. Both
                // instruments read the same handle, so neither can be left
                // describing or drawing a world nobody is stepping.
                return (
                    session,
                    [new LiveWorldSource(() => new DemoWorldDebugView(live!.World))],
                    new LiveVisibilitySource(() => new DemoWorldVisibility(live!.World)));
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(world),
                    world,
                    $"parsed as a known world, but this host builds none of that name. " +
                    $"Known worlds: {string.Join(", ", ViewerOptions.KnownWorlds)}.");
        }
    }
}
