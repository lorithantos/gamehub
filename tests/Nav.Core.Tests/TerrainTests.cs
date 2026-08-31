namespace Nav.Core.Tests;

public sealed class TerrainTests
{
    [Theory]
    [InlineData('.')]
    [InlineData('G')]
    public void GroundIsPassable(char symbol)
    {
        Assert.True(Terrain.IsRecognised(symbol));
        Assert.True(Terrain.IsPassable(symbol));
    }

    [Theory]
    [InlineData('@')]
    [InlineData('O')]
    [InlineData('T')]
    [InlineData('S')]  // swamp -- conditional in the multi-terrain variants, blocked here
    [InlineData('W')]  // water -- likewise
    public void BlockedTerrainIsRecognisedButNotPassable(char symbol)
    {
        Assert.True(Terrain.IsRecognised(symbol));
        Assert.False(Terrain.IsPassable(symbol));
    }

    [Theory]
    [InlineData('X')]
    [InlineData('g')]   // lower case is a different character, not a lenient spelling
    [InlineData(' ')]
    [InlineData('\0')]
    [InlineData('é')]  // outside the table, so the bounds check is what answers
    public void AnythingElseIsUnrecognisedAndFailsClosed(char symbol)
    {
        Assert.False(Terrain.IsRecognised(symbol));
        Assert.False(Terrain.IsPassable(symbol));
    }
}
