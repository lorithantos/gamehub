using Nav.Core;

namespace Nav.Viewer.Tests;

/// <summary>
/// What colour a unit comes out, and why.
/// </summary>
/// <remarks>
/// <c>ViewerApp.ColourFor</c> is private and static, and it stays that way: it is
/// read through the circle the viewer actually draws, exactly as the route and
/// no-route marks are. A colour is not a fact about a method, it is a fact about
/// the frame -- and a test that called the method directly would go on passing
/// after the day somebody stopped calling it.
/// <para>
/// Its own file rather than another thousand lines of <see cref="ViewerAppTests"/>,
/// for the reason <c>InspectorTests</c> is: colour is one concern and these
/// fixtures are built for it.
/// </para>
/// </remarks>
public sealed class UnitColourTests
{
    private const int StatusHeight = 26;

    private static GridLayout LayoutFor(Grid grid) => GridLayout.Fit(grid, 1000, 1000 - StatusHeight);

    /// <summary>Idle frames, one simulation step each, so a count reads in TICKS.</summary>
    private static ScriptedFrame[] Ticks(int count) =>
        ScriptedHost.Idle(count, (float)WorldScale.Default.SecondsPerTick);

    /// <summary>A one-wide corridor: everybody is in everybody else's way.</summary>
    private const string QueueCorridor =
        """
        type octile
        height 3
        width 12
        map
        @@@@@@@@@@@@
        @..........@
        @@@@@@@@@@@@
        """;

    /// <summary>Open ground, walled at the edge, wide enough to cross for a while.</summary>
    private static Grid OpenField()
    {
        const int Width = 60;
        const int Height = 40;

        var lines = new List<string> { "type octile", $"height {Height}", $"width {Width}", "map" };
        for (var y = 0; y < Height; y++)
        {
            lines.Add(y == 0 || y == Height - 1
                ? new string('@', Width)
                : $"@{new string('.', Width - 2)}@");
        }

        return Grid.FromMapText(string.Join("\n", lines));
    }

    /// <summary>
    /// The unit discs in the last frame drawn, indexed by agent id.
    /// </summary>
    /// <remarks>
    /// Picked as the LARGEST circles on the frame rather than by a radius spelled
    /// out here: every other circle the viewer puts at a unit's position -- the
    /// selection dot, the leader's doubled dot -- is a fraction of the unit's own,
    /// and so is a route's wait mark. Choosing by the maximum keeps the
    /// third-of-a-cell radius a presentation detail, which is how the rest of this
    /// suite treats radii.
    /// <para>
    /// Render walks <c>Session.Agents</c>, which is in id order, so the position
    /// in this list is the agent's id.
    /// </para>
    /// </remarks>
    private static List<RgbaColor> UnitColours(ViewerApp app, RecordingRenderer renderer)
    {
        var circles = renderer.LastFrameOfKind<DrawCommand.Circle>().ToList();
        var widest = circles.Max(c => c.Radius);
        var discs = circles.Where(c => c.Radius == widest).ToList();

        Assert.Equal(app.Agents.Count, discs.Count);
        return [.. discs.Select(c => c.Color)];
    }

    /// <summary>
    /// Eight units in <see cref="QueueCorridor"/>, ordered to the far end ONE AT A
    /// TIME, which is what makes them jam.
    /// </summary>
    /// <remarks>
    /// A single group order would not do it: a group shares a flow field and
    /// spreads its parking slots, so its members queue politely and mostly read as
    /// arrived. Eight separate orders to the same cell in a corridor one wide is
    /// eight units that each want the same ground, and the ones at the back stall,
    /// back off, and probe again -- which is the only way this suite has found to
    /// produce all three of the blocked states the branches below describe.
    /// </remarks>
    private static ViewerApp Jammed()
    {
        var grid = Grid.FromMapText(QueueCorridor);
        var app = new ViewerApp(grid, LayoutFor(grid), squad: 8);

        for (var id = 0; id < app.Agents.Count; id++)
        {
            app.Session.Select([id]);
            app.Session.OrderSelection(grid.Index(10, 1));
        }

        return app;
    }

    [Fact]
    public void TheStuckArrivedAndWaitingColoursAreExactlyWhatTheyWere()
    {
        // THE LITERALS, PINNED. Sides and the dead branch arrived on top of these
        // four lines without touching any of them, so nothing failed and nothing
        // said they still meant what they used to. They are values a human eye
        // was tuned against -- the dim red that says "in the queue" as against the
        // bright one that says "refused" -- and re-deriving them is not something
        // anybody should be able to do by accident.
        //
        // Sampled EVERY tick rather than at the end. A blocked unit's colour is
        // the one that changes fastest of any on the map -- stall, back off,
        // probe, stall -- so the last frame of a run is the worst possible place
        // to look for it.
        var app = Jammed();
        var renderer = new RecordingRenderer();

        var queued = 0;
        var probing = 0;
        var arrived = 0;

        for (var tick = 0; tick < 400; tick++)
        {
            renderer.Clear();
            using (var step = new ScriptedHost(Ticks(1), renderer))
            {
                step.Run(app);
            }

            var colours = UnitColours(app, renderer);
            foreach (var unit in app.Agents)
            {
                // Nobody dies in this fixture, so the branch above these three is
                // never consulted and what is measured here is the old order.
                Assert.True(unit.Alive);

                if (unit.Stuck && unit.Waiting)
                {
                    queued++;
                    Assert.Equal(RgbaColor.Rgb(190, 120, 120), colours[unit.Id]);
                }
                else if (unit.Stuck)
                {
                    probing++;
                    Assert.Equal(RgbaColor.Red, colours[unit.Id]);
                }
                else if (unit.Arrived)
                {
                    arrived++;
                    Assert.Equal(RgbaColor.Rgb(130, 130, 130), colours[unit.Id]);
                }
                else if (unit.Waiting)
                {
                    // NEVER REACHED, and said out loud rather than left as a
                    // silent hole. Waiting-without-stalling means a doctrine held
                    // the unit -- the metered gather's pacing -- and the viewer
                    // orders through the default scrum, which has no pacing to
                    // hold anybody with. Every waiting unit the viewer can produce
                    // has stalled first and is caught by the branch above.
                    Assert.Equal(RgbaColor.Rgb(150, 150, 170), colours[unit.Id]);
                }
            }
        }

        // Each of the three the fixture CAN reach, actually reached -- a colour
        // asserted on a state that never occurred is a green test guarding
        // nothing, which is the failure this suite has already had once.
        Assert.True(queued > 0, "the fixture never jammed anybody into the queue");
        Assert.True(probing > 0, "the fixture never caught a stalled unit probing again");
        Assert.True(arrived > 0, "the fixture never got anybody home");
    }

