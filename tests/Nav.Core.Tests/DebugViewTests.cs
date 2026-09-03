using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Nav.Core.Tests;

/// <summary>
/// The debug surface: what a human gets to see of state the tick keeps for its
/// own reasons and never published.
/// </summary>
/// <remarks>
/// Every fact asserted here is arranged from OUTSIDE the view -- a unit is
/// parked, a follower is walled in, a gate is set by a doctrine -- so a row that
/// reports a constant instead of reading the agent fails. The rows are prose and
/// nothing may parse them back into values; a test is the one place that rule
/// does not apply, because pinning the wording IS the point.
/// </remarks>
public sealed class DebugViewTests
{
    private const string Room =
        """
        type octile
        height 9
        width 9
        map
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        """;

    /// <summary>A single passable lane, so a unit standing in it is a wall.</summary>
    private const string Corridor =
        """
        type octile
        height 3
        width 9
        map
        @@@@@@@@@
        @.......@
        @@@@@@@@@
        """;

    /// <summary>
    /// Twelve open rows, wide enough to seat more live destinations than the
    /// field cache holds. That is the only condition under which a look at the
    /// cache can cost anything, and it is why this map is not the 9x9 room.
    /// </summary>
    private const string Yard =
        """
        type octile
        height 12
        width 12
        map
        ............
        ............
        ............
        ............
        ............
        ............
        ............
        ............
        ............
        ............
        ............
        ............
        """;

    /// <summary>Two more destinations than the cache can hold, so it is always evicting.</summary>
    private const int LiveDestinations = MovementSystem.FieldCapacity + 2;

    private static (MovementSystem System, Grid Grid) Scene(string map, params (int X, int Y)[] at)
    {
        var grid = Grid.FromMapText(map);
        var system = new MovementSystem(grid);
        foreach (var (x, y) in at)
        {
            system.AddAgent(grid.Index(x, y));
        }

        return (system, grid);
    }

    private static string Value(IDebugView view, string group, string key)
    {
        var rows = view.Describe().Where(r => r.Group == group && r.Key == key).ToArray();
        Assert.True(rows.Length == 1, $"expected one '{group}/{key}' row, got {rows.Length}");
        return rows[0].Value;
    }

    private static bool Has(IDebugView view, string group) =>
        view.Describe().Any(r => r.Group == group);

    private static bool HasKey(IDebugView view, string group, string key) =>
        view.Describe().Any(r => r.Group == group && r.Key == key);

    /// <summary>
    /// Ten single-unit orders on the yard, one agent per destination, agent id
    /// equal to its destination's index. Every tick's leader sweep asks for all
    /// ten fields against a cache of eight, so eviction is continuous and the
    /// order it happens in is the thing under test.
    /// </summary>
    private static (MovementSystem System, CountingFieldSource Source, int[] Destinations) Crowd()
    {
        var grid = Grid.FromMapText(Yard);
        var source = new CountingFieldSource(new FieldCache(grid, MovementSystem.FieldCapacity));
        var system = new MovementSystem(grid, fields: source);

        var destinations = new int[LiveDestinations];
        for (var i = 0; i < LiveDestinations; i++)
        {
            var agent = system.AddAgent(grid.Index(i, 0));
            destinations[i] = grid.Index(i, 11);
            system.Order([agent], destinations[i]);
        }

        return (system, source, destinations);
    }

    /// <summary>The count a row leads with, so an arranged number can be checked.</summary>
    private static int LeadingNumber(string value)
    {
        var digits = value.TakeWhile(char.IsAsciiDigit).ToArray();
        Assert.NotEmpty(digits);
        return int.Parse(new string(digits), CultureInfo.InvariantCulture);
    }

    /// <summary>Gathers normally, and from <see cref="FromTick"/> parks one member.</summary>
    private sealed class ParkOne(int member) : GatherDoctrine
    {
        public int FromTick { get; set; } = int.MaxValue;

        public override void Advance(IGroupOps ops)
        {
            base.Advance(ops);
            if (ops.CurrentTick >= FromTick && ops.Members.Contains(member))
            {
                ops.Park(member);
            }
        }
    }

