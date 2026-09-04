namespace Nav.Worlds.Tests;

/// <summary>
/// The ORDER inside <see cref="GuardRetreatWorld.Step"/>: doctrine, then the
/// board, then the settle, then the fallen taken off.
/// </summary>
/// <remarks>
/// Until the tick lived in the world, the only thing pinning any of this was a
/// byte-identical trace hash over five hundred and twenty ticks -- which says a
/// run changed, never which of the four moved. Each test here breaks under one
/// swap and says which one in its name.
/// <para>
/// <b>They probe the order through the board's own broadcast.</b> Nothing here
/// reaches inside <see cref="GuardRetreatWorld.Step"/>: a handler on
/// <see cref="MovementSystem.Happened"/> is woken mid-tick, and what it reads
/// off the world at that instant says what has already happened and what has
/// not. A test that instead played the four calls itself would be the second
/// simulation this move exists to prevent.
/// </para>
/// </remarks>
public sealed class GuardRetreatWorldStepTests
{
    /// <summary>Long enough to reach the first casualty, which falls around tick 22.</summary>
    private const int Ticks = 120;

    /// <summary>
    /// The line moves on tick zero, and it can only do that because it was
    /// ordered before the board was told to advance.
    /// </summary>
    /// <remarks>
    /// A board that ticked first would tick over eight units with no goal but
    /// the cell they are standing on, and the march would start one tick late
    /// for the whole run.
    /// </remarks>
    [Fact]
    public void TheLineIsOrderedBeforeTheBoardMoves()
    {
        var guardWorld = new GuardRetreatWorld();
        var started = guardWorld.Board.Agents.ToDictionary(a => a.Id, a => a.Cell);

        guardWorld.Step(0);

        Assert.Contains(
            guardWorld.Board.Agents,
            a => a.Id < GuardRetreatWorld.Guards && a.Cell != started[a.Id]);
    }

    /// <summary>
    /// The world settles against where the board LEFT everybody, not where it
    /// found them: every settle is stamped with the tick the board had just
    /// finished.
    /// </summary>
    /// <remarks>
    /// <see cref="DemoWorld.AsOf"/> is the probe, and it is the world's own
    /// answer rather than an inference: the settle stamps it with the board's
    /// clock as it finds it, so a settle that ran after the board reads the tick
    /// just finished and one that ran before it reads the tick about to be
    /// played. Checked on every tick of a fight that has waves arriving,
    /// casualties and units rotating through repair, so it is answering for a
    /// board in every state this world puts one in.
    /// </remarks>
    [Fact]
    public void TheWorldSettlesAfterTheBoardHasMovedEverybody()
    {
        var guardWorld = new GuardRetreatWorld();
        var world = guardWorld.World;

        var settledBeforeTheBoardMoved = new List<string>();
        for (var tick = 0; tick < Ticks; tick++)
        {
            guardWorld.Step(tick);
            if (world.AsOf != guardWorld.Board.CurrentTick)
            {
                settledBeforeTheBoardMoved.Add(
                    $"tick {tick}: settled as of {world.AsOf} with the board at {guardWorld.Board.CurrentTick}");
            }
        }

        Assert.Empty(settledBeforeTheBoardMoved);

        // Not a quiet hundred ticks: the probe answered for a board that was
        // shooting, losing units and taking them off.
        Assert.NotEmpty(guardWorld.Waves);
        Assert.Contains(guardWorld.Board.Agents, a => !a.Alive);
    }

