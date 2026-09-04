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
    /// <summary>
    /// The budget a map is fitted into, and the room left under it for the
    /// status line.
    /// </summary>
    /// <remarks>
    /// Reachable from the test project because the chrome tests measure the
    /// status line at the size THIS host gives it. A test that restated 1000
    /// would be measuring a window nobody opens.
    /// </remarks>
    internal const int MaxMapPixels = 1000;

    /// <summary>Room left under the map for the status line, in pixels.</summary>
    internal const int StatusHeight = 26;

    /// <summary>
    /// THE PANEL, DECLARED: sections in the order they are shown, each with the
    /// groups it holds in the order they are shown.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS THE GOD CLASS, IN THE GOOD WAY, AND THAT IS WHY THE TABLE IS
    /// HERE.</b> Laying the movement layer's headings out next to a tactics
    /// source's is a statement about BOTH, and this host is the only place in the
    /// viewer entitled to hold both: Nav.Viewer.Shared references Nav.Core alone
    /// so that it cannot name a Kit or a Squad, and the table used to walk around
    /// that with quoted strings -- the seam breached in the one form the compiler
    /// cannot see. There was nowhere legal to put these names as a type, and that
    /// absence was the proof the table was in the wrong project.
    /// <para>
    /// <b>Every name is a CONSTANT, so a rename fails to compile.</b> Quoted, a
    /// heading that drifted did not break: it stopped matching, dropped to
    /// unknown-order and sank to the bottom of its section, silently, in a window
    /// nobody had open. <see cref="MovementGroups"/> and
    /// <see cref="DemoWorldGroups"/> are the producers' own vocabulary, and this
    /// is the one list that says what order to read them in.
    /// </para>
    /// <para>
    /// No file format and no settings type: it is a static table today, and the
    /// extension point is that a loader would build this object instead. That is
    /// the same shape <c>Keymap</c> uses, and for the same reason.
    /// </para>
    /// <para>
    /// Reachable from the test project so the panel can be asserted against the
    /// arrangement the window actually opens with. A test that restated the list
    /// would be measuring its own composition.
    /// </para>
    /// </remarks>
    internal static readonly InspectorArrangement Arrangement = new(
    [
        (InspectorLayout.MovementSection, new[]
        {
            MovementGroups.Agent,
            MovementGroups.Progress,
            MovementGroups.Plan,
            MovementGroups.Formation,
            MovementGroups.Field,
            MovementGroups.Planning,
        }),
        (InspectorLayout.TacticsSection, new[]
        {
            DemoWorldGroups.Squad,
            DemoWorldGroups.Condition,
            DemoWorldGroups.Kit,
            DemoWorldGroups.Fight,
            DemoWorldGroups.Perception,
            DemoWorldGroups.World,
            DemoWorldGroups.Rates,
            DemoWorldGroups.Rank,
        }),
        (InspectorLayout.ViewerSection, new[]
        {
            InspectorLayout.SourcesGroup,
            InspectorLayout.ControlsGroup,
        }),
    ]);

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
        // The arrangement is handed over as DATA, from the one place that can see
        // both halves of the seam. The app orders blocks by it and never learns
        // what any of the names mean.
        var app = new ViewerApp(
            session, MaxMapPixels, MaxMapPixels - StatusHeight, keys: null, sources, eyes, Arrangement);
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
    /// <para>
    /// Reachable from the test project for one reason: the status line has to be
    /// measured against the world the user is actually looking at, and a test
    /// that built its own guard fight would be measuring its own composition.
    /// </para>
    /// </remarks>
    /// <param name="world">A name from <see cref="ViewerOptions.KnownWorlds"/>.</param>
    internal static (ViewerSession Session, IReadOnlyList<IWorldDebugView> Sources, IVisibilityView Eyes)
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
                    [new LiveWorldSource(() => Panel(live!))],
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

    /// <summary>
    /// The panel's view of the world as it stands: the tactics world, and every
    /// squad on the board as its own doctrine is handed it.
    /// </summary>
    /// <remarks>
    /// <b>Wiring, and it belongs here for the same reason the rest of Compose
    /// does.</b> Which perception a squad reasons through is a fact about the
    /// composition -- the guards look through side 0 and the waves through side
    /// 1, exactly as <see cref="GuardRetreatWorld.Step"/> hands them out -- and
    /// nothing downstream could work it out. Reading the squad rows through the
    /// wrong side's eyes would be a panel quietly telling the reader the guards
    /// can see what the attackers can.
    /// <para>
    /// The views are snapshots, so this is called again for every read rather
    /// than once. <see cref="LiveWorldSource"/> is what makes that true, and it
    /// is also what keeps the waves list current: a wave that arrives at tick
    /// 160 is in here from the next read onwards without anything being told.
    /// </para>
    /// </remarks>
    private static DemoWorldDebugView Panel(GuardRetreatWorld live) =>
        new(
            live.World,
            [
                live.Guard.ViewFor(live.Board, live.World.ViewFor(0)),
                .. live.Waves.Select(wave => wave.ViewFor(live.Board, live.World.ViewFor(1))),
            ]);
}
