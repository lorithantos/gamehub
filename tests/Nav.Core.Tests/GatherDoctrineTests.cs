namespace Nav.Core.Tests;

/// <summary>
/// The doctrine passes, driven directly through the seam.
/// </summary>
/// <remarks>
/// Every rule here arrived as a bug fix and, until the seam became an interface,
/// was pinned only by whole-scenario arrival counts. A scenario test says
/// "arrivals fell from 24 to 19"; these say which rule stopped holding.
/// </remarks>
public sealed class GatherDoctrineTests
{
    [Fact]
    public void AStalledMemberOnGroundNoWorseThanItsGoalParksWhereItStands()
    {
        // The countermand fixture ended with a unit squatting on the DESTINATION
        // ITSELF, walled in by later arrivals, still assigned a cell two steps
        // away it could no longer reach. Arriving beats replanning to somewhere
        // worse.
        var ops = new FakeGroupOps()
            .With(id: 0, cell: 10, goal: 20, stalledReplans: 2)
            .Cost(10, 4.0)
            .Cost(20, 6.0);

        new GatherDoctrine().Advance(ops);

        Assert.Equal([(0, 10)], ops.Claims);
    }

    [Fact]
    public void AMemberMerelyPausingInTrafficDoesNotParkEarly()
    {
        // Stall-gated on purpose. Without the gate a unit that waited one tick in
        // a queue would claim the road it was standing on, which is how a group
        // settles in a corridor instead of at its destination.
        var ops = new FakeGroupOps()
            .With(id: 0, cell: 10, goal: 20, stalledReplans: 0)
            .Cost(10, 4.0)
            .Cost(20, 6.0);

        new GatherDoctrine().Advance(ops);

        Assert.Empty(ops.Claims);
    }

    [Fact]
    public void AStalledMemberOnWorseGroundThanItsGoalKeepsWalking()
    {
        // stalledReplans is 1, not 3, and the difference is the test. At 2 or more
        // the member is HARD stalled, ReconcilePass takes over, and with no
        // reachable spot it falls back to claiming the member's own cell -- which
        // is correct behaviour and would make this assertion pass for the wrong
        // reason. One failed replan isolates SettleWhereYouStand.
        var ops = new FakeGroupOps()
            .With(id: 0, cell: 10, goal: 20, stalledReplans: 1)
            .Cost(10, 9.0)
            .Cost(20, 6.0);

        new GatherDoctrine().Advance(ops);

        Assert.Empty(ops.Claims);
    }

    [Fact]
    public void ClaimPassNeverOffersASlotFurtherOutThanTheMemberAlreadyStands()
    {
        // "Never claim outward." A unit already at the rim -- a displaced claimant
        // after a squatter's swap, most often -- offered the innermost REMAINING
        // slot can be offered one BEHIND itself, and it walks away from the crowd
        // to reach it. Standing put is always better.
        var ops = new FakeGroupOps { Destination = 0, Slots = [1, 2, 3] }
            .With(id: 0, cell: 5, goal: 1, stalledReplans: 0)
            .Cost(5, 2.0)     // the member is already closer than any free slot
            .Cost(1, 7.0)
            .Cost(2, 8.0)
            .Cost(3, 9.0);

        new GatherDoctrine().Advance(ops);

        Assert.Empty(ops.Claims);
    }

    [Fact]
    public void ClaimPassTakesTheInnermostSlotStillOpenRatherThanTheNearestOne()
    {
        // Member-nearest picking was tried and measured WORSE -- 11 late backward
        // moves against 4 -- because a group approaching from one face burns the
        // near slots first and forces every latecomer through the pack. Slot 1 is
        // taken, so the next member must take 2 (innermost remaining) and not 3
        // (nearest to it).
        //
        // Member 1 holding slot 1 is also what makes the claim possible at all:
        // the frontier is the outermost CLAIMED slot, so with nobody settled the
        // radius is just the margin and a member out at cost 3 is not yet "near".
        // Fill like water -- a slot is booked moments before it is filled.
        var ops = new FakeGroupOps { Destination = 1, Slots = [1, 2, 3] }
            .With(id: 0, cell: 5, goal: 1, stalledReplans: 0)
            .With(id: 1, cell: 1, goal: 1, hasSlot: true)
            .Cost(5, 3.0)
            .Cost(1, 1.0)
            .Cost(2, 2.0)
            .Cost(3, 2.5);

        new GatherDoctrine().Advance(ops);

        Assert.Equal([(0, 2)], ops.Claims);
    }

