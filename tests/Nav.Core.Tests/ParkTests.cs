namespace Nav.Core.Tests;

/// <summary>
/// The validated park on the real system: a doctrine may stop a unit where it
/// stands, and the unit stays stopped -- but only when the cell can be held.
/// </summary>
/// <remarks>
/// The first attempt to say "stay where you are" from a doctrine failed two
/// ways, and both are pinned here. Claiming the ground underfoot set the goal
/// and did not stop the unit: it walked off along its committed plan and came
/// back, forever. Discarding the plan and holding the cell unasked stood the
/// unit on a cell another plan was committed to cross, and the reservation
/// table's own assertion caught it in five scenarios. So the operation asks the
/// table first, and a refusal changes nothing.
/// </remarks>
public sealed class ParkTests
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

    /// <summary>
    /// Gathers normally, and from <see cref="FromTick"/> on tries to park
    /// <paramref name="member"/> on every pass, recording each answer.
    /// </summary>
    private sealed class ParkOne(int member) : GatherDoctrine
    {
        public int FromTick { get; set; } = int.MaxValue;

        public List<(int Tick, bool Parked)> Attempts { get; } = [];

        public List<(int Tick, string Members)> Passes { get; } = [];

        public override void Advance(IGroupOps ops)
        {
            base.Advance(ops);
            Passes.Add((ops.CurrentTick, string.Join(",", ops.Members)));
            if (ops.CurrentTick >= FromTick && ops.Members.Contains(member))
            {
                Attempts.Add((ops.CurrentTick, ops.Park(member)));
            }
        }
    }

    [Fact]
    public void AParkedMemberStaysWhereItWasStopped()
    {
        // Two units ordered across an open room. Mid-walk the doctrine parks the
        // first one. From then on it does not move, it reads as arrived, and
        // its slot is the cell it was stopped on.
        var (system, grid) = Scene(Room, (0, 4), (0, 5));
        var doctrine = new ParkOne(member: 0);

        system.Order([0, 1], grid.Index(8, 4), doctrine);

        // The first plan lands a latency after the order; walk until it has.
        var start = grid.Index(0, 4);
        for (var tick = 0; tick < 20 && system.Agents[0].Cell == start; tick++)
        {
            system.Tick();
        }

        Assert.NotEqual(start, system.Agents[0].Cell);
        Assert.False(system.Agents[0].Arrived);

        // The pass of the next tick parks it; that tick's move then keeps it there.
        var standingOn = system.Agents[0].Cell;
        doctrine.FromTick = system.CurrentTick;
        system.Tick();

        Assert.Single(doctrine.Attempts);
        Assert.True(doctrine.Attempts[0].Parked, $"passes: {string.Join(" ", doctrine.Passes)}");
        Assert.Equal(standingOn, system.Agents[0].Cell);

        for (var tick = 0; tick < 30; tick++)
        {
            system.Tick();
            Assert.Equal(standingOn, system.Agents[0].Cell);
        }

        Assert.Equal(standingOn, system.Agents[0].Goal);
        Assert.True(system.Agents[0].Arrived);
        Assert.True(system.Agents[1].Arrived, "the other unit never settled");
        Assert.NotEqual(system.Agents[0].Cell, system.Agents[1].Cell);

        // Asking again of a unit already parked is a harmless yes.
        Assert.All(doctrine.Attempts, a => Assert.True(a.Parked));
    }

    private sealed class ParkAnyone(int target) : GatherDoctrine
    {
        public override void Advance(IGroupOps ops)
        {
            base.Advance(ops);
            ops.Park(target);
        }
    }

    [Fact]
    public void ParkingANonMemberIsRefusedBeforeAnythingChanges()
    {
        // Confinement is the seam's, and it holds for the new verb the same as
        // for the old ones: a doctrine handed one group cannot stop another
        // group's unit, even by naming it.
        var (system, grid) = Scene(Room, (0, 4), (0, 5), (0, 6));

        system.Order([2], grid.Index(8, 6));
        system.Order([0, 1], grid.Index(8, 4), new ParkAnyone(target: 2));

        Assert.Throws<ArgumentOutOfRangeException>(system.Tick);
    }
}
