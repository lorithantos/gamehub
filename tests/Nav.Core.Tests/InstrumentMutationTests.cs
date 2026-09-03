using System.Text;
using Nav.Core;
using Nav.InstrumentAudit;

namespace Nav.Core.Tests;

/// <summary>
/// Every member marked <see cref="ObservesAttribute"/>, walked through IL, with
/// everything it can reach that changes state.
/// </summary>
/// <remarks>
/// LOOKING MUST NOT CAUSE. An instrument that mutates the run moves the numbers
/// this project decides on, and moves them only when somebody is watching --
/// which is the one condition under which nobody can see it happening.
/// <para>
/// Reading by hand found the two that <see cref="Interfaces.IDistanceFieldView"/>
/// and <see cref="Interfaces.IReservationView"/> were written to close, and
/// could never show there is no third. This is that reading done mechanically.
/// </para>
/// </remarks>
public sealed class InstrumentMutationTests
{
    /// <summary>
    /// Mutations a human has looked at and accepted, as <c>site: what</c>.
    /// EMPTY, and that is the finding: every instrument in this assembly is
    /// clean without one entry of excuse.
    /// </summary>
    private static readonly HashSet<string> Approved = [];

    [Fact]
    public void AnInstrumentReachesNoStateItDoesNotOwn()
    {
        var assembly = typeof(MovementSystem).Assembly;
        var walk = new MutationWalk(assembly);
        var instruments = MutationWalk.Instruments(assembly);

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

        // A GREEN AUDIT THAT READ NOTHING IS NOT A GREEN AUDIT. These pin that
        // the walk got into the bodies and had mutations to judge, so a change
        // that quietly stops it resolving calls fails here rather than passing.
        Assert.True(walk.Visited > 50, $"only {walk.Visited} methods read");
        Assert.True(walk.OwnedDropped > 20, $"only {walk.OwnedDropped} owned mutations seen");
        Assert.True(unapproved == 0, report.ToString());
    }

    /// <summary>
    /// The detector's teeth, on an instrument written to have the fault. Without
    /// it a clean sweep above says nothing: a walk that resolved no calls would
    /// report exactly the same green.
    /// </summary>
    [Fact]
    public void TheWalkSeparatesWhatAnInstrumentOwnsFromWhatItDoesNot()
    {
        var walk = new MutationWalk(typeof(InstrumentMutationTests).Assembly);
        var root = typeof(Meddler).GetMethod(nameof(Meddler.Describe))!;

        var found = walk.From(root).Where(m => m.Suppressed is null).ToList();
        var report = MutationWalk.Report(root, found);

        Assert.Contains(found, m => m.Origin == Origin.This && m.What == "this._asks =");
        Assert.Contains(found, m => m.Origin == Origin.Field && m.What == "_seen.Add()");
        Assert.True(found.Count == 2, report);
    }

    /// <summary>
    /// WHAT IT GETS WRONG, IN BOTH DIRECTIONS, PINNED. Neither of these is a bug
    /// to fix here; both are the price of a receiver traced through one method
    /// with no interprocedural knowledge, and the audit above is only worth
    /// reading beside them.
    /// </summary>
    [Fact]
    public void TheWalkMissesWhatItCannotSeeAndInventsWhatItCannotFollow()
    {
        var walk = new MutationWalk(typeof(InstrumentMutationTests).Assembly);

        // FALSE NEGATIVE. The list is mutated through a delegate held in a
        // field. The call is Action.Invoke in the framework, so there is no body
        // to read and no name on the mutator list, and the walk reports nothing.
        var indirect = walk.From(typeof(Indirect).GetMethod(nameof(Indirect.Describe))!)
                           .Where(m => m.Suppressed is null)
                           .ToList();
        Assert.Empty(indirect);

        // FALSE POSITIVE. The list was made two lines away, in a method the walk
        // does not follow the return value of, so a scratch list reads as state
        // of unknown provenance.
        var borrowed = walk.From(typeof(Borrower).GetMethod(nameof(Borrower.Describe))!)
                           .Where(m => m.Suppressed is null)
                           .ToList();
        Assert.Single(borrowed);
        Assert.Equal(Origin.Unknown, borrowed[0].Origin);
    }

    /// <summary>An instrument that causes, so the walk has something to fail on.</summary>
    private sealed class Meddler
    {
        private readonly List<int> _seen = [];
        private int _asks;

        [Observes]
        public IReadOnlyList<int> Describe()
        {
            _asks++;
            _seen.Add(_asks);

            var rows = new List<int>(_seen);
            rows.Add(_asks);
            rows.Sort();
            return rows;
        }
    }

    /// <summary>An instrument that causes through a delegate, which the walk cannot follow.</summary>
    private sealed class Indirect
    {
        private readonly List<int> _seen = [];
        private readonly Action<int> _record;

        public Indirect() => _record = _seen.Add;

        [Observes]
        public IReadOnlyList<int> Describe()
        {
            _record(1);
            return _seen;
        }
    }

    /// <summary>An instrument that owns its scratch but got it from a call.</summary>
    private sealed class Borrower
    {
        private static List<int> Fresh() => [];

        [Observes]
        public IReadOnlyList<int> Describe()
        {
            var rows = Fresh();
            rows.Add(1);
            return rows;
        }
    }
}
