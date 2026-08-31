namespace Nav.Core.Tests;

/// <summary>
/// Acceptance criteria 1 and 2, plus the row/column transposition pitfall.
/// </summary>
public sealed class GridTests
{
    /// <summary>
    /// Deliberately not square and not symmetric: a transposed read of a 5x3 map
    /// cannot even be built, and the single blocked cell at (1,0) lands at (0,1)
    /// under a transposition that somehow survived the dimension check.
    /// </summary>
    private const string Asymmetric =
        """
        type octile
        height 3
        width 5
        map
        .@...
        .....
        .....
        """;

    [Fact]
    public void Fixture_HasTheDeclaredDimensions()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        Assert.Equal(12, grid.Width);
        Assert.Equal(7, grid.Height);
        Assert.Equal(84, grid.CellCount);
    }

    [Fact]
    public void Fixture_HasTheExpectedPassableCount()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        // 10 + 5 + 9 + 5 + 10 across the five interior rows.
        Assert.Equal(39, grid.PassableCount);
    }

    [Fact]
    public void Fixture_RoundTripsToTheSameLayout()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        var expected = string.Join(
            '\n',
            SampleMaps.CornerCutTrap.ReplaceLineEndings("\n").Split('\n').Skip(4));

        Assert.Equal(expected, grid.ToMapBody());
    }

    [Fact]
    public void TooFewRows_ThrowsNamingTheOffendingLine()
    {
        const string text =
            """
            type octile
            height 4
            width 3
            map
            ...
            ...
            """;

        var ex = Assert.Throws<MapFormatException>(() => Grid.FromMapText(text));

        // Header is four lines, two rows are present, so line 7 is where the
        // third row should have been.
        Assert.Equal(7, ex.LineNumber);
        Assert.Contains("7", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TooManyRows_ThrowsNamingTheOffendingLine()
    {
        const string text =
            """
            type octile
            height 2
            width 3
            map
            ...
            ...
            ...
            """;

        var ex = Assert.Throws<MapFormatException>(() => Grid.FromMapText(text));

        Assert.Equal(7, ex.LineNumber);
    }

    [Fact]
    public void ShortRow_ThrowsNamingTheOffendingLine()
    {
        const string text =
            """
            type octile
            height 3
            width 4
            map
            ....
            ...
            ....
            """;

        var ex = Assert.Throws<MapFormatException>(() => Grid.FromMapText(text));

        Assert.Equal(6, ex.LineNumber);
        Assert.Contains("width 4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrecognisedTerrain_ThrowsNamingTheOffendingLine()
    {
        const string text =
            """
            type octile
            height 2
            width 3
            map
            ...
            .X.
            """;

        var ex = Assert.Throws<MapFormatException>(() => Grid.FromMapText(text));

        Assert.Equal(6, ex.LineNumber);
        Assert.Contains("'X'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonOctileType_IsRefused()
    {
        const string text =
            """
            type hex
            height 1
            width 1
            map
            .
            """;

        var ex = Assert.Throws<MapFormatException>(() => Grid.FromMapText(text));

        Assert.Equal(1, ex.LineNumber);
    }

    [Fact]
    public void TrailingBlankLines_AreTolerated()
    {
        var text = SampleMaps.CornerCutTrap + "\n\n";

        var grid = Grid.FromMapText(text);

        Assert.Equal(39, grid.PassableCount);
    }

    [Fact]
    public void CarriageReturns_AreTolerated()
    {
        var text = SampleMaps.CornerCutTrap.ReplaceLineEndings("\r\n");

        var grid = Grid.FromMapText(text);

        Assert.Equal(12, grid.Width);
        Assert.Equal(39, grid.PassableCount);
    }

    [Fact]
    public void FromMapFile_ReadsTheSameGridAsFromMapText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nav-{Guid.NewGuid():N}.map");
        File.WriteAllText(path, SampleMaps.CornerCutTrap);
        try
        {
            var fromFile = Grid.FromMapFile(path);
            var fromText = Grid.FromMapText(SampleMaps.CornerCutTrap);

            Assert.Equal(fromText.Width, fromFile.Width);
            Assert.Equal(fromText.Height, fromFile.Height);
            Assert.Equal(fromText.ToMapBody(), fromFile.ToMapBody());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromMapFile_NamesTheFileInTheError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nav-{Guid.NewGuid():N}.map");
        File.WriteAllText(path, "type octile\nheight 2\nwidth 3\nmap\n...\n");
        try
        {
            var ex = Assert.Throws<MapFormatException>(() => Grid.FromMapFile(path));

            Assert.Equal(path, ex.SourcePath);
            Assert.Contains(path, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void XIsTheColumnAndYIsTheRow()
    {
        var grid = Grid.FromMapText(Asymmetric);

        Assert.Equal(5, grid.Width);
        Assert.Equal(3, grid.Height);

        // The blocked cell is on the first row, second column. Read transposed it
        // would be on the second row, first column -- which must still be open.
        Assert.False(grid.IsPassable(1, 0));
        Assert.True(grid.IsPassable(0, 1));
    }

    [Fact]
    public void FlatIndexIsRowMajor()
    {
        var grid = Grid.FromMapText(Asymmetric);

        Assert.Equal(1, grid.Index(1, 0));
        Assert.Equal(5, grid.Index(0, 1));

        var index = grid.Index(3, 2);
        Assert.Equal(3, grid.ColumnOf(index));
        Assert.Equal(2, grid.RowOf(index));
    }

    [Fact]
    public void IndexOverloadAgreesWithTheCoordinateOverload()
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                Assert.Equal(grid.IsPassable(x, y), grid.IsPassable(grid.Index(x, y)));
            }
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(12, 0)]
    [InlineData(0, 7)]
    public void OffMapCoordinatesAreNeitherInBoundsNorPassable(int x, int y)
    {
        var grid = Grid.FromMapText(SampleMaps.CornerCutTrap);

        Assert.False(grid.InBounds(x, y));
        Assert.False(grid.IsPassable(x, y));
    }
}
