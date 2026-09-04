using System.Text;

using Nav.Core;
using Nav.InstrumentAudit;

namespace Nav.Viewer.Tests;

/// <summary>
/// The walk pointed at the viewer: every member marked
/// <see cref="ObservesAttribute"/> here, through IL, with everything it can reach
/// that changes state.
/// </summary>
/// <remarks>
/// LOOKING MUST NOT CAUSE, at the layer where the temptation is worst. A panel
/// reads the movement system once a frame and only while somebody is watching, so
/// anything it moves moves on exactly the runs nobody is measuring.
/// <para>
/// <b>The app is not the subject; the parts of it that only look are.</b>
/// <see cref="ViewerApp"/> legitimately rewrites camera, selection, pause and the
/// rows themselves in <see cref="ViewerApp.Update"/> -- that is the viewer doing
/// its job. What is marked is what claims to change nothing: the session's five
/// passthroughs, the two builders that turn them into text,
/// <see cref="ViewerApp.Render"/>, and <see cref="InputAccumulator.Snapshot"/>.
/// </para>
/// <para>
/// The last two are here because the rule widened. Render is the frame's widest
/// read and draws what it read; Snapshot is what the accumulator holds, and used
/// to empty the frame's presses on its way out -- which changed nothing in the
/// simulation and was exactly the fault anyway.
/// </para>
/// <para>
/// Nav.Core is walked with it rather than treated as a wall. Every marked member
/// here is a passthrough one line deep, so a walk that stopped at the assembly
/// boundary would report the forwarding and nothing about what it forwards to.
/// </para>
/// </remarks>
public sealed class InstrumentMutationTests
{
    /// <summary>
    /// Mutations a human has looked at and accepted, as <c>site: what</c>.
    /// EMPTY, and that is the finding: the viewer's instruments reach nothing
    /// they do not own without one entry of excuse.
    /// </summary>
    private static readonly HashSet<string> Approved = [];

    [Fact]
    public void AViewerInstrumentReachesNoStateItDoesNotOwn()
    {
        var viewer = typeof(ViewerApp).Assembly;
        var walk = new MutationWalk(typeof(MovementSystem).Assembly, viewer);
        var instruments = MutationWalk.Instruments(viewer);

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

        // A GREEN AUDIT THAT READ NOTHING IS NOT A GREEN AUDIT. Most of what is
        // marked here forwards into Nav.Core in one line, so these are what say
        // the walk crossed the boundary and had mutations to judge rather than
        // stopping at five passthroughs and calling them clean.
        Assert.True(walk.Visited > 75, $"only {walk.Visited} methods read");
        Assert.True(walk.OwnedDropped > 35, $"only {walk.OwnedDropped} owned mutations seen");
        Assert.True(unapproved == 0, report.ToString());
    }

    /// <summary>
    /// The detector's teeth, on an instrument written to have the viewer's own
    /// version of the fault. Without it the sweep above says nothing: a walk that
    /// resolved no calls would report exactly the same green.
    /// </summary>
    [Fact]
    public void TheWalkCatchesAPanelThatOrdersWhileItReads()
    {
        var walk = new MutationWalk(typeof(MovementSystem).Assembly, typeof(InstrumentMutationTests).Assembly);
        var root = typeof(Meddler).GetMethod(nameof(Meddler.Describe))!;

        var found = walk.From(root).Where(m => m.Suppressed is null).ToList();
        var report = MutationWalk.Report(root, found);

        Assert.Contains(found, m => m.Origin == Origin.This && m.What == "this._reads =");
        Assert.Contains(found, m => m.Origin == Origin.Field && m.What == "_watched.Add()");
        Assert.True(found.Count == 2, report);
    }

    /// <summary>A panel that causes, so the walk has something to fail on.</summary>
    private sealed class Meddler
    {
        private readonly List<int> _watched = [];
        private int _reads;

        [Observes]
        public IReadOnlyList<DebugRow> Describe()
        {
            _reads++;
            _watched.Add(_reads);

            var rows = new List<DebugRow>();
            foreach (var id in _watched)
            {
                rows.Add(new DebugRow("Viewer", "watched", $"{id}"));
            }

            return rows;
        }
    }
}