    /// <summary>
    /// A unit killed while the world settles is STILL ON THE BOARD for that
    /// tick, and leaves at its edge.
    /// </summary>
    /// <remarks>
    /// <b>A kept property, not an accident.</b> Everything the settle resolves
    /// resolves against a board that still holds the dying unit, so who was shot
    /// at does not depend on which shot happened to land first; the board is put
    /// right afterwards, once. Taking the fallen off inside the settle, or
    /// before it, would empty a cell in the middle of a tick, and every unit
    /// resolved after that would be reading a different board from the ones
    /// resolved before it.
    /// <para>
    /// The probe is the removal event itself: at the instant the board announces
    /// a unit is gone, the world's casualty list must already name it, which it
    /// can only do if the settle has finished.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFallenLeaveTheBoardAfterTheSettleNotDuringIt()
    {
        var guardWorld = new GuardRetreatWorld();
        var world = guardWorld.World;

        var removedAfterTheSettleNamedThem = new List<int>();
        var removedBeforeAnySettleNamedThem = new List<int>();
        guardWorld.Board.Happened += e =>
        {
            if (e.Kind != MovementEventKind.Removed)
            {
                return;
            }

            var named = world.Fallen.Any(f => f.Victim == e.Agent)
                ? removedAfterTheSettleNamedThem
                : removedBeforeAnySettleNamedThem;
            named.Add(e.Agent);
        };

        for (var tick = 0; tick < Ticks && world.Fallen.Count == 0; tick++)
        {
            guardWorld.Step(tick);
        }

        var fallen = world.Fallen.Select(f => f.Victim).Order().ToList();
        Assert.NotEmpty(fallen);
        Assert.Equal(fallen, removedAfterTheSettleNamedThem.Order());
        Assert.Empty(removedBeforeAnySettleNamedThem);

        // Off the board by the tick's edge, and still describable: this is what
        // a caller narrating the tick reads once Step has returned.
        foreach (var victim in fallen)
        {
            Assert.False(guardWorld.Board.Agents.Single(a => a.Id == victim).Alive);
            Assert.NotNull(world.KitOf(victim));
        }
    }

    /// <summary>
    /// A wave is put on the board before the doctrine pass, so it is under
    /// orders on the tick it arrives rather than standing still for one.
    /// </summary>
    /// <remarks>
    /// <see cref="GuardRetreatWorld.Entered"/> is how a caller learns it
    /// happened, since <see cref="GuardRetreatWorld.Step"/> hands nothing back:
    /// the wave on the tick it arrived, and null on every other.
    /// </remarks>
    [Fact]
    public void AWaveArrivesUnderOrdersOnTheTickItEntersAndIsReadableAfterTheStep()
    {
        var guardWorld = new GuardRetreatWorld();

        guardWorld.Step(GuardRetreatWorld.WaveTicks[0]);

        var wave = guardWorld.Entered;
        Assert.NotNull(wave);
        Assert.Equal([wave], guardWorld.Waves);

        var ordered = guardWorld.Board.Agents.Where(a => wave.Members.Contains(a.Id)).ToList();
        Assert.Equal(wave.Members.Count, ordered.Count);
        Assert.All(ordered, a => Assert.NotEqual(a.Cell, a.Goal));

        guardWorld.Step(GuardRetreatWorld.WaveTicks[0] + 1);

        Assert.Null(guardWorld.Entered);
    }

    /// <summary>
    /// The whole tick is reachable through <see cref="IWorld"/>: a holder with
    /// nothing but Nav.Core in front of it drives the fight.
    /// </summary>
    /// <remarks>
    /// The point of the interface living in Nav.Core rather than beside this
    /// class. Everything this test names -- the grid, the board, the step -- is
    /// a Nav.Core type, and a viewer that referenced only Nav.Core could have
    /// written it.
    /// </remarks>
    [Fact]
    public void TheTickIsDrivenThroughTheInterfaceAViewerCanHold()
    {
        IWorld world = new GuardRetreatWorld();
        var started = world.Board.Agents.ToDictionary(a => a.Id, a => a.Cell);

        Assert.Equal(49, world.Grid.Width);
        Assert.Equal(33, world.Grid.Height);

        world.Step(0);

        Assert.Equal(1, world.Board.CurrentTick);
        Assert.Contains(
            world.Board.Agents,
            a => a.Id < GuardRetreatWorld.Guards && a.Cell != started[a.Id]);
    }
}