    [Fact]
    public void AMemberStandingOnItsOwnGoalClaimsItByStandingThere()
    {
        var ops = new FakeGroupOps { Destination = 7, Slots = [7] }
            .With(id: 0, cell: 7, goal: 7, stalledReplans: 0)
            .Cost(7, 0.0);

        new GatherDoctrine().Advance(ops);

        Assert.Equal([(0, 7)], ops.Claims);
    }

    [Fact]
    public void ASettledGroupIsLeftEntirelyAlone()
    {
        // All three passes fall straight through on a group that has finished, and
        // that is what makes leaving the doctrine running cost nothing. If this
        // ever fails, a settled blob has started churning.
        var ops = new FakeGroupOps { Destination = 1, Slots = [1, 2] }
            .With(id: 0, cell: 1, goal: 1, hasSlot: true)
            .With(id: 1, cell: 2, goal: 2, hasSlot: true)
            .Cost(1, 0.0)
            .Cost(2, 1.0);

        new GatherDoctrine().Advance(ops);

        Assert.Empty(ops.Claims);
        Assert.Empty(ops.Releases);
    }

    [Fact]
    public void MeteringLetsAConvoyThroughAndHoldsEverybodyBehindIt()
    {
        // The convoy is gate.Width * ConvoyDepth = 1 * 4 members, so the queue has
        // to be LONGER than four before anyone is held at all. A first draft used
        // six members of whom only four cleared the gate, held nobody, and looked
        // like a broken meter rather than a queue that was simply short enough to
        // walk through -- which is exactly the behaviour intended.
        //
        // Costs are chosen against the real constants: PassedMargin is one
        // diagonal (~1.414) and ContactRange two (~2.828), so a member is "beyond
        // the gate" past 6.42 and the meter is dormant unless the leader is inside
        // 9.24.
        var ops = new FakeGroupOps
        {
            Destination = 0,
            Slots = [0],
            Chokepoints = [new Chokepoint(Cell: 50, Width: 1)],
        }
            .Cost(50, 5.0)
            .Cost(0, 0.0);

        for (var id = 0; id < 8; id++)
        {
            ops.With(id, cell: 100 + id, goal: 0).Cost(100 + id, 7.0 + (0.5 * id));
        }

        new MeteredGatherDoctrine().Advance(ops);

        // Four through, four held -- and the held ones are the FURTHEST four,
        // because the queue is ordered by distance to the gate.
        Assert.Equal([4, 5, 6, 7], ops.Holds.Select(h => h.Id).Order());
        Assert.All(ops.Holds, h => Assert.Equal(2, h.Ticks));
    }

    [Fact]
    public void ASquatterTakesTheClaimOfAnAbsentMemberAndSendsItBackToTheQueue()
    {
        // The squatter's swap. Member 0 is hard-stalled on cell 10, which member 1
        // claimed from out at cell 40. The only reachable spot, 30, lies farther
        // out than 10 but nearer than member 0's own goal, so the pass would
        // otherwise march it outward. Instead it takes the cell it is standing on
        // and member 1 goes back to the queue to claim again.
        //
        // Member 0 HOLDS a slot so that ClaimPass, which only looks at the
        // un-slotted, leaves it alone and the reconcile pass is the one under test.
        var ops = new FakeGroupOps { Slots = [30] }
            .With(id: 0, cell: 10, goal: 20, hasSlot: true, stalledReplans: 2)
            .With(id: 1, cell: 40, goal: 10, hasSlot: true)
            .Cost(10, 4.0)
            .Cost(20, 9.0)
            .Cost(30, 6.0)
            .Cost(40, 12.0);

        new GatherDoctrine().Advance(ops);

        Assert.Equal([1], ops.Releases);
        Assert.Equal([(0, 10)], ops.Claims);
    }

    [Fact]
    public void ASquatterOnAnotherGroupsClaimStaysPutAndReleasesNobody()
    {
        // The same picture, but the claimant belongs to a different group.
        // ClaimantOf names it, because it is system-wide like IsClaimed, and that
        // is exactly why the doctrine checks membership before releasing: another
        // group's member is not this doctrine's to send anywhere. The fake refuses
        // the release the way the real seam does, so a doctrine that forgot the
        // check fails here loudly rather than passing by accident.
        var ops = new FakeGroupOps { Slots = [30] }
            .With(id: 0, cell: 10, goal: 20, hasSlot: true, stalledReplans: 2)
            .ClaimedBy(10, outsider: 9)
            .Cost(10, 4.0)
            .Cost(20, 9.0)
            .Cost(30, 6.0);

        new GatherDoctrine().Advance(ops);

        Assert.Empty(ops.Releases);
        Assert.Empty(ops.Claims);
    }
}
