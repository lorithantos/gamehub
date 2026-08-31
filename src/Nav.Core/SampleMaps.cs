namespace Nav.Core;

/// <summary>
/// Maps compiled into the assembly, so the tests and the viewer both have
/// something to work on with nothing downloaded.
/// </summary>
public static class SampleMaps
{
    /// <summary>
    /// A 12x7 map chosen to catch the corner-cutting bug on its own.
    /// </summary>
    /// <remarks>
    /// Start (1,1) to goal (10,5) is 9 + 2*sqrt(2) = 11.82843 across 11 steps when
    /// diagonal squeezes between two blocked cells are refused. An implementation
    /// that permits them returns 10.65685 for the same query, so the number itself
    /// is the diagnosis -- see <c>Movement</c>.
    /// <para>
    /// It is also the viewer's default map, so the viewer runs before any
    /// benchmark data exists on the machine.
    /// </para>
    /// </remarks>
    public const string CornerCutTrap =
        """
        type octile
        height 7
        width 12
        map
        @@@@@@@@@@@@
        @..........@
        @..@@@@@...@
        @......@...@
        @.@@@@.@...@
        @..........@
        @@@@@@@@@@@@
        """;
}