    /// <summary>
    /// A source that answers as the one it wraps until it is sealed, after which
    /// building anything THROWS.
    /// </summary>
    /// <remarks>
    /// The panel reads through a type with no <c>For</c> on it, and no test can
    /// assert the absence of a member that would not compile. What a test can do
    /// is stand a source under the system that turns the call into an exception
    /// rather than into a number that quietly moved, and then open the panel.
    /// </remarks>
    private sealed class SealsAfterTheTick(IDistanceFieldSource inner) : IDistanceFieldSource, IDistanceFieldView
    {
        /// <summary>Set between ticks, so the run itself is never obstructed.</summary>
        public bool Sealed { get; set; }

        public int Count => inner.Count;

        public IDistanceFieldView View => this;

        public DistanceField For(int destination) => Sealed
            ? throw new InvalidOperationException(
                $"a field for #{destination} was asked for while nothing but the panel was running")
            : inner.For(destination);

        public bool TryPeek(int destination, [NotNullWhen(true)] out DistanceField? field) =>
            inner.View.TryPeek(destination, out field);
    }

    /// <summary>
    /// Holds one member behind its retry gate every pass and does nothing else,
    /// so the gate is the only thing under test.
    /// </summary>
    private sealed class HoldOne(int member, int ticks) : GroupDoctrine
    {
        public override void Advance(IGroupOps ops)
        {
            ArgumentNullException.ThrowIfNull(ops);
            ops.Hold(member, ticks);
        }
    }

