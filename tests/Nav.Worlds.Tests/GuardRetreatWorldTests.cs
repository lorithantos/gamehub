namespace Nav.Worlds.Tests;

/// <summary>
/// What the constructor leaves standing: eight guards on side 0 carrying the
/// kits the line was designed around, two pads, and fog on.
/// </summary>
/// <remarks>
/// These are the facts a host would be wiring a window to, and every one of
/// them was invisible while the world lived inside a demo's <c>Play</c> --
/// the only way to check any of it was to run five hundred and twenty ticks
/// and read a trace.
/// <para>
/// Nothing here plays the fight. The tick loop belongs to whoever is running
/// it, and the demo's byte-identical trace is what pins the playing.
/// </para>
/// </remarks>
public sealed class GuardRetreatWorldTests
{
    [Fact]
    public void TheLineIsEightLivingUnitsOnSideZero()
    {
        var guardWorld = new GuardRetreatWorld();

        var agents = guardWorld.Board.Agents;
        Assert.Equal(GuardRetreatWorld.Guards, agents.Count);
        Assert.All(agents, agent => Assert.True(agent.Alive));
        Assert.All(agents, agent => Assert.Equal(0, guardWorld.World.SideOf(agent.Id)));
        Assert.Equal(agents.Select(a => a.Id).Order(), guardWorld.Guard.Members.Order());
    }

    /// <summary>
    /// Kits by id, not by count: which unit carries what decides where the line
    /// can see and what it can hurt, so a reshuffle is a different world even
    /// with the same six tanks and two buggies.
    /// </summary>
    [Fact]
    public void EveryGuardCarriesTheKitItsPositionInTheLineCallsFor()
    {
        var guardWorld = new GuardRetreatWorld();

        var kits = guardWorld.Board.Agents.Select(a => guardWorld.World.KitOf(a.Id)!.Name);
        Assert.Equal(
            ["tank", "buggy", "tank", "tank", "buggy", "tank", "tank", "buggy"],
            kits);
    }

    /// <summary>
    /// Both pads exist and both are already something side 0 can plan a retreat
    /// to, though no guard is within twenty cells of either.
    /// </summary>
    /// <remarks>
    /// A pad watching its own ground is what makes that true, and it is the
    /// whole reason this map can be twenty-two cells wide without the retreat
    /// becoming impossible under fog.
    /// </remarks>
    [Fact]
    public void TwoPadsStandAndBothAreKnownToTheLineWithoutBeingSeenByIt()
    {
        var guardWorld = new GuardRetreatWorld();

        Assert.Equal(2, guardWorld.World.RepairCells.Count);
        Assert.Equal(guardWorld.World.RepairCells.Order(), guardWorld.World.RepairPointsFor(0).Order());
    }

    /// <summary>
    /// Fog is on, and the proof is that the line opens the run knowing nothing:
    /// an omniscient world would already be reporting the enemy it cannot see.
    /// </summary>
    [Fact]
    public void FogIsOnAndTheLineHasSeenNobodyBeforeTheFirstWave()
    {
        var guardWorld = new GuardRetreatWorld();

        Assert.True(guardWorld.World.Fog);
        Assert.Empty(guardWorld.World.HostilesFor(0));
        Assert.Empty(guardWorld.World.SightingsFor(0));
    }

    [Fact]
    public void AWaveArrivesOnItsOwnTickAndNothingArrivesBetween()
    {
        var guardWorld = new GuardRetreatWorld();

        Assert.Empty(guardWorld.Waves);
        Assert.Null(guardWorld.SendWave(1));

        var wave = guardWorld.SendWave(GuardRetreatWorld.WaveTicks[0]);

        Assert.NotNull(wave);
        Assert.Equal(6, wave.Members.Count);
        Assert.All(wave.Members, id => Assert.Equal(1, guardWorld.World.SideOf(id)));
        Assert.All(wave.Members, id => Assert.NotNull(guardWorld.World.KitOf(id)));
        Assert.Equal([wave], guardWorld.Waves);
    }
}
