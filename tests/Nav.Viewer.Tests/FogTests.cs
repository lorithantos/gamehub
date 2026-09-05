using System.Globalization;
using System.Numerics;

using Nav.Core;
using Nav.Core.Interfaces;

namespace Nav.Viewer.Tests;

/// <summary>
/// The board drawn through one side's eyes: what is dimmed, what is not drawn
/// at all, and what is drawn although it is no longer there.
/// </summary>
/// <remarks>
/// <b>The whole file runs on a fake built from Nav.Core alone</b>, and it has to:
/// this project references <c>Nav.Viewer.Shared</c> and nothing else, which is
/// the same absence the viewer is built behind. A test that reached for the real
/// guard fight to find out what side 0 can see would be the seam leaking through
/// the test project. What is measured here is the viewer's half of the contract
/// -- given an <see cref="IVisibilityView"/>, what does it draw -- and the
/// tactics half is measured where the tactics types live.
/// <para>
/// <b>Why any of this exists.</b> The viewer drew the true board, so a doctrine
/// acting on knowledge it should not have looked exactly like one that had
/// earned it. Every assertion below is a way that can now be caught.
/// </para>
/// </remarks>
public sealed class FogTests
{
    private const int StatusHeight = 26;

    /// <summary>A walled room big enough to stand units apart in.</summary>
    private static Grid Room(int width = 14, int height = 9)
    {
        var lines = new List<string> { "type octile", $"height {height}", $"width {width}", "map" };
        for (var y = 0; y < height; y++)
        {
            lines.Add(y == 0 || y == height - 1
                ? new string('@', width)
                : $"@{new string('.', width - 2)}@");
        }

        return Grid.FromMapText(string.Join('\n', lines));
    }

    /// <summary>How long one tick lasts, so a frame count reads in ticks.</summary>
    private static float TickSeconds => (float)WorldScale.Default.SecondsPerTick;

    /// <summary>
    /// A viewer over a world with the given units, and the eyes it draws
    /// through.
    /// </summary>
    private static (ViewerApp App, RecordingRenderer Renderer, Grid Grid) Viewer(
        Grid grid,
        IReadOnlyList<(int Cell, int Side)> units,
        FakeEyes? eyes,
        IReadOnlyList<IWorldDebugView>? sources = null)
    {
        var session = ViewerSession.FromWorld(() => new StillWorld(grid, units), "fog");
        var app = new ViewerApp(session, 1000, 1000 - StatusHeight, keys: null, sources: sources, eyes: eyes);
        return (app, new RecordingRenderer(), grid);
    }

    /// <summary>Presses each key for a frame, releasing between so edges fire.</summary>
    private static void Play(ViewerApp app, RecordingRenderer renderer, params ViewerKeys[] keys)
    {
        var frames = keys
            .SelectMany(k => new[] { new ScriptedFrame(KeysDown: k), new ScriptedFrame() })
            .ToArray();

        using var host = new ScriptedHost(frames, renderer);
        host.Run(app);
    }

    /// <summary>Plays whole ticks, one per frame.</summary>
    private static void Ticks(ViewerApp app, RecordingRenderer renderer, int count)
    {
        using var host = new ScriptedHost(ScriptedHost.Idle(count, TickSeconds), renderer);
        host.Run(app);
    }

    [Fact]
    public void TheFogIsASecondTerrainImageBetweenTheMapAndTheUnits()
    {
        var grid = Room();
        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [grid.Index(2, 2)];

        var (app, renderer, _) = Viewer(grid, [(grid.Index(2, 2), 0)], eyes);
        Play(app, renderer, ViewerKeys.Viewpoint);

        var frame = renderer.LastFrame;
        var terrains = frame.OfType<DrawCommand.Terrain>().ToList();

        // TWO images, not one. The second is the fog, and it is a different
        // object -- both renderers cache their upload by reference identity, so
        // an app that handed the same instance twice would be drawing the map
        // over itself.
        Assert.Equal(2, terrains.Count);
        Assert.NotSame(terrains[0].Image, terrains[1].Image);

        // Over the same rectangle, so the fog lands cell for cell on the map.
        Assert.Equal(terrains[0].Destination, terrains[1].Destination);

        // And UNDER everything. The D3D11 renderer batches lines and circles and
        // flushes them at EndFrame, so a mark drawn before a terrain quad still
        // lands on top of it there -- the two hosts only agree on this ordering,
        // and this is what pins the app to it.
        var fogAt = frame.ToList().FindIndex(c => c is DrawCommand.Terrain t && ReferenceEquals(t.Image, terrains[1].Image));
        var marks = frame.Select((c, i) => (c, i)).Where(p => p.c is DrawCommand.Circle or DrawCommand.Line).ToList();
        Assert.NotEmpty(marks);
        Assert.All(marks, mark => Assert.True(mark.i > fogAt, $"a mark at {mark.i} was drawn before the fog at {fogAt}"));
    }

