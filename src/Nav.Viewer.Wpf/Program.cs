using System.IO;

using Nav.Core;
using Nav.Core.Interfaces;
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
    /// Groups this host wants somewhere other than where their view put them:
    /// per section, the headings to show first, in the order to show them.
    /// </summary>
    /// <remarks>
    /// <b>EMPTY, and that is the finding.</b> Every heading on the panel today is
    /// where the view that emits it declared it, so there is nothing here to
    /// disagree with a producer about. An entry is how a disagreement is
    /// expressed -- <c>(InspectorLayout.TacticsSection, [DemoWorldGroups.World])</c>
    /// would put the board's block above the unit's without the view being
    /// touched, and without restating the seven names under it.
    /// <para>
    /// <b>The extension point, unchanged in shape.</b> A loader that reads a file
    /// builds this list instead of the empty one, exactly as <c>Keymap</c> would.
    /// Derived by default is not the same as fixed.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyList<(string Section, IReadOnlyList<string> First)> Preferences = [];

    /// <summary>
    /// THE PANEL, DERIVED: each view asked what it can produce, under the caption
    /// this host files it under, in the order this host reads them in.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS THE GOD CLASS, IN THE GOOD WAY, AND WHAT THAT ENTITLES IT TO IS
    /// THE ORDER.</b> Filing the movement layer's answers under one caption and a
    /// tactics source's under another is a statement about BOTH, and this host is
    /// the only place in the viewer entitled to make it: Nav.Viewer.Shared
    /// references Nav.Core alone so that it cannot name a Kit or a Squad.
    /// <para>
    /// <b>What it is no longer entitled to is the VOCABULARY.</b> It used to
    /// restate every heading as a constant -- better than quoting them, and still
    /// a second copy of a list the producers own, kept honest by a test whose
    /// whole job was to catch the two drifting. A copy that cannot exist cannot
    /// drift, so the names come off <see cref="IDebugView.Groups"/> now and the
    /// test that guarded the copy has become the invariant the asking creates:
    /// no view emits a group it did not declare.
    /// </para>
    /// <para>
    /// <b>The movement layer is asked through a per-agent view like the panel
    /// asks it.</b> A vocabulary is a fact about the view and not about the unit,
    /// so any id answers -- see <see cref="IDebugView.Groups"/> -- and
    /// <see cref="AnyUnit"/> is that "any" written down.
    /// </para>
    /// <para>
    /// Reachable from the test project so the panel can be asserted against the
    /// arrangement the window actually opens with. A test that restated the list
    /// would be measuring its own composition.
    /// </para>
    /// </remarks>
    /// <param name="session">The movement layer, asked for its vocabulary.</param>
    /// <param name="sources">Everything else describing itself into the panel.</param>
    /// <param name="preferences">
    /// What to hoist, or null for <see cref="Preferences"/> -- which is what the
    /// window opens with.
    /// </param>
    internal static InspectorArrangement ArrangementFor(
        ViewerSession session,
        IReadOnlyList<IWorldDebugView> sources,
        IReadOnlyList<(string Section, IReadOnlyList<string> First)>? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sources);

        var declared = new List<(string Section, IReadOnlyList<string> Declared)>
        {
            (InspectorLayout.MovementSection, session.DebugFor(AnyUnit).Groups),
        };

        // Every source under the one caption, in the order they were handed over
        // -- which is the order the panel merges their rows in, so a second
        // source lands after the first here as well.
        foreach (var source in sources)
        {
            declared.Add((InspectorLayout.TacticsSection, source.Groups));
        }

        // The viewer's own words, from the viewer's own project. This host does
        // not own them either.
        declared.Add((InspectorLayout.ViewerSection, InspectorLayout.ViewerGroups));

        return InspectorArrangement.Derived(declared, preferences ?? Preferences);
    }

    /// <summary>
    /// The id the movement layer's vocabulary is asked through. Any would do;
    /// <see cref="IDebugView.Groups"/> is a fact about the view.
    /// </summary>
    private const int AnyUnit = 0;

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
        // The arrangement is handed over as DATA, derived here from what the
        // composed views say they can produce. The app orders blocks by it and
        // never learns what any of the names mean.
        var app = new ViewerApp(
            session,
            MaxMapPixels,
            MaxMapPixels - StatusHeight,
            keys: null,
            sources,
            eyes,
            ArrangementFor(session, sources));
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
