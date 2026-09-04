using System.Reflection;
using System.Text;
using Nav.InstrumentAudit;

namespace Nav.Tactics.Tests;

/// <summary>
/// The walk pointed at every perception accessor this world has, and then at
/// the verb they all used to hide inside.
/// </summary>
/// <remarks>
/// <see cref="DemoWorld.SightingsFor"/> read as a question and called
/// <c>Look</c>, which cleared the stale flag, rewrote what each side could see,
/// and stamped fresh sightings with the CURRENT tick. So a panel asking a side
/// what it knew resolved perception at the panel's tick rather than at the tick
/// doctrine would have asked -- and <see cref="Models.Sighting.Tick"/> is what
/// doctrine compares against to decide forgetting.
/// <para>
/// The graph does not see it. It records the write to <c>_stale</c> and nothing
/// else, because <c>_visible[side] = seen</c> is a field READ followed by a
/// mutating call on what the field points at. That gap is why this walk reads
/// instructions rather than edges.
/// </para>
/// <para>
/// <b>The resolve moved to the tick edge, so there is nothing left for a reader
/// to cause.</b> All six accessors are pure reads now, and the green on the
/// doctrine-facing three is the evidence that the observer effect is gone at the
/// root rather than merely fenced off behind a view type.
/// </para>
/// <para>
/// <b>A clean walk means nothing unless the walk can still be shown finding
/// something.</b> The control used to be the doctrine queries themselves; it is
/// now <see cref="DemoWorld.Settle"/>, which is where the looking went and which
/// is a verb by every reading.
/// </para>
/// </remarks>
public sealed class InstrumentMutationTests
{
    /// <summary>
    /// The view, walked with NO approved list. A mutation reachable from here is
    /// a fault rather than an entry to add.
    /// </summary>
    [Theory]
    [InlineData(nameof(IPerceptionView.AsOf))]
    [InlineData(nameof(IPerceptionView.PeekHostiles))]
    [InlineData(nameof(IPerceptionView.PeekSightings))]
    [InlineData(nameof(IPerceptionView.PeekRepairPoints))]
    [InlineData(nameof(IPerceptionView.PeekVisibleCells))]
    public void TheViewCausesNothing(string member) =>
        Clean(typeof(IPerceptionView).GetMethod(member) ??
              typeof(IPerceptionView).GetProperty(member)!.GetGetMethod()!);

    /// <summary>
    /// The three doctrine reads, to the same depth and with the same empty
    /// approved list.
    /// </summary>
    /// <remarks>
    /// This is the finding that used to be the control. Doctrine asks these
    /// every tick, and while asking resolved perception the tick a side forgot
    /// on depended on who else had been reading -- so the run was different on
    /// the runs somebody was watching.
    /// </remarks>
    [Theory]
    [InlineData(nameof(DemoWorld.HostilesFor))]
    [InlineData(nameof(DemoWorld.SightingsFor))]
    [InlineData(nameof(DemoWorld.RepairPointsFor))]
    public void TheDoctrineReadsCauseNothingEither(string member) =>
        Clean(typeof(DemoWorld).GetMethod(member)!);

    /// <summary>
    /// The panel's read of who a unit is shooting at, walked the same way.
    /// </summary>
    /// <remarks>
    /// Apart from the two theories above because nothing in the tick asks it:
    /// <c>Fire</c> writes the map and never reads it back, so
    /// this is the one read here whose only caller is a watcher. That is exactly
    /// the shape the attribute exists for -- a member nobody would notice
    /// causing something, because nobody but an observer calls it.
    /// </remarks>
    [Fact]
    public void TheTargetReadCausesNothing() =>
        Clean(typeof(DemoWorld).GetMethod(nameof(DemoWorld.TargetOf))!);

    [Fact]
    public void TheWalkStillFindsWhatSettleCauses()
    {
        var walk = new MutationWalk(typeof(MovementSystem).Assembly, typeof(DemoWorld).Assembly);
        var root = typeof(DemoWorld).GetMethod(nameof(DemoWorld.Settle))!;

        var found = walk.From(root);
        var report = Report(walk, root, found);

        // The look is reachable from the settle and nowhere else that a reader
        // can get at, which is the whole shape of the change: one verb, at the
        // end of the tick, carrying every write perception makes.
        var caused = found.Where(m => m.Suppressed is null && m.Site == "DemoWorld.Look").ToList();

        Assert.True(caused.Count > 0, report);
        Assert.Contains(caused, m => m.What == "this._stale =");
        Assert.Contains(caused, m => m.What == "this._asOf =");
        Assert.Contains(caused, m => m.What == "_visible.set_Item()");
        Assert.Contains(caused, m => m.What == "_pads.set_Item()");
    }

    /// <summary>Walks one member and fails with the whole report if anything survives.</summary>
    private static void Clean(MethodBase root)
    {
        var walk = new MutationWalk(typeof(MovementSystem).Assembly, typeof(DemoWorld).Assembly);
        var found = walk.From(root);
        var report = Report(walk, root, found);

        // A member with no body -- an interface declaration -- would report
        // clean without the walk having reached an implementation, and its
        // cleanliness would be about the contract rather than about the world.
        Assert.True(walk.Visited > 0, report);

        var caused = found.Where(m => m.Suppressed is null).ToList();
        Assert.True(caused.Count == 0, report);
    }

    private static string Report(MutationWalk walk, MethodBase root, IReadOnlyList<Mutation> found)
    {
        var report = new StringBuilder(MutationWalk.Report(root, found));
        report.Append($"\n{walk.Visited} methods read, {walk.OwnedDropped} mutations dropped as owned");
        foreach (var note in walk.Notes.Distinct())
        {
            report.Append("\n  note: ").Append(note);
        }

        return report.ToString();
    }
}