    [Fact]
    public void AnEnemyTheSideCannotSeeIsNotDrawn()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var theirs = grid.Index(11, 6);

        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine];

        var (app, renderer, _) = Viewer(grid, [(mine, 0), (theirs, 1)], eyes);

        // The observer first, so the comparison is against what is really there.
        Ticks(app, renderer, 1);
        var seenByEverybody = renderer.LastFrameOfKind<DrawCommand.Circle>().Count();
        Assert.Contains(renderer.LastFrameOfKind<DrawCommand.Circle>(), c => c.Center == app.CenterOfCell(theirs));

        Play(app, renderer, ViewerKeys.Viewpoint);

        // Exactly one unit fewer, and it is the one nobody found. Not dimmed and
        // not ghosted: absent.
        var throughSideZero = renderer.LastFrameOfKind<DrawCommand.Circle>().ToList();
        Assert.Equal(0, app.Viewpoint);
        Assert.Equal(seenByEverybody - 1, throughSideZero.Count);
        Assert.DoesNotContain(throughSideZero, c => c.Center == app.CenterOfCell(theirs));
        Assert.Contains(throughSideZero, c => c.Center == app.CenterOfCell(mine));
    }

    [Fact]
    public void AGhostStandsWhereTheSideLastSawTheUnitAndNotWhereItIs()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var theirs = grid.Index(11, 6);
        var remembered = grid.Index(6, 4);

        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine];
        eyes.Memory[0] = [new RememberedUnit(1, remembered, 0)];

        var (app, renderer, _) = Viewer(grid, [(mine, 0), (theirs, 1)], eyes);
        Play(app, renderer, ViewerKeys.Viewpoint);

        var circles = renderer.LastFrameOfKind<DrawCommand.Circle>().ToList();

        // The belief is on screen and the fact is not, which is the entire
        // point: a side shooting at a cell nothing is standing in is a doctrine
        // fault you cannot see on a true board.
        Assert.Contains(circles, c => c.Center == app.CenterOfCell(remembered));
        Assert.DoesNotContain(circles, c => c.Center == app.CenterOfCell(theirs));
    }

    [Fact]
    public void AStalerSightingIsFainterThanAFreshOne()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var fresh = grid.Index(4, 4);
        var stale = grid.Index(6, 4);

        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine];

        var (app, renderer, _) = Viewer(grid, [(mine, 0), (grid.Index(11, 6), 1)], eyes);

        // Two ticks of history, so one sighting is current and one is old.
        Ticks(app, renderer, 40);
        eyes.Memory[0] =
        [
            new RememberedUnit(1, fresh, app.CurrentTick),
            new RememberedUnit(2, stale, app.CurrentTick - 40),
        ];

        Play(app, renderer, ViewerKeys.Viewpoint);

        var circles = renderer.LastFrameOfKind<DrawCommand.Circle>().ToList();
        var atFresh = circles.Single(c => c.Center == app.CenterOfCell(fresh)).Color;
        var atStale = circles.Single(c => c.Center == app.CenterOfCell(stale)).Color;

        // Fading rather than hiding: forty ticks of doubt has to LOOK like forty
        // ticks of doubt, or a watcher reads a memory as a contact.
        Assert.NotEqual(atFresh, atStale);
        Assert.True(
            atStale.R + atStale.G + atStale.B < atFresh.R + atFresh.G + atFresh.B,
            $"the stale ghost {atStale} is not fainter than the fresh one {atFresh}");
    }

    [Fact]
    public void TheFogDimsGroundTheSideCannotSeeAndPaintsThePadsItHasFound()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var seenPad = grid.Index(3, 2);
        var unseen = grid.Index(11, 6);

        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine, seenPad];
        eyes.Pads[0] = [seenPad];

        var (app, renderer, _) = Viewer(grid, [(mine, 0)], eyes);
        Play(app, renderer, ViewerKeys.Viewpoint);

        var fog = renderer.LastFrameOfKind<DrawCommand.Terrain>().Last().Image;

        var lit = Texel(fog, mine);
        var dark = Texel(fog, unseen);
        var pad = Texel(fog, seenPad);

        // Seen open ground is the ordinary map colour, so the picture is still
        // the map.
        Assert.Equal(RgbaColor.RayWhite, lit);

        // What nobody is looking at is darker, and still OPAQUE -- neither
        // renderer promises a blend state, so the fog covers rather than tints.
        Assert.True(dark.R < lit.R && dark.G < lit.G && dark.B < lit.B, $"{dark} is not darker than {lit}");
        Assert.Equal(255, dark.A);

        // A pad the side has found is marked; one it has not is not in the list
        // at all, so nothing on this picture says where to retreat to except
        // ground somebody actually looked at.
        Assert.NotEqual(lit, pad);
        Assert.NotEqual(dark, pad);
    }

    [Fact]
    public void ThePadListAndNotTheVisibleSetIsWhatMarksAPad()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var far = grid.Index(11, 6);

        // The SAME cell, on ground side 0 can see both times, listed as a pad
        // once and not the other. A viewer that marked pads off the ground it
        // can see rather than off what the side found would draw the same
        // picture twice.
        var found = Fog(grid, mine, seen: [mine, far], pads: [far]);
        var notFound = Fog(grid, mine, seen: [mine, far], pads: []);

        Assert.NotEqual(Texel(found, mine), Texel(found, far));
        Assert.Equal(Texel(notFound, mine), Texel(notFound, far));
    }

    /// <summary>The fog image side 0 gets, for one visible set and one pad list.</summary>
    private static TerrainImage Fog(Grid grid, int unit, List<int> seen, List<int> pads)
    {
        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = seen;
        eyes.Pads[0] = pads;

        var (app, renderer, _) = Viewer(grid, [(unit, 0)], eyes);
        Play(app, renderer, ViewerKeys.Viewpoint);

        return renderer.LastFrameOfKind<DrawCommand.Terrain>().Last().Image;
    }

    /// <summary>
    /// The fog-bearing script, played through side 0's eyes: one unit of its
    /// own, one enemy it cannot see, a pad it has found, and two sightings of
    /// different ages.
    /// </summary>
    /// <remarks>
    /// Shared by the two tests below so that "the default draws what it always
    /// drew" and "a different style draws something else" are the SAME frame
    /// asked twice, and any difference between them is the style and nothing
    /// about the script.
    /// </remarks>
    private static IReadOnlyList<DrawCommand> FogFrame(FogStyle? style)
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var pad = grid.Index(3, 2);

        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine, pad];
        eyes.Pads[0] = [pad];

        var session = ViewerSession.FromWorld(() => new StillWorld(grid, [(mine, 0), (grid.Index(11, 6), 1)]), "fog");
        var app = new ViewerApp(session, 1000, 1000 - StatusHeight, eyes: eyes, fog: style);
        var renderer = new RecordingRenderer();

        Ticks(app, renderer, 40);
        eyes.Memory[0] =
        [
            new RememberedUnit(1, grid.Index(6, 4), app.CurrentTick),
            new RememberedUnit(2, grid.Index(8, 3), app.CurrentTick - 40),
        ];

        Play(app, renderer, ViewerKeys.Viewpoint);
        return renderer.LastFrame;
    }

    [Fact]
    public void TheDefaultStyleDrawsTheFrameTheViewerAlwaysDrew()
    {
        // EVERY CALL, SPELLED OUT: the fog image as a census of the colours it
        // was painted with, and each circle with the exact ink that came off the
        // ghost ramp. The five numbers that used to be constants are all in
        // here, so a table consulted with anything but today's row cannot
        // produce this list.
        string[] expected =
        [
            "begin 0,0,0,255",

            // The true map underneath, which no member of the table touches.
            "terrain 0,0,994,639 14x9 245,245,245,255x84 80,80,80,255x42",

            // The fog over it: a wall the side cannot see is 80 dimmed to 22 and
            // open ground it cannot see is 245 dimmed to 68 -- both at 0.28 --
            // beside the one cell it stands on and the one pad it has found.
            "terrain 0,0,994,639 14x9 22,22,22,255x42 245,245,245,255x1 60,150,90,255x1 68,68,68,255x82",

            // The fresh sighting is the near end of the ramp exactly; the
            // forty-tick-old one is a third of the way to the far end, which is
            // where a fade of 120 puts it and nowhere else.
            "circle <461.5, 319.5> 24.14 190,120,200,255",
            "circle <603.5, 248.5> 24.14 146,95,155,255",

            // The side's own unit and its leader mark, unchanged by any of this.
            "circle <177.5, 177.5> 24.14 130,130,130,255",
            "circle <177.5, 177.5> 8.448999 0,0,0,255",
            "end",
        ];

        Assert.Equal(expected, FogFrame(style: null).Select(Depict).ToList());
    }

    [Fact]
    public void ANonDefaultStyleDrawsTheSameBeliefDifferently()
    {
        // Half the dimming, a red pad and a ramp that runs green to blue over
        // ten ticks instead of a hundred and twenty. Nothing about the script
        // moved, so every difference below came out of the table.
        var loud = FogStyle.Default with
        {
            Dim = 0.5f,
            Pad = RgbaColor.Rgb(200, 40, 40),
            GhostFresh = RgbaColor.Rgb(0, 240, 0),
            GhostStale = RgbaColor.Rgb(0, 0, 240),
            GhostFade = 10,
        };

        string[] expected =
        [
            "begin 0,0,0,255",

            // The map underneath is the same map: a style depicts belief and
            // owns nothing else on the frame.
            "terrain 0,0,994,639 14x9 245,245,245,255x84 80,80,80,255x42",

            // Half of 245 and half of 80, where the default row dims them to 68
            // and 22, and a red cell where the pad was green.
            "terrain 0,0,994,639 14x9 122,122,122,255x82 200,40,40,255x1 " +
                "245,245,245,255x1 40,40,40,255x42",

            // Forty ticks against a fade of ten is past the far end, so the
            // older sighting is the stale colour exactly rather than the mix the
            // default row put there.
            "circle <461.5, 319.5> 24.14 0,240,0,255",
            "circle <603.5, 248.5> 24.14 0,0,240,255",

            // Unchanged, because nothing in the table reaches a unit the side
            // can see.
            "circle <177.5, 177.5> 24.14 130,130,130,255",
            "circle <177.5, 177.5> 8.448999 0,0,0,255",
            "end",
        ];

        Assert.Equal(expected, FogFrame(loud).Select(Depict).ToList());
    }

    [Fact]
    public void TheViewpointKeyCyclesEverySideAndComesBackToTheObserver()
    {
        var grid = Room();

        // Three sides, and NOT 0, 1, 2: nothing promises side numbers are dense
        // or that there are two of them, and a viewer that counted instead of
        // walking the list would show a board nobody is fighting for.
        var eyes = new FakeEyes([0, 2, 5]);
        var (app, renderer, _) = Viewer(grid, [(grid.Index(2, 2), 0)], eyes);

        Assert.Equal(-1, app.Viewpoint);

        var walked = new List<int>();
        for (var press = 0; press < 4; press++)
        {
            Play(app, renderer, ViewerKeys.Viewpoint);
            walked.Add(app.Viewpoint);
        }

        Assert.Equal([0, 2, 5, -1], walked);
    }

    [Fact]
    public void AViewerNobodyLentEyesToNeverLeavesTheObserver()
    {
        var grid = Room();
        var (app, renderer, _) = Viewer(grid, [(grid.Index(2, 2), 0)], eyes: null);

        Play(app, renderer, ViewerKeys.Viewpoint, ViewerKeys.Viewpoint, ViewerKeys.Viewpoint);

        Assert.Equal(-1, app.Viewpoint);
        Assert.Single(renderer.LastFrameOfKind<DrawCommand.Terrain>());

        // And the key is not hinted, because hinting a key that does nothing is
        // the lie the status line is generated from a keymap to avoid.
        Assert.DoesNotContain("view", app.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheObserverDrawsExactlyWhatAViewerWithNoFogDraws()
    {
        var grid = Room();
        IReadOnlyList<(int Cell, int Side)> units = [(grid.Index(2, 2), 0), (grid.Index(11, 6), 1)];

        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [grid.Index(2, 2)];
        eyes.Memory[0] = [new RememberedUnit(1, grid.Index(6, 4), 0)];

        var (withFog, fogged, _) = Viewer(grid, units, eyes);
        var (withoutFog, plain, _) = Viewer(grid, units, eyes: null);

        foreach (var (app, renderer) in new[] { (withFog, fogged), (withoutFog, plain) })
        {
            using var host = new ScriptedHost(ScriptedHost.Idle(20, TickSeconds), renderer);
            host.Run(app);
        }

        // Byte for byte the same frames. THE OBSERVER IS THE CONTROL: every
        // draw-count test in this project is written against it, so a fog that
        // changed the observer's picture would have quietly rewritten what all
        // of them measure.
        Assert.Equal(-1, withFog.Viewpoint);
        Assert.Equal(
            plain.Commands.Select(Describe).ToList(),
            fogged.Commands.Select(Describe).ToList());
    }

    [Fact]
    public void TheFogImageIsNotRebuiltWhileTheVisibleSetHoldsStill()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);

        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine, grid.Index(3, 2)];

        var (app, renderer, _) = Viewer(grid, [(mine, 0)], eyes);
        Play(app, renderer, ViewerKeys.Viewpoint);

        renderer.Clear();
        Ticks(app, renderer, 30);

        var images = renderer.OfKind<DrawCommand.Terrain>().Select(t => t.Image).ToList();
        var fogs = images.Where((_, i) => i % 2 == 1).ToList();
        Assert.Equal(30, fogs.Count);

        // ONE object over thirty ticks. Both renderers key their upload cache on
        // reference identity, so a fresh image every frame is a texture upload
        // every frame -- and the answer never changed, so there was nothing to
        // upload. The fake hands back a NEW list each call, which is what makes
        // this a test of comparing the answer rather than of comparing the call.
        Assert.All(fogs, image => Assert.Same(fogs[0], image));
        Assert.True(eyes.Asked >= 30, $"the app asked what side 0 can see only {eyes.Asked} times");

        // And when it does change, the picture does.
        eyes.Seen[0] = [mine];
        renderer.Clear();
        Ticks(app, renderer, 2);

        var after = renderer.OfKind<DrawCommand.Terrain>().Select(t => t.Image).Where((_, i) => i % 2 == 1).ToList();
        Assert.NotSame(fogs[0], after[0]);
        Assert.Same(after[0], after[1]);
    }

    [Fact]
    public void TheStatusLineSaysWhoseEyesTheBoardIsDrawnThroughWithoutChangingLength()
    {
        var grid = Room();
        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [grid.Index(2, 2)];

        var (app, renderer, _) = Viewer(grid, [(grid.Index(2, 2), 0)], eyes);

        Assert.Contains("V view observer", app.StatusText, StringComparison.Ordinal);
        var lengths = new HashSet<int> { app.StatusText.Length };

        for (var press = 0; press < 3; press++)
        {
            Play(app, renderer, ViewerKeys.Viewpoint);
            lengths.Add(app.StatusText.Length);
        }

        Assert.Contains("V view observer", app.StatusText, StringComparison.Ordinal);

        // One length, whatever it says. A status line that breathes shakes a
        // window sized to its content.
        Assert.Single(lengths);
    }

    [Fact]
    public void TheViewpointKeyIsListedAsDoingSomethingOnlyWhereItDoes()
    {
        // The folder lists every bound key, so it cannot deal with an inert one
        // by leaving it out the way the status line does. What it does instead
        // is say which it is -- and the same map, drawn through somebody's eyes,
        // has to stop saying it.
        var grid = Room();
        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [grid.Index(2, 2)];

        var (lent, _, _) = Viewer(grid, [(grid.Index(2, 2), 0)], eyes);
        var (blind, _, _) = Viewer(grid, [(grid.Index(2, 2), 0)], null);

        Assert.Equal("cycle viewpoint", Row(lent, "V"));
        Assert.Equal("cycle viewpoint (nothing to cycle here)", Row(blind, "V"));
    }

    [Fact]
    public void AUnitTheSideCannotSeeIsNotDescribedEither()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var theirs = grid.Index(11, 6);

        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine];

        var source = new Tattler();
        var (app, renderer, _) = Viewer(grid, [(mine, 0), (theirs, 1)], eyes, [source]);
        app.Session.Select([1]);

        // The observer first, so what the panel loses is measured against what
        // there was to lose rather than against an assumption.
        Ticks(app, renderer, 1);
        Assert.Equal("1", Value(app, "Agent", "id"));
        Assert.Equal("unit 1 at 40%", Value(app, "Fight", "health"));

        source.AskedAbout.Clear();
        Play(app, renderer, ViewerKeys.Viewpoint);

        Assert.Equal(0, app.Viewpoint);

        // Nothing about the unit, from any layer that had something to say.
        Assert.DoesNotContain(
            app.Inspector,
            r => string.Equals(r.Section, InspectorLayout.MovementSection, StringComparison.Ordinal));
        Assert.False(Says(app, "Fight", "health"), "the panel read an unseen unit's health");
        Assert.False(Says(app, "Sources", "waits"), "the panel counted an unseen unit's waits");
        Assert.False(Says(app, "Sources", "no route"), "the panel reported an unseen unit's route");

        // NOT ASKED, not filtered. A source is somebody else's code and the only
        // way to be sure it cannot leak is to leave it uncalled.
        Assert.Empty(source.AskedAbout);

        // And it says which it is. An empty panel reads as a broken one.
        Assert.True(Says(app, "Sources", "unit"), "the panel went quiet without saying why");
        Assert.Equal("not visible from here", Value(app, "Sources", "unit"));
        Assert.Equal("side 0 has not found it", Note(app, "Sources", "unit"));

        // The fight's own rows are not about the unit, so fog has no business
        // with them and they are still here.
        Assert.Equal("3 rounds a tick", Value(app, "Fight world", "rates"));
    }

    [Fact]
    public void AUnitTheSideCanSeeIsDescribedExactlyAsItWasBefore()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var theirs = grid.Index(11, 6);
        IReadOnlyList<(int Cell, int Side)> units = [(mine, 0), (theirs, 1)];

        // Side 0 is standing over both cells, so the enemy is FOUND -- and a fix
        // that hid every enemy rather than every unfound one fails here.
        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine, theirs];

        var (fogged, foggedFrames, _) = Viewer(grid, units, eyes, [new Tattler()]);
        var (plain, plainFrames, _) = Viewer(grid, units, eyes: null, sources: [new Tattler()]);

        // THE SAME SCRIPT ON BOTH, which is what makes the two panels comparable:
        // the viewpoint key does nothing at all to a viewer nobody lent eyes to,
        // so one lands on side 0 and the other stays on the truth having spent
        // the identical number of frames getting there.
        foreach (var (app, renderer) in new[] { (fogged, foggedFrames), (plain, plainFrames) })
        {
            app.Session.Select([1]);
            Play(app, renderer, ViewerKeys.Viewpoint);
        }

        Assert.Equal(0, fogged.Viewpoint);
        Assert.Equal(-1, plain.Viewpoint);
        Assert.True(Says(fogged, "Fight", "health"), "side 0 was refused a unit it is standing over");
        Assert.Equal("unit 1 at 40%", Value(fogged, "Fight", "health"));
        Assert.Equal(AboutTheUnit(plain), AboutTheUnit(fogged));
    }

    [Fact]
    public void TheObserverPanelIsWhatAViewerWithNoFogSays()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var theirs = grid.Index(11, 6);
        IReadOnlyList<(int Cell, int Side)> units = [(mine, 0), (theirs, 1)];

        // Eyes that can see almost nothing, and a remembered sighting on top: the
        // most fogged board this fixture can build, watched from the observer.
        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine];
        eyes.Memory[0] = [new RememberedUnit(1, grid.Index(6, 4), 0)];

        var (withFog, fogged, _) = Viewer(grid, units, eyes, [new Tattler()]);
        var (withoutFog, plain, _) = Viewer(grid, units, eyes: null, sources: [new Tattler()]);

        foreach (var (app, renderer) in new[] { (withFog, fogged), (withoutFog, plain) })
        {
            app.Session.Select([1]);
            Ticks(app, renderer, 20);
        }

        // THE OBSERVER IS THE CONTROL, for the panel as much as for the picture.
        // Every other inspector test in this project is written against it, so a
        // fog that reached the observer's rows would have quietly rewritten what
        // all of them measure -- and it is the enemy unit being watched, which is
        // the one a hiding rule with the wrong condition takes away first.
        Assert.Equal(-1, withFog.Viewpoint);
        Assert.False(Says(withFog, "Sources", "unit"), "the observer was told something was hidden");
        Assert.True(Says(withFog, "Fight", "health"), "the observer was refused a unit it can see");
        Assert.Equal("unit 1 at 40%", Value(withFog, "Fight", "health"));
        Assert.Equal(AboutTheUnit(withoutFog), AboutTheUnit(withFog));
    }

    [Fact]
    public void CyclingTheViewpointChangesThePanelWithoutChangingWhatIsWatched()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var theirs = grid.Index(11, 6);

        // Side 0 has found nothing but its own unit; side 1 has found nothing at
        // all -- and still sees the unit it owns, because a side is never fogged
        // out of its own roster.
        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine];

        var (app, renderer, _) = Viewer(grid, [(mine, 0), (theirs, 1)], eyes, [new Tattler()]);
        app.Session.Select([1]);
        Ticks(app, renderer, 1);

        var told = new List<bool>();
        var hidden = new List<bool>();
        for (var press = 0; press < 4; press++)
        {
            told.Add(Says(app, "Fight", "health"));
            hidden.Add(Says(app, "Sources", "unit"));

            // THE SELECTION IS UNTOUCHED. What changes is who is looking, and a
            // fix that dropped the watched unit instead of the answers about it
            // would move the panel onto some other unit entirely.
            Assert.Equal([1], app.Session.Selection);
            Play(app, renderer, ViewerKeys.Viewpoint);
        }

        Assert.Equal([1], app.Session.Selection);

        // Observer, side 0, side 1, observer. Only side 0 is looking at a unit it
        // has not found, and only side 0's panel goes quiet.
        Assert.Equal([true, false, true, true], told);
        Assert.Equal([false, true, false, false], hidden);
    }

    /// <summary>
    /// The panel without the controls folder, which is the part of it that is
    /// about the watched unit.
    /// </summary>
    /// <remarks>
    /// The folder is dropped because the viewpoint key HAS to read differently
    /// between a viewer with eyes and one without -- hinting a key that does
    /// nothing is the lie the folder is generated from the keymap to avoid, and
    /// that difference is pinned by its own test rather than smuggled in here.
    /// </remarks>
    private static List<DebugRow> AboutTheUnit(ViewerApp app) =>
        [.. app.Inspector.Where(r =>
            !string.Equals(r.Group, InspectorLayout.ControlsGroup, StringComparison.Ordinal))];

    /// <summary>The one row under a group and key.</summary>
    private static DebugRow Panel(ViewerApp app, string group, string key) =>
        app.Inspector.Single(r =>
            string.Equals(r.Group, group, StringComparison.Ordinal) &&
            string.Equals(r.Key, key, StringComparison.Ordinal));

    /// <summary>Whether the panel carries a row under a group and key at all.</summary>
    private static bool Says(ViewerApp app, string group, string key) =>
        app.Inspector.Any(r =>
            string.Equals(r.Group, group, StringComparison.Ordinal) &&
            string.Equals(r.Key, key, StringComparison.Ordinal));

    private static string Value(ViewerApp app, string group, string key) => Panel(app, group, key).Value;

    private static string? Note(ViewerApp app, string group, string key) => Panel(app, group, key).Note;

    /// <summary>What the controls folder says one keycap does.</summary>
    private static string Row(ViewerApp app, string keycap) =>
        app.Inspector.Single(r =>
            string.Equals(r.Group, "Controls", StringComparison.Ordinal) &&
            string.Equals(r.Key, keycap, StringComparison.Ordinal)).Value;

    /// <summary>One texel of an image, as the colour it was painted with.</summary>
    private static RgbaColor Texel(TerrainImage image, int cell)
    {
        var pixels = image.Pixels;
        var at = cell * 4;
        return new RgbaColor(pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]);
    }

    /// <summary>
    /// A draw call as comparable text.
    /// </summary>
    /// <remarks>
    /// <see cref="DrawCommand.Terrain"/> holds an image, and two viewers built
    /// over the same map hold equal pictures in different objects -- so record
    /// equality would call two identical frames different. What is compared is
    /// the DECISION: which verb, where, how big, what colour.
    /// </remarks>
    private static string Describe(DrawCommand command) => command switch
    {
        DrawCommand.Terrain t => string.Create(
            CultureInfo.InvariantCulture, $"terrain {t.Destination} {t.Image.Width}x{t.Image.Height}"),
        DrawCommand.Circle c => string.Create(
            CultureInfo.InvariantCulture, $"circle {c.Center} {c.Radius} {c.Color}"),
        DrawCommand.Line l => string.Create(
            CultureInfo.InvariantCulture, $"line {l.From} {l.To} {l.Thickness} {l.Color}"),
        DrawCommand.BeginFrame b => $"begin {b.Clear}",
        _ => "end",
    };

    /// <summary>
    /// A draw call as comparable text, with the colours in it.
    /// </summary>
    /// <remarks>
    /// <see cref="Describe"/> compares the DECISION and deliberately says nothing
    /// about what an image holds, because the two viewers it compares hold equal
    /// pictures in different objects. This one is for the opposite question --
    /// did the picture change -- so it carries the ink: every circle's colour,
    /// and for a terrain image a census of the colours it was painted with,
    /// which pins a dim and a pad without writing a hundred and twenty-six
    /// texels out.
    /// </remarks>
    private static string Depict(DrawCommand command) => command switch
    {
        DrawCommand.Terrain t => string.Create(
            CultureInfo.InvariantCulture,
            $"terrain {t.Destination.X},{t.Destination.Y},{t.Destination.Width},{t.Destination.Height} " +
            $"{t.Image.Width}x{t.Image.Height} {Census(t.Image)}"),
        DrawCommand.Circle c => string.Create(
            CultureInfo.InvariantCulture, $"circle {c.Center} {c.Radius} {Ink(c.Color)}"),
        DrawCommand.Line l => string.Create(
            CultureInfo.InvariantCulture, $"line {l.From} {l.To} {l.Thickness} {Ink(l.Color)}"),
        DrawCommand.BeginFrame b => $"begin {Ink(b.Clear)}",
        _ => "end",
    };

    /// <summary>A colour as four numbers, short enough to read in a failure.</summary>
    private static string Ink(RgbaColor colour) =>
        string.Create(CultureInfo.InvariantCulture, $"{colour.R},{colour.G},{colour.B},{colour.A}");

    /// <summary>Every colour an image was painted with, and how many cells got it.</summary>
    private static string Census(TerrainImage image)
    {
        // Sorted, so the census reads the same way twice. A plain dictionary's
        // order is an implementation detail and this string is an assertion.
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var cell = 0; cell < image.Width * image.Height; cell++)
        {
            var ink = Ink(Texel(image, cell));
            counts[ink] = counts.TryGetValue(ink, out var seen) ? seen + 1 : 1;
        }

        return string.Join(' ', counts.Select(c => string.Create(
            CultureInfo.InvariantCulture, $"{c.Key}x{c.Value}")));
    }

    /// <summary>
    /// A source with one fact about the watched unit and one about the fight,
    /// and a note of who it was asked to describe.
    /// </summary>
    /// <remarks>
    /// Written out of nothing but the interface, exactly as <see cref="FakeEyes"/>
    /// is, and for the same reason: this project references Nav.Viewer.Shared
    /// alone, so a test that reached for a real tactics source to find out what
    /// the panel leaks would be the seam leaking through the test.
    /// <para>
    /// The two halves are the point. A unit's health is a fact fog is about; the
    /// rate the fight is running at is not, and a rule that took both away would
    /// have hidden the map's own legend along with the unit.
    /// </para>
    /// </remarks>
    private sealed class Tattler : IWorldDebugView
    {
        private const string Unit = "Fight";
        private const string World = "Fight world";

        /// <summary>Every unit the panel asked this source to describe.</summary>
        public List<int> AskedAbout { get; } = [];

        public IReadOnlyList<string> Groups => [Unit, World];

        public IReadOnlyList<DebugRow> Describe() => [new DebugRow(World, "rates", "3 rounds a tick")];

        public IDebugView DebugFor(int agent)
        {
            AskedAbout.Add(agent);
            return new UnitRows(agent);
        }

        private sealed class UnitRows(int agent) : IDebugView
        {
            public IReadOnlyList<string> Groups => [Unit];

            public IReadOnlyList<DebugRow> Describe() =>
                [new DebugRow(Unit, "health", $"unit {agent} at 40%")];
        }
    }
}
