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
    /// THE DELEGATE HOLE, CLOSED. <see cref="Indirect"/> mutates a field through
    /// an <see cref="Action{T}"/> built in its constructor and invoked in the
    /// instrument, which is the shape <see cref="MovementSystem.Happened"/> has;
    /// the walk reads every body in the assembly for where delegates are stored
    /// before it walks anything, so the store and the call no longer have to be
    /// in the same method.
    /// </summary>
    [Fact]
    public void TheWalkFollowsADelegateHeldInAField()
    {
        var walk = new MutationWalk(typeof(InstrumentMutationTests).Assembly);
        var root = typeof(Indirect).GetMethod(nameof(Indirect.Describe))!;

        var found = walk.From(root).Where(m => m.Suppressed is null).ToList();

        // The receiver travels with the target: List.Add says nothing about WHICH
        // list, and _seen is the whole answer.
        Assert.Contains(found, m => m.Origin == Origin.Field && m.What == "_seen.Add()");
        Assert.True(found.Count == 1, MutationWalk.Report(root, found));

        // THE SHAPE THE TACTICS LAYER ACTUALLY LISTENS ON. Subscribing through an
        // event accessor never writes the backing field itself, so the walk maps
        // add_Happened onto the field of that name or sees nothing at all.
        var fires = typeof(Broadcast).GetMethod(nameof(Broadcast.Describe))!;
        var heard = walk.From(fires).Where(m => m.Suppressed is null).ToList();

        Assert.Contains(heard, m => m.Origin == Origin.Field && m.What == "_heard.Add()");
        Assert.True(heard.Count == 1, MutationWalk.Report(fires, heard));
    }

    /// <summary>
    /// A DELEGATE FIELD IS A SET, NOT A SLOT. <c>+=</c> leaves two targets in one
    /// field and a later assignment leaves a second the walk cannot rule out, so
    /// it answers for all of them. Over-reporting is the direction this detector
    /// is allowed to be wrong in; the other one is a silent lie.
    /// </summary>
    [Fact]
    public void ADelegateFieldAnswersForEveryTargetItWasSeenHolding()
    {
        var walk = new MutationWalk(typeof(InstrumentMutationTests).Assembly);

        var combined = typeof(Multicast).GetMethod(nameof(Multicast.Describe))!;
        var multicast = walk.From(combined).Where(m => m.Suppressed is null).ToList();

        // Both halves of the += mutate, because a target that changed nothing
        // would leave no trace of having been resolved at all.
        Assert.Contains(multicast, m => m.Origin == Origin.Field && m.What == "_seen.Add()");
        Assert.Contains(multicast, m => m.Origin == Origin.This && m.What == "this._asks =");
        Assert.True(multicast.Count == 2, MutationWalk.Report(combined, multicast));

        var moved = typeof(Repointed).GetMethod(nameof(Repointed.Describe))!;
        var repointed = walk.From(moved).Where(m => m.Suppressed is null).ToList();

        // Repoint is never called from the instrument. It is still an answer the
        // field could be holding by the time the instrument runs.
        Assert.Contains(repointed, m => m.What == "_first.Add()");
        Assert.Contains(repointed, m => m.What == "_second.Add()");
        Assert.True(repointed.Count == 2, MutationWalk.Report(moved, repointed));
    }

    /// <summary>
    /// WHAT IT STILL GETS WRONG, IN BOTH DIRECTIONS, PINNED. Neither of these is
    /// a bug to fix here. A delegate handed in from outside was never built in an
    /// assembly the walk reads, and a scratch list handed back by a call has no
    /// origin it can see; both are the price of a value followed one method at a
    /// time, and the audit above is only worth reading beside them.
    /// </summary>
    [Fact]
    public void TheWalkMissesWhatItCannotSeeAndInventsWhatItCannotFollow()
    {
        var walk = new MutationWalk(typeof(InstrumentMutationTests).Assembly);

        // FALSE NEGATIVE. The delegate was handed in rather than built here, so
        // there is no ldftn anywhere in the assembly to follow it back to and the
        // walk has no target to report. It says so instead of staying silent,
        // which is the only reason this one is readable as a hole rather than as
        // a clean instrument.
        var handed = walk.From(typeof(Handed).GetMethod(nameof(Handed.Describe))!)
                         .Where(m => m.Suppressed is null)
                         .ToList();
        Assert.Empty(handed);
        Assert.Contains(
            walk.Notes,
            n => n == "unresolved delegate _record.Invoke in InstrumentMutationTests.Handed.Describe");

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

    /// <summary>An instrument that causes through a delegate built in its constructor.</summary>
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

    /// <summary>Two targets combined into one field, both of which cause.</summary>
    private sealed class Multicast
    {
        private readonly List<int> _seen = [];
        private readonly Action<int> _record;
        private int _asks;

        public Multicast()
        {
            _record = Count;
            _record += _seen.Add;
        }

        [Observes]
        public IReadOnlyList<int> Describe()
        {
            _record(1);
            return _seen;
        }

        private void Count(int value) => _asks += value;
    }

    /// <summary>A delegate field pointed somewhere else after construction.</summary>
    private sealed class Repointed
    {
        private readonly List<int> _first = [];
        private readonly List<int> _second = [];
        private Action<int> _record;

        public Repointed() => _record = _first.Add;

        public void Repoint() => _record = _second.Add;

        [Observes]
        public IReadOnlyList<int> Describe()
        {
            _record(1);
            return _first;
        }
    }

    /// <summary>An instrument that fires an event, the way a system broadcasts.</summary>
    private sealed class Broadcast
    {
        public event Action<int>? Happened;

        [Observes]
        public int Describe()
        {
            Happened?.Invoke(1);
            return 0;
        }
    }

    /// <summary>The listener, which is where the state the broadcast reaches lives.</summary>
    private sealed class Listener
    {
        private readonly List<int> _heard = [];

        public Listener(Broadcast broadcast) => broadcast.Happened += _heard.Add;

        public IReadOnlyList<int> Heard => _heard;
    }

    /// <summary>An instrument holding a delegate it was given rather than built.</summary>
    private sealed class Handed
    {
        private readonly List<int> _seen = [];
        private readonly Action<int> _record;

        public Handed(Action<int> record) => _record = record;

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
