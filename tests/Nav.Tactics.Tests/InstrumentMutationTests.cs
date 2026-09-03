using System.Text;
using Nav.InstrumentAudit;

namespace Nav.Tactics.Tests;

/// <summary>
/// The walk pointed at the known positive: a query that resolves perception as a
/// side effect of being asked.
/// </summary>
/// <remarks>
/// <see cref="DemoWorld.SightingsFor"/> reads as a question and calls
/// <c>Look</c>, which clears the stale flag, rewrites what each side can see,
/// and stamps fresh sightings with the CURRENT tick. So a panel asking a side
/// what it knows resolves perception at the panel's tick rather than at the tick
/// doctrine would have asked -- and <see cref="Models.Sighting.Tick"/> is what
/// doctrine compares against to decide forgetting.
/// <para>
/// The graph does not see it. It records the write to <c>_stale</c> and nothing
/// else, because <c>_visible[side] = seen</c> is a field READ followed by a
/// mutating call on what the field points at. That gap is why this walk reads
/// instructions rather than edges.
/// </para>
/// <para>
/// <b>Nothing here is fixed.</b> The finding is a design question about where
/// perception should be resolved, not a bug with an obvious edit; this pins what
/// the walk can see so that answering it later is a change somebody notices.
/// </para>
/// </remarks>
public sealed class InstrumentMutationTests
{
    [Fact]
    public void TheWalkFindsWhatSightingsForCauses()
    {
        var walk = new MutationWalk(typeof(MovementSystem).Assembly, typeof(DemoWorld).Assembly);
        var root = typeof(DemoWorld).GetMethod(nameof(DemoWorld.SightingsFor))!;

        var found = walk.From(root);
        var report = new StringBuilder(MutationWalk.Report(root, found));
        report.Append($"\n{walk.Visited} methods read, {walk.OwnedDropped} mutations dropped as owned");
        foreach (var note in walk.Notes.Distinct())
        {
            report.Append("\n  note: ").Append(note);
        }

        var caused = found.Where(m => m.Suppressed is null && m.Site == "DemoWorld.Look").ToList();

        Assert.True(caused.Count > 0, report.ToString());
        Assert.Contains(caused, m => m.What == "this._stale =");
        Assert.Contains(caused, m => m.What == "_visible.set_Item()");
        Assert.Contains(caused, m => m.What == "_pads.set_Item()");
    }

    [Fact]
    public void TheOtherTwoQueriesCauseTheSameThing()
    {
        var walk = new MutationWalk(typeof(MovementSystem).Assembly, typeof(DemoWorld).Assembly);

        foreach (var name in new[] { nameof(DemoWorld.HostilesFor), nameof(DemoWorld.RepairPointsFor) })
        {
            var root = typeof(DemoWorld).GetMethod(name)!;
            var found = walk.From(root);
            Assert.True(
                found.Any(m => m.Suppressed is null && m.Site == "DemoWorld.Look"),
                MutationWalk.Report(root, found));
        }
    }
}
