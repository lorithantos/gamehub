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
        FakeEyes? eyes)
    {
        var session = ViewerSession.FromWorld(() => new StillWorld(grid, units), "fog");
        var app = new ViewerApp(session, 1000, 1000 - StatusHeight, keys: null, sources: null, eyes: eyes);
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
    /// A scripted <see cref="IVisibilityView"/>: whatever the test says each side
    /// can see, remembers and has found.
    /// </summary>
    /// <remarks>
    /// <b>It hands back a FRESH list every call</b>, which is deliberate. The app
    /// is supposed to decide whether the fog it drew is still right by comparing
    /// what it was told, and a fake that returned the same object would let an
    /// app comparing references pass.
    /// </remarks>
    private sealed class FakeEyes(IReadOnlyList<int> sides) : IVisibilityView
    {
        public Dictionary<int, List<int>> Seen { get; } = [];

        public Dictionary<int, List<int>> Pads { get; } = [];

        public Dictionary<int, List<RememberedUnit>> Memory { get; } = [];

        /// <summary>How many times the app asked what a side can see.</summary>
        public int Asked { get; private set; }

        public IReadOnlyList<int> Sides => sides;

        public IReadOnlyList<int> VisibleCells(int side)
        {
            Asked++;
            return Seen.TryGetValue(side, out var cells) ? [.. cells] : [];
        }

        public IReadOnlyList<int> RepairPoints(int side) =>
            Pads.TryGetValue(side, out var cells) ? [.. cells] : [];

        public IReadOnlyList<RememberedUnit> Remembered(int side) =>
            Memory.TryGetValue(side, out var known) ? [.. known] : [];
    }

    /// <summary>
    /// A world whose units stand still: placed once, on the sides the test asked
    /// for, and never ordered anywhere.
    /// </summary>
    /// <remarks>
    /// Standing still is the point. What is being measured is which units reach
    /// the renderer and where their marks land, and a unit that walks moves the
    /// answer to both between two frames of the same test.
    /// </remarks>
    private sealed class StillWorld : IWorld
    {
        public StillWorld(Grid grid, IReadOnlyList<(int Cell, int Side)> units)
        {
            Grid = grid;
            Board = new MovementSystem(grid);
            foreach (var (cell, side) in units)
            {
                Board.AddAgent(cell, side);
            }
        }

        public Grid Grid { get; }

        public MovementSystem Board { get; }

        public void Step(int tick) => Board.Tick();
    }
}