    [Fact]
    public void ABodyWearsTheDeadGreyRatherThanTheColourOfWhateverItWasDoing()
    {
        // A removed unit keeps its id and its last cell for the life of the
        // system, so it is drawn on every frame from now on and it needs to read
        // as ground rather than as a unit that has stopped taking orders.
        //
        // WHAT THE NEW BRANCH ACTUALLY OVERRIDES IS ARRIVED, and this is the place
        // that fact is recorded. The branch sits above stuck as well, but a
        // dead-and-stuck unit CANNOT BE BUILT: MovementSystem.Remove zeroes
        // StalledTicks and parks the goal on the unit's own cell, and every verb
        // that could re-goal or re-stall it refuses a removed agent. So a corpse
        // is always Arrived and never Stuck, and the two states below are asserted
        // rather than assumed -- if Remove ever stops parking the goal, that is
        // the day the ordering against stuck starts to matter and this test should
        // fail rather than quietly start testing something else.
        var grid = Grid.FromMapText(QueueCorridor);
        var app = new ViewerApp(grid, LayoutFor(grid), squad: 2);
        var renderer = new RecordingRenderer();

        app.Session.Remove(1);

        using var host = new ScriptedHost([new ScriptedFrame(Dt: 0f)], renderer);
        host.Run(app);

        var live = app.Agents[0];
        var body = app.Agents[1];

        Assert.True(live.Alive);
        Assert.False(body.Alive);
        Assert.True(body.Arrived, "Remove stopped parking the goal on the unit's own cell");
        Assert.False(body.Stuck);

        var colours = UnitColours(app, renderer);

        Assert.Equal(RgbaColor.Rgb(55, 55, 60), colours[1]);

        // And not the grey one line down, which is the branch that would claim it
        // if the dead one moved or came out. Named rather than derived: the two
        // greys are close enough on paper that "some grey" would pass either way.
        Assert.NotEqual(RgbaColor.Rgb(130, 130, 130), colours[1]);

        // Its neighbour is standing on its own goal and has not been removed, so
        // it wears the arrived grey -- the pair, in one frame, is what makes this
        // a comparison rather than a single reading.
        Assert.True(live.Arrived);
        Assert.Equal(RgbaColor.Rgb(130, 130, 130), colours[0]);
    }

    [Fact]
    public void EveryMovingUnitWearsItsOwnColourInsideItsSidesArc()
    {
        // Two claims, because either alone is passed by something broken.
        //
        // DISTINCT: a hue per id is the whole reason units are not all one colour,
        // and a spread that collapsed -- the recorded way to get that is stepping
        // the golden ANGLE and then folding it into a narrower arc, where 137.5
        // degrees modulo the arc is a few degrees and consecutive ids come out the
        // same -- still draws eight circles and still looks like a squad.
        //
        // INSIDE THE ARC: distinctness alone is equally true of the old full-wheel
        // spread, which is what this replaced. Side 0's arc runs from red through
        // yellow to green and stops well short of 120 degrees, where blue first
        // appears, so every unit on it has its blue channel at the ramp's floor and
        // one of red or green at the top. A unit spread around the whole wheel
        // lands outside that and fails here.
        //
        // Open ground and separate orders, so these are units walking rather than
        // arrived or jammed: arrived, stuck and waiting are answered by earlier
        // branches and never reach the hue at all.
        var grid = OpenField();
        var app = new ViewerApp(grid, LayoutFor(grid), squad: 8);

        for (var id = 0; id < app.Agents.Count; id++)
        {
            app.Session.Select([id]);
            app.Session.OrderSelection(grid.Index(58, 2 + (id * 4)));
        }

        var renderer = new RecordingRenderer();
        using var host = new ScriptedHost(Ticks(20), renderer);
        host.Run(app);

        var colours = UnitColours(app, renderer);
        var moving = app.Agents
            .Where(a => a.Alive && !a.Arrived && !a.Stuck && !a.Waiting)
            .ToList();

        Assert.Equal(app.Agents.Count, moving.Count);

        var hues = moving.Select(u => colours[u.Id]).ToList();
        Assert.Equal(hues.Count, hues.Distinct().Count());

        // 89 is the ramp's floor -- (0.35 * 255) truncated -- which is what a
        // channel the hue contributes nothing to comes out as.
        Assert.All(hues, c => Assert.Equal((byte)89, c.B));
        Assert.All(hues, c => Assert.True(
            c.R == 255 || c.G == 255, $"{c} is not on the red-to-green arc side 0 owns"));
    }
}
