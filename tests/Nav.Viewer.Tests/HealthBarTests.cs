using System.Globalization;
using System.Numerics;

using Nav.Core;

namespace Nav.Viewer.Tests;

/// <summary>
/// The bar over a unit's head: that it is there for both sides, that it is
/// never there for a unit the fog is hiding, and that its colour says how hurt
/// the unit is.
/// </summary>
/// <remarks>
/// <b>The whole file runs on fakes built from Nav.Core alone</b>, as
/// <see cref="FogTests"/> does and for the same reason: this project references
/// <c>Nav.Viewer.Shared</c> and nothing else, which is the absence the viewer is
/// built behind. Health reaches the viewer as one fraction over
/// <see cref="IHealthView"/>, so one fraction is what is scripted here -- a test
/// that reached for a real fight to find out what a guard's health is would be
/// the seam leaking through the test.
/// <para>
/// <b>Why any of this exists.</b> The web replay pages drew health bars and the
/// move to this viewer lost them, so a fight could be watched without seeing who
/// was winning it.
/// </para>
/// </remarks>
public sealed class HealthBarTests
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
    /// A viewer over standing units, the eyes it draws through and the health it
    /// draws bars from. Either may be null, which is a viewer that was handed
    /// neither.
    /// </summary>
    private static (ViewerApp App, RecordingRenderer Renderer) Viewer(
        Grid grid,
        IReadOnlyList<(int Cell, int Side)> units,
        FakeEyes? eyes = null,
        FakeHealth? health = null)
    {
        var session = ViewerSession.FromWorld(() => new StillWorld(grid, units), "health");
        var app = new ViewerApp(session, 1000, 1000 - StatusHeight, eyes: eyes, health: health);
        return (app, new RecordingRenderer());
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
    public void AViewerNobodyHandedHealthToDrawsTheFrameItAlwaysDrew()
    {
        var grid = Room();
        var (app, renderer) = Viewer(grid, [(grid.Index(2, 2), 0), (grid.Index(11, 6), 1)]);
        Ticks(app, renderer, 1);

        // EVERY CALL, SPELLED OUT. A bar is two lines and there are no lines in
        // here at all, so anything the bars added -- to either unit, in any
        // colour, at any width -- would appear in this list rather than hide
        // inside a count.
        string[] expected =
        [
            "begin 0,0,0,255",
            "terrain 0,0,994,639 14x9",

            // Two units standing still, and nothing else: a disc each -- both
            // arrived, so both grey -- and the doubled dot the first of them
            // wears for leading its group.
            "circle <177.5, 177.5> 24.14 130,130,130,255",
            "circle <177.5, 177.5> 8.448999 0,0,0,255",
            "circle <816.5, 461.5> 24.14 130,130,130,255",
            "end",
        ];

        Assert.Equal(expected, renderer.LastFrame.Select(Depict).ToList());
    }

    [Fact]
    public void EveryUnitDrawnGetsOneBarAndTheOneTheFogHidesGetsNone()
    {
        var grid = Room();
        var mine = grid.Index(2, 2);
        var theirs = grid.Index(11, 6);

        var eyes = new FakeEyes([0, 1]);
        eyes.Seen[0] = [mine];

        var health = new FakeHealth();
        health.Left[0] = 0.9f;
        health.Left[1] = 0.4f;

        var (app, renderer) = Viewer(grid, [(mine, 0), (theirs, 1)], eyes, health);

        // The observer first: BOTH SIDES WEAR BARS. Nothing about a bar is
        // one side's business, and a viewer that drew them only for side 0
        // would still pass every fog assertion below.
        Ticks(app, renderer, 1);
        Assert.Equal(-1, app.Viewpoint);
        Assert.Equal(
            new[] { app.CenterOfCell(mine).X, app.CenterOfCell(theirs).X },
            Bars(renderer.LastFrame).Select(b => Mid(b.Track).X).Order().ToList());

        // Now through side 0's eyes, which have found nothing but their own
        // unit. The enemy's disc goes, and its bar goes WITH it -- from the same
        // decision, because the bar is drawn inside the loop the hidden unit
        // never reaches.
        Play(app, renderer, ViewerKeys.Viewpoint);
        var seen = renderer.LastFrame;

        Assert.Equal(0, app.Viewpoint);
        Assert.DoesNotContain(
            seen.OfType<DrawCommand.Circle>(), c => c.Center == app.CenterOfCell(theirs));

        var drawn = Bars(seen);
        Assert.Single(drawn);
        Assert.Equal(app.CenterOfCell(mine).X, Mid(drawn[0].Track).X);

        // And no line of any kind is left anywhere near where the hidden unit
        // stands: not a track, not a fill, not a stub.
        Assert.DoesNotContain(
            seen.OfType<DrawCommand.Line>(),
            l => Math.Abs(Mid(l).X - app.CenterOfCell(theirs).X) < app.Layout.CellSize);
    }

    [Theory]

    // Above the upper threshold: green.
    [InlineData(1.0f, 70, 200, 80)]
    [InlineData(0.61f, 70, 200, 80)]

    // ON the upper threshold: yellow. THE BOUNDARY BELONGS TO THE MIDDLE BAND,
    // so a unit sitting exactly on 0.6 is hurt rather than healthy -- the moment
    // something has happened to a unit is the moment worth showing.
    [InlineData(0.6f, 225, 200, 60)]
    [InlineData(0.31f, 225, 200, 60)]

    // ON the lower threshold: yellow as well, by the same rule. Both boundaries
    // are the middle band's, so neither of them is a colour that depends on
    // which way a comparison was written.
    [InlineData(0.3f, 225, 200, 60)]

    // Below it: red.
    [InlineData(0.29f, 220, 60, 55)]
    [InlineData(0.01f, 220, 60, 55)]
    public void TheColourFollowsTheFractionAndBothThresholdsBelongToTheMiddleBand(
        float left, byte r, byte g, byte b)
    {
        Assert.Equal(RgbaColor.Rgb(r, g, b), OneUnitAt(left).Fill!.Color);
    }

    [Fact]
    public void TheTrackIsTheWholeBarAndTheFillIsOnlyWhatIsLeft()
    {
        var grid = Room();
        var (app, renderer) = Viewer(grid, [(grid.Index(2, 2), 0)], health: Hurt(0.5f));
        Ticks(app, renderer, 1);

        // TWO LINES FOR ONE UNIT, counted before anything is read out of them,
        // so a bar drawn as a single line fails here saying how many there were
        // rather than further down saying it could not find one.
        var lines = renderer.LastFrameOfKind<DrawCommand.Line>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(HealthStyle.Default.Track, lines[0].Color);

        var half = OneUnitAt(0.5f);

        // AND THE TRACK IS THE LONGER ONE. What is MISSING is the question a
        // watcher is asking, and it can only be read against a bar of known
        // length -- a fill on its own is a short line, which is what a healthy
        // unit drawn smaller also looks like.
        var track = Length(half.Track);
        Assert.True(track > 0.0f);
        Assert.Equal(track / 2.0f, Length(half.Fill!), 3);

        // Both start at the same end and sit at the same height, so the fill
        // empties from the right rather than shrinking toward its middle.
        Assert.Equal(half.Track.From, half.Fill!.From);
        Assert.Equal(half.Track.From.Y, half.Fill!.To.Y);
        Assert.Equal(half.Track.Thickness, half.Fill!.Thickness);

        // Above the unit, not on it: the disc's radius is 0.34 of a cell and the
        // no-route cross reaches past that, so a bar drawn at the centre would
        // be drawn over both. The unit's own disc is the first circle of the
        // frame; the smaller ones after it are the leader's mark.
        var unit = renderer.LastFrameOfKind<DrawCommand.Circle>().First();
        Assert.True(
            half.Track.From.Y < unit.Center.Y - unit.Radius,
            $"the bar at {half.Track.From.Y} is not clear of a unit at {unit.Center.Y} r{unit.Radius}");

        // A unit with nothing left keeps its track and loses its fill, so a
        // destroyed unit reads as an empty bar rather than as a bar somebody
        // forgot to draw.
        var gone = OneUnitAt(0.0f);
        Assert.Equal(track, Length(gone.Track), 3);
        Assert.Null(gone.Fill);

        // And a view that answered outside 0..1 cannot draw past its own track:
        // the fill is a length, and a length nobody clamped is a picture that
        // means nothing.
        Assert.Equal(track, Length(OneUnitAt(1.4f).Fill!), 3);
    }

    /// <summary>One unit at a given health, and the bar the viewer drew for it.</summary>
    private static (DrawCommand.Line Track, DrawCommand.Line? Fill) OneUnitAt(float left)
    {
        var grid = Room();
        var (app, renderer) = Viewer(grid, [(grid.Index(2, 2), 0)], health: Hurt(left));
        Ticks(app, renderer, 1);

        return Assert.Single(Bars(renderer.LastFrame));
    }

    /// <summary>A health view with the one unit these scripts place in it.</summary>
    private static FakeHealth Hurt(float left)
    {
        var health = new FakeHealth();
        health.Left[0] = left;
        return health;
    }

    /// <summary>
    /// The bars in a frame: the track, and the fill over it where there is one.
    /// </summary>
    /// <remarks>
    /// Read off the recorded commands rather than off a flag, and read by
    /// COLOUR rather than by position in the list: the track is the one thing
    /// painted with <see cref="HealthStyle.Track"/>, and every fill is one of
    /// the three health colours. Nothing else in these scripts draws a line --
    /// there is no route, no drag band and no unit without a plan -- so a bar
    /// that appeared for a unit that should not have one has nowhere to hide.
    /// </remarks>
    private static List<(DrawCommand.Line Track, DrawCommand.Line? Fill)> Bars(
        IReadOnlyList<DrawCommand> frame)
    {
        var lines = frame.OfType<DrawCommand.Line>().ToList();
        var bars = new List<(DrawCommand.Line, DrawCommand.Line?)>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Color != HealthStyle.Default.Track)
            {
                continue;
            }

            var fill = i + 1 < lines.Count && lines[i + 1].Color != HealthStyle.Default.Track
                ? lines[i + 1]
                : null;

            bars.Add((lines[i], fill));
        }

        return bars;
    }

    private static Vector2 Mid(DrawCommand.Line line) => (line.From + line.To) / 2.0f;

    private static float Length(DrawCommand.Line line) => (line.To - line.From).Length();

    /// <summary>A draw call as comparable text, with the colours in it.</summary>
    private static string Depict(DrawCommand command) => command switch
    {
        DrawCommand.Terrain t => string.Create(
            CultureInfo.InvariantCulture,
            $"terrain {t.Destination.X},{t.Destination.Y},{t.Destination.Width},{t.Destination.Height} " +
            $"{t.Image.Width}x{t.Image.Height}"),
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

    /// <summary>
    /// A scripted <see cref="IHealthView"/>: whatever the test says is left of
    /// each unit, and 1 for anybody it was told nothing about.
    /// </summary>
    /// <remarks>
    /// The unknown answer is the interface's, not an invention here -- see
    /// <see cref="IHealthView.HealthOf"/>. A fake that answered 0 for a stranger
    /// would put an empty bar over every unit a test forgot to script, which
    /// reads as a finding rather than as a gap.
    /// </remarks>
    private sealed class FakeHealth : IHealthView
    {
        public Dictionary<int, float> Left { get; } = [];

        /// <summary>How many times the app asked after anybody's health.</summary>
        public int Asked { get; private set; }

        public float HealthOf(int agent)
        {
            Asked++;
            return Left.TryGetValue(agent, out var left) ? left : 1.0f;
        }
    }
}