    [Fact]
    public void AMemberWithoutASlotSaysSoAndAParkedOneNamesTheCellItHolds()
    {
        // A group member starts WITHOUT a parking slot -- the one fact that
        // explains a unit still walking while its formation looks settled --
        // and nothing outside this view could say so.
        var (system, grid) = Scene(Room, (0, 4), (0, 5));
        var doctrine = new ParkOne(member: 0);
        system.Order([0, 1], grid.Index(8, 4), doctrine);

        Assert.StartsWith("none", Value(system.DebugFor(0), "Progress", "slot"), StringComparison.Ordinal);
        Assert.StartsWith("none", Value(system.DebugFor(1), "Progress", "slot"), StringComparison.Ordinal);

        // Walk until the first plan has moved it, then park it where it stands.
        var start = grid.Index(0, 4);
        for (var tick = 0; tick < 20 && system.Agents[0].Cell == start; tick++)
        {
            system.Tick();
        }

        Assert.NotEqual(start, system.Agents[0].Cell);
        var standingOn = system.Agents[0].Cell;
        doctrine.FromTick = system.CurrentTick;
        system.Tick();

        var slot = Value(system.DebugFor(0), "Progress", "slot");
        Assert.StartsWith("held:", slot, StringComparison.Ordinal);
        Assert.Contains($"#{standingOn}", slot, StringComparison.Ordinal);

        // The fellow is still queueing, so the row is per agent and not per system.
        Assert.StartsWith("none", Value(system.DebugFor(1), "Progress", "slot"), StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockedFollowerCountsEveryTickItStoodStill()
    {
        // Agent 0 was never ordered, so it is parked in the lane for good and
        // there is no way past it. The two ordered behind it descend the field
        // until they are nose to tail against it, and then just stand.
        var (system, grid) = Scene(Corridor, (5, 1), (1, 1), (2, 1));
        system.Order([1, 2], grid.Index(7, 1));

        for (var tick = 0; tick < 8; tick++)
        {
            system.Tick();
        }

        var blocked = Value(system.DebugFor(2), "Progress", "blocked");
        var first = LeadingNumber(blocked);
        Assert.True(first > 0, $"the follower reported '{blocked}' after eight ticks against a wall");

        // The threshold is what makes the count mean anything, so it is in the row.
        Assert.Contains("of 12", blocked, StringComparison.Ordinal);

        // It is a COUNTER, not a flag: five more ticks of standing is five more.
        for (var tick = 0; tick < 5; tick++)
        {
            system.Tick();
        }

        Assert.Equal(first + 5, LeadingNumber(Value(system.DebugFor(2), "Progress", "blocked")));

        // And the unit that never left its cell in the corridor is a follower of
        // nothing: it has no formation at all, which the view says outright.
        Assert.StartsWith("none", Value(system.DebugFor(0), "Formation", "formation"), StringComparison.Ordinal);
        Assert.False(Has(system.DebugFor(0), "Field"), "no formation means no group field to measure against");
    }

    [Fact]
    public void AGatedUnitReportsTheTicksLeftRatherThanTheTickItLifts()
    {
        // Twenty ticks of hold set during the pass, read after the clock has
        // advanced once: nineteen left. A raw RetryAfterTick would read twenty
        // and mean nothing without the clock beside it.
        var (system, grid) = Scene(Room, (0, 4), (0, 5));
        system.Order([0, 1], grid.Index(8, 4), new HoldOne(member: 0, ticks: 20));

        Assert.StartsWith("open", Value(system.DebugFor(0), "Progress", "retry gate"), StringComparison.Ordinal);

        system.Tick();

        var gate = Value(system.DebugFor(0), "Progress", "retry gate");
        Assert.Equal(19, LeadingNumber(gate));
        Assert.Contains("backstop 64", gate, StringComparison.Ordinal);

        // The doctrine re-holds every pass, so it stays nineteen out rather than
        // counting down -- which is the gate moving, and exactly what the row
        // should show.
        system.Tick();
        Assert.Equal(19, LeadingNumber(Value(system.DebugFor(0), "Progress", "retry gate")));

        // Its fellow was never held.
        Assert.StartsWith("open", Value(system.DebugFor(1), "Progress", "retry gate"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdThisSystemNeverIssuedSaysSoInsteadOfThrowing()
    {
        var (system, _) = Scene(Room, (0, 0), (1, 0));

        foreach (var unknown in new[] { 2, 99, -1 })
        {
            var rows = system.DebugFor(unknown).Describe();
            var only = Assert.Single(rows);
            Assert.Equal("id", only.Key);
            Assert.Contains("no such agent", only.Value, StringComparison.Ordinal);
            Assert.Contains("this system has 2", only.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ARemovedAgentSaysItIsGoneRatherThanReportingAnEmptyUnit()
    {
        var (system, grid) = Scene(Room, (0, 4), (0, 5));
        system.Order([0, 1], grid.Index(8, 4));
        system.Tick();

        var where = system.Agents[0].Cell;
        system.Remove(0);

        var view = system.DebugFor(0);
        Assert.StartsWith("no", Value(view, "Unit", "alive"), StringComparison.Ordinal);
        Assert.Contains("removed from the world", Value(view, "Unit", "alive"), StringComparison.Ordinal);
        Assert.Contains($"#{where}", Value(view, "Unit", "cell"), StringComparison.Ordinal);

        // A corpse holds no plan, no slot and no formation, so it gets no rows
        // claiming otherwise -- zeroes under those headings would read as facts
        // about a unit rather than as the absence of one.
        foreach (var group in new[] { "Progress", "Plan", "Formation", "Field", "Planning" })
        {
            Assert.False(Has(view, group), $"a removed agent reported a '{group}' heading");
        }

        // The living one it was ordered with still describes in full.
        Assert.True(Has(system.DebugFor(1), "Plan"));
    }

    [Fact]
    public void OpeningThePanelDoesNotChangeWhichFieldsSurviveOrHowManyWereBuilt()
    {
        // THE INSTRUMENT MUST NOT MOVE THE NEEDLE. Reading the Field rows through
        // the source's ordinary For marks that field most recently used; with more
        // live destinations than the cache holds, that rescues it from an eviction
        // the run had already decided on, some colder field is dropped instead,
        // and the rebuild shows up in the field-build count. Nobody moves
        // differently -- a rebuilt field has identical contents -- so the only
        // casualty is the measurement, which is what this project decides on.
        static (int Builds, int Live) Run(bool openThePanel)
        {
            var (system, source, _) = Crowd();

            // A MIDDLE group, so its field is never the one the leader sweep
            // touched last. Reading the newest field would disturb nothing and
            // the test would pass under any implementation.
            const int Watched = 3;

            for (var tick = 0; tick < 30; tick++)
            {
                system.Tick();
                if (openThePanel)
                {
                    system.DebugFor(Watched).Describe();
                }
            }

            // WHAT SURVIVED IS NOT THE WITNESS HERE, and it was tried: with ten
            // destinations swept every tick against a cache of eight, the last
            // eight asks of the tick decide the survivors whatever went before,
            // so the end-of-tick set is identical under both implementations and
            // asserting on it is green while proving nothing. The rebuild count
            // is what the disturbance actually shows up in; the eviction ORDER is
            // pinned by the sibling test.
            return (source.Builds, system.LiveFields);
        }

        var quiet = Run(openThePanel: false);
        var watched = Run(openThePanel: true);

        Assert.Equal(quiet.Live, watched.Live);
        Assert.Equal(quiet.Builds, watched.Builds);

        // AND THE ARRANGEMENT IS REAL. Two equal numbers off a cache that never
        // evicted would prove nothing at all: the cache must be full, and it must
        // have rebuilt far more than the ten fields a single pass needs.
        Assert.Equal(MovementSystem.FieldCapacity, quiet.Live);
        Assert.True(
            quiet.Builds > LiveDestinations * 2,
            $"only {quiet.Builds} builds over 30 ticks -- the cache is not thrashing, so nothing was at stake");
    }

    [Fact]
    public void OpeningThePanelDoesNotChangeWhichFieldIsEvictedNext()
    {
        // THE EVICTION ORDER ITSELF, arranged so that exactly one field is dropped
        // and it is decidable which. Eight destinations fill the cache exactly, so
        // one tick's sweep leaves it full and evicting nothing; the coldest field
        // is then the first the sweep touched, which is agent 0's. A ninth order
        // forces one eviction, and which destination went is the LRU order made
        // visible from outside.
        static int[] Run(bool openThePanel)
        {
            var grid = Grid.FromMapText(Yard);
            var source = new CountingFieldSource(new FieldCache(grid, MovementSystem.FieldCapacity));
            var system = new MovementSystem(grid, fields: source);

            var destinations = new int[MovementSystem.FieldCapacity];
            for (var i = 0; i < destinations.Length; i++)
            {
                var agent = system.AddAgent(grid.Index(i, 0));
                destinations[i] = grid.Index(i, 11);
                system.Order([agent], destinations[i]);
            }

            system.Tick();

            // The COLDEST field, which is the one a look would rescue.
            if (openThePanel)
            {
                system.DebugFor(0).Describe();
            }

            var latecomer = system.AddAgent(grid.Index(9, 0));
            system.Order([latecomer], grid.Index(9, 11));

            return [.. destinations.Where(d => source.TryPeek(d, out _))];
        }

        var quiet = Run(openThePanel: false);
        var watched = Run(openThePanel: true);

        Assert.Equal(quiet, watched);

        // The arrangement, asserted rather than assumed: exactly one of the eight
        // is gone, and it is the one the run's own asks left coldest. A run that
        // evicted nothing, or evicted several, would make the equality above hold
        // for reasons that have nothing to do with the panel.
        Assert.Equal(MovementSystem.FieldCapacity - 1, quiet.Length);
        Assert.DoesNotContain(Grid.FromMapText(Yard).Index(0, 11), quiet);
    }

    [Fact]
    public void ThePanelReadsThroughAReferenceThatCannotBuildAField()
    {
        // THE NARROWING ITSELF. The two tests above catch a panel that reaches the
        // mutating member by the counts it moves, which is the damage; this one
        // catches the reach. A source that refuses to build turns the mistake into
        // an exception at the moment it is made, so it holds for a field that is
        // MISSING too -- the case where building one would look like helpfulness
        // and where a count-based test has the least to say.
        var grid = Grid.FromMapText(Yard);
        var source = new SealsAfterTheTick(new FieldCache(grid, MovementSystem.FieldCapacity));
        var system = new MovementSystem(grid, fields: source);

        var destinations = new int[LiveDestinations];
        for (var i = 0; i < LiveDestinations; i++)
        {
            var agent = system.AddAgent(grid.Index(i, 0));
            destinations[i] = grid.Index(i, 11);
            system.Order([agent], destinations[i]);
        }

        system.Tick();

        // BOTH BRANCHES OF THE FIELD ROW, arranged before the source is sealed:
        // more live destinations than the cache holds means one of each exists.
        var held = Enumerable.Range(0, LiveDestinations).First(id => source.TryPeek(destinations[id], out _));
        var dropped = Enumerable.Range(0, LiveDestinations).First(id => !source.TryPeek(destinations[id], out _));

        source.Sealed = true;

        Assert.Contains(
            "to the destination",
            Value(system.DebugFor(held), "Field", "from here"),
            StringComparison.Ordinal);
        Assert.StartsWith(
            "not cached",
            Value(system.DebugFor(dropped), "Field", "field"),
            StringComparison.Ordinal);

        // And the seal is real: the run's own asks go through the member the panel
        // has no way to name, so a tick fails where the panel did not.
        Assert.Throws<InvalidOperationException>(system.Tick);
    }

    [Fact]
    public void AFieldTheCacheNoLongerHoldsIsReportedMissingRatherThanAsADistance()
    {
        // A missing measurement must not look like a measurement of zero, and
        // "0 to the destination" is the reading for a unit standing on its goal.
        var (system, source, destinations) = Crowd();
        system.Tick();

        var dropped = Enumerable
            .Range(0, LiveDestinations)
            .First(id => !source.TryPeek(destinations[id], out _));

        var view = system.DebugFor(dropped);
        var value = Value(view, "Field", "field");
        Assert.StartsWith("not cached", value, StringComparison.Ordinal);
        Assert.False(value.Any(char.IsAsciiDigit), $"the missing-field row reported a number: '{value}'");

        // No distance rows at all, rather than distance rows saying nothing.
        Assert.False(HasKey(view, "Field", "from here"), $"a missing field still reported a distance: '{value}'");
        Assert.False(HasKey(view, "Field", "from goal"), $"a missing field still reported a distance: '{value}'");

        // And describing it did not quietly build the field it said was missing.
        Assert.False(source.TryPeek(destinations[dropped], out _), "the panel built the field it had just called missing");

        // A unit whose field IS held still gets the numbers; the row above is the
        // absence of an answer, not the retirement of one.
        var held = Enumerable
            .Range(0, LiveDestinations)
            .First(id => source.TryPeek(destinations[id], out _));
        Assert.Contains(
            "to the destination",
            Value(system.DebugFor(held), "Field", "from here"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheSystemViewReportsTheTickAndKeepsItsRowsInGroupOrder()
    {
        var (system, grid) = Scene(Room, (0, 4), (0, 5), (4, 8));
        var view = (IDebugView)system;

        Assert.Equal("0", Value(view, "World", "tick"));

        system.Order([0, 1], grid.Index(8, 4));
        for (var tick = 0; tick < 3; tick++)
        {
            system.Tick();
        }

        Assert.Equal("3", Value(view, "World", "tick"));
        Assert.StartsWith("3 alive of 3", Value(view, "World", "agents"), StringComparison.Ordinal);
        Assert.Equal("1", Value(view, "World", "groups"));

        system.Remove(2);
        Assert.StartsWith("2 alive of 3", Value(view, "World", "agents"), StringComparison.Ordinal);

        // A heading is rendered by watching Group change, so a group must never
        // appear twice. That ordering is the reason these are rows and not a
        // dictionary.
        var groups = view.Describe().Select(r => r.Group).ToArray();
        var runs = groups.Where((g, i) => i == 0 || groups[i - 1] != g).ToArray();
        Assert.Equal(runs.Length, runs.Distinct().Count());
        Assert.Equal(new[] { "World", "Last tick", "Limits" }, runs);
    }
}
