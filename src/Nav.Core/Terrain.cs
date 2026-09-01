namespace Nav.Core;

/// <summary>
/// Which Moving AI terrain characters can be walked on.
/// </summary>
/// <remarks>
/// A table rather than a <c>switch</c>, because milestone 2 makes these rules
/// configurable and data is the thing you can swap at runtime. The table is also
/// the faster shape: one array read on a byte indexed by the character itself,
/// with no branch chain to walk.
/// <para>
/// Three states, not two. "Blocked" and "not a terrain character at all" are
/// different facts, and collapsing them would let a corrupt file parse as a
/// solid wall -- a plausible-looking map that fails much later and confusingly.
/// </para>
/// <para>
/// <c>S</c> (swamp) and <c>W</c> (water) are impassable here. They carry
/// conditional semantics in the multi-terrain benchmark variants -- swamp is
/// enterable only from regular terrain, water traversable only from water -- but
/// the standard problems and the published optimal costs we validate against use
/// only <c>.</c> <c>G</c> <c>@</c> <c>O</c> <c>T</c>. Treating them as blocked is
/// what the oracle expects; the conditional rules are a later milestone's problem
/// and are exactly the kind of thing this table is shaped to accept.
/// </para>
/// </remarks>
internal static class Terrain
{
    private const byte Unrecognised = 0;
    private const byte Blocked = 1;
    private const byte Open = 2;

    /// <summary>
    /// Covers the whole of ASCII. Every terrain character in the format is well
    /// below this, so anything at or above it is unrecognised by definition and
    /// the bounds check doubles as the lookup's guard.
    /// </summary>
    private const int TableLength = 128;

    /// <summary>
    /// The rules themselves, kept as a declared list so the table below is
    /// derived rather than hand-maintained. This is the part milestone 2 will
    /// lift out of a static field and into a loaded configuration.
    /// </summary>
    private static readonly (char Symbol, bool Passable)[] Rules =
    [
        ('.', true),   // passable terrain
        ('G', true),   // passable terrain
        ('@', false),  // out of bounds
        ('O', false),  // out of bounds
        ('T', false),  // trees
        ('S', false),  // swamp -- see remarks
        ('W', false),  // water -- see remarks
    ];

    private static readonly byte[] Table = BuildTable();

    private static byte[] BuildTable()
    {
        var table = new byte[TableLength];
        foreach (var (symbol, passable) in Rules)
        {
            table[symbol] = passable ? Open : Blocked;
        }

        return table;
    }

    /// <summary>True if <paramref name="symbol"/> is a terrain character the format defines.</summary>
    public static bool IsRecognised(char symbol) =>
        symbol < TableLength && Table[symbol] != Unrecognised;

    /// <summary>
    /// True if <paramref name="symbol"/> can be walked on. An unrecognised
    /// character answers false, so callers that skip <see cref="IsRecognised"/>
    /// fail closed rather than open.
    /// </summary>
    public static bool IsPassable(char symbol) =>
        symbol < TableLength && Table[symbol] == Open;
}
