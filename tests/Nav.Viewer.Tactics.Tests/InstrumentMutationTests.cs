using System.Text;

using Nav.InstrumentAudit;
using Nav.Viewer.Tactics;

namespace Nav.Viewer.Tactics.Tests;

/// <summary>
/// The walk pointed at the seam: every member marked
/// <see cref="ObservesAttribute"/> in this layer, through IL, with everything it
/// can reach that changes state.
/// </summary>
/// <remarks>
/// LOOKING MUST NOT CAUSE, and this is the layer with the most to reach. A row
/// here is built out of a tactics world's health, kits, rank table and
/// perception, so a walk from one <c>Describe</c> crosses into Nav.Tactics and
/// on into Nav.Core -- and the perception half is exactly where the observer
/// effect was found and fixed once already.
/// <para>
/// Nav.Core and Nav.Tactics are walked with this assembly rather than treated as
/// walls. Every row is a one-line read forwarded into one of them, so a walk
/// that stopped at the assembly boundary would report the forwarding and nothing
/// about what it forwards to.
/// </para>
/// </remarks>
public sealed class InstrumentMutationTests
{
    /// <summary>
    /// Mutations a human has looked at and accepted, as <c>site: what</c>.
    /// EMPTY, and that is the finding: this view reaches nothing it does not own
    /// without one entry of excuse.
    /// </summary>
    private static readonly HashSet<string> Approved = [];

    [Fact]
    public void ASeamInstrumentReachesNoStateItDoesNotOwn()
    {
        var seam = typeof(DemoWorldDebugView).Assembly;
        var walk = new MutationWalk(typeof(MovementSystem).Assembly, typeof(DemoWorld).Assembly, seam);
        var instruments = MutationWalk.Instruments(seam);

        Assert.NotEmpty(instruments);

        var report = new StringBuilder();
        var unapproved = 0;
        foreach (var instrument in instruments)
        {
            var found = walk.From(instrument);
            report.Append(MutationWalk.Report(instrument, found)).Append("\n\n");
            unapproved += found.Count(m => m.Suppressed is null && !Approved.Contains($"{m.Site}: {m.What}"));
        }

        report.Append($"{instruments.Count} instruments, {walk.Visited} methods read, ")
              .Append($"{walk.OwnedDropped} mutations dropped as owned, {walk.Notes.Count} notes");
        foreach (var note in walk.Notes.Distinct())
        {
            report.Append("\n  note: ").Append(note);
        }

        // A GREEN AUDIT THAT READ NOTHING IS NOT A GREEN AUDIT. Everything marked
        // here reads a world, so these are what say the walk crossed into
        // Nav.Tactics and had mutations to judge rather than stopping at three
        // row builders and calling them clean.
        Assert.True(walk.Visited > 60, $"only {walk.Visited} methods read");
        Assert.True(walk.OwnedDropped > 30, $"only {walk.OwnedDropped} owned mutations seen");
        Assert.True(unapproved == 0, report.ToString());
    }

    /// <summary>
    /// The detector's teeth, on the verb the world's looking actually lives in.
    /// Without it the sweep above says nothing: a walk that resolved no calls
    /// into Nav.Tactics would report exactly the same green.
    /// </summary>
    /// <remarks>
    /// <see cref="DemoWorld.Settle"/> is the clock edge, and every write
    /// perception makes is behind it. That it is reachable from there and from
    /// nowhere a reader can get at is the shape this whole seam depends on.
    /// </remarks>
    [Fact]
    public void TheWalkStillFindsWhatTheClockEdgeCauses()
    {
        var walk = new MutationWalk(
            typeof(MovementSystem).Assembly,
            typeof(DemoWorld).Assembly,
            typeof(DemoWorldDebugView).Assembly);
        var root = typeof(DemoWorld).GetMethod(nameof(DemoWorld.Settle))!;

        var found = walk.From(root);
        var caused = found.Where(m => m.Suppressed is null && m.Site == "DemoWorld.Look").ToList();
        var report = MutationWalk.Report(root, found);

        Assert.True(caused.Count > 0, report);
        Assert.Contains(caused, m => m.What == "this._asOf =");
        Assert.Contains(caused, m => m.What == "_visible.set_Item()");
    }

    /// <summary>
    /// Taking a squad's view changes nothing, which is what lets the panel take
    /// one on every read.
    /// </summary>
    /// <remarks>
    /// <see cref="Squad.ViewFor"/> is not itself marked, because a doctrine pass
    /// builds the same snapshot through the two-argument <c>Squad.Advance</c>,
    /// and that one is meant to be followed by orders. What has to hold is the
    /// SNAPSHOT half: the constructor reads a board, a membership
    /// and a perception, and if any of those three reads resolved something on
    /// the way past then a panel would be moving the fight it is watching. That
    /// is the exact fault this seam was fixed for once already, in the
    /// perception half, so it is worth a root of its own here.
    /// </remarks>
    [Fact]
    public void TakingASquadsViewForAnInstrumentCausesNothing()
    {
        var walk = new MutationWalk(
            typeof(MovementSystem).Assembly,
            typeof(DemoWorld).Assembly,
            typeof(DemoWorldDebugView).Assembly);
        var root = typeof(Squad).GetMethod(nameof(Squad.ViewFor))!;

        var found = walk.From(root);
        var report = MutationWalk.Report(root, found);

        Assert.True(found.All(m => m.Suppressed is not null), report);

        // And the walk went somewhere: the constructor reads the board and both
        // halves of the perception, so a green from three methods would mean it
        // stopped at the assembly wall rather than that nothing happens.
        Assert.True(walk.Visited > 20, $"only {walk.Visited} methods read\n{report}");
    }
}
