using System.Globalization;

namespace Nav.Core;

/// <summary>
/// An immutable 8-connected grid map in the Moving AI benchmark format.
/// </summary>
/// <remarks>
/// The origin is the upper-left corner: <c>x</c> is the column, <c>y</c> is the
/// row, and the file is row-major.
/// <para>
/// Cell identity across this codebase is the <em>flat integer index</em>
/// <c>y * Width + x</c>, not a coordinate pair. That keeps the search's state
/// arrays dense and stops the inner loop allocating tuples.
/// </para>
/// <para>
/// The backing store is a flat <c>bool[]</c> for the same reason: a jagged array
/// would put a pointer chase between the search and every cell it touches.
/// </para>
/// </remarks>
public sealed class Grid
{
    /// <summary>type, height, width, map.</summary>
    private const int HeaderLines = 4;

    private readonly bool[] _passable;

    private Grid(int width, int height, bool[] passable, int passableCount)
    {
        Width = width;
        Height = height;
        _passable = passable;
        PassableCount = passableCount;
    }

    /// <summary>
    /// Columns, in cells. With <see cref="Height"/> it fixes the flat index
    /// <c>y * Width + x</c> that is cell identity everywhere in this codebase.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Rows, in cells. Taken from the header, and the body must have exactly this
    /// many map lines -- the parser refuses a file where the two disagree.
    /// </summary>
    public int Height { get; }

    /// <summary>Total cells, passable or not. The length every search array wants.</summary>
    public int CellCount => _passable.Length;

    /// <summary>Walkable cells. Counted once during parsing; the grid never changes.</summary>
    public int PassableCount { get; }

    /// <summary>
    /// True if <paramref name="x"/>, <paramref name="y"/> names a cell of this
    /// map. Says nothing about whether that cell is walkable; the single unsigned
    /// compare per axis catches negatives without a second test.
    /// </summary>
    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>The flat index of a cell. Callers are expected to have checked bounds.</summary>
    public int Index(int x, int y) => (y * Width) + x;

    /// <summary>
    /// The <c>x</c> a flat index came from -- half the inverse of
    /// <see cref="Index(int, int)"/>. Unchecked: an index off the map returns a
    /// number rather than a complaint.
    /// </summary>
    public int ColumnOf(int index) => index % Width;

    /// <summary>
    /// The <c>y</c> a flat index came from -- the other half of the inverse of
    /// <see cref="Index(int, int)"/>, and unchecked for the same reason.
    /// </summary>
    public int RowOf(int index) => index / Width;

    /// <summary>False for anything off the map, so callers need no separate bounds test.</summary>
    public bool IsPassable(int x, int y) => InBounds(x, y) && _passable[Index(x, y)];

    /// <summary>False for anything off the map, so callers need no separate bounds test.</summary>
    public bool IsPassable(int index) => (uint)index < (uint)_passable.Length && _passable[index];

    /// <summary>
    /// Reads and parses a map file, carrying <paramref name="path"/> into any
    /// <see cref="MapFormatException"/> so a failure names the file it came from.
    /// </summary>
    /// <param name="path">The map file. Neither null nor blank.</param>
    public static Grid FromMapFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>
    /// Parses map text already in memory -- the same parser as
    /// <see cref="FromMapFile(string)"/>, with no source path to report, so an
    /// error locates itself as "map text, line N" instead.
    /// </summary>
    /// <param name="text">A whole map file's contents, header included.</param>
    public static Grid FromMapText(string text) => Parse(text, source: null);

    /// <summary>
    /// Renders the grid back to the map body -- no header, one line per row,
    /// <c>.</c> for passable and <c>@</c> for blocked.
    /// </summary>
    /// <remarks>
    /// Exists so a round-trip is a string comparison rather than a nest of index
    /// assertions. It is lossy on purpose: the two-state model cannot tell trees
    /// from out-of-bounds, and pretending otherwise would make the round-trip test
    /// prove something the type does not actually guarantee.
    /// </remarks>
    public string ToMapBody()
    {
        var rows = new string[Height];
        var row = new char[Width];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                row[x] = _passable[Index(x, y)] ? '.' : '@';
            }

            rows[y] = new string(row);
        }

        return string.Join('\n', rows);
    }

    /// <remarks>
    /// Strict throughout: every disagreement between the header and the body is a
    /// throw naming the line, never a silent repair.
    /// <para>
    /// A map that quietly loads at the wrong dimensions produces a plausible
    /// mirrored world whose scenario costs are all subtly wrong — far more
    /// expensive to diagnose than a refusal to load.
    /// </para>
    /// </remarks>
    private static Grid Parse(string text, string? source)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd('\r');
        }

        if (lines.Length < HeaderLines)
        {
            throw new MapFormatException(
                source,
                lines.Length,
                $"expected {HeaderLines} header lines, the file has {lines.Length}");
        }

        var type = RequireHeaderValue(lines[0], "type", source, lineNumber: 1);
        if (!string.Equals(type, "octile", StringComparison.Ordinal))
        {
            throw new MapFormatException(source, 1, $"unsupported map type '{type}'; only 'octile' is understood");
        }

        var height = RequireHeaderCount(lines[1], "height", source, lineNumber: 2);
        var width = RequireHeaderCount(lines[2], "width", source, lineNumber: 3);

        if (!string.Equals(lines[3].Trim(), "map", StringComparison.Ordinal))
        {
            throw new MapFormatException(source, 4, $"expected 'map', found '{lines[3]}'");
        }

        var passable = new bool[width * height];
        var passableCount = 0;

        for (var y = 0; y < height; y++)
        {
            var lineNumber = HeaderLines + y + 1;
            if (HeaderLines + y >= lines.Length)
            {
                throw new MapFormatException(
                    source,
                    lineNumber,
                    $"header declares height {height}, but the file ends after {y} map row(s)");
            }

            var row = lines[HeaderLines + y];
            if (row.Length != width)
            {
                throw new MapFormatException(
                    source,
                    lineNumber,
                    $"header declares width {width}, but this row has {row.Length} character(s)");
            }

            for (var x = 0; x < width; x++)
            {
                var symbol = row[x];
                if (!Terrain.IsRecognised(symbol))
                {
                    throw new MapFormatException(
                        source,
                        lineNumber,
                        $"unrecognised terrain character '{symbol}' at column {x}");
                }

                if (Terrain.IsPassable(symbol))
                {
                    passable[(y * width) + x] = true;
                    passableCount++;
                }
            }
        }

        // Trailing blank lines are ordinary end-of-file punctuation; trailing
        // CONTENT means the header undercounts the body, which is the same class
        // of bug as it overcounting and gets the same refusal.
        for (var i = HeaderLines + height; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0)
            {
                continue;
            }

            throw new MapFormatException(
                source,
                i + 1,
                $"header declares height {height}, but the file has further map rows");
        }

        return new Grid(width, height, passable, passableCount);
    }

    private static string RequireHeaderValue(string line, string keyword, string? source, int lineNumber)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !string.Equals(parts[0], keyword, StringComparison.Ordinal))
        {
            throw new MapFormatException(source, lineNumber, $"expected '{keyword} <value>', found '{line}'");
        }

        return parts[1];
    }

    private static int RequireHeaderCount(string line, string keyword, string? source, int lineNumber)
    {
        var value = RequireHeaderValue(line, keyword, source, lineNumber);

        // InvariantCulture here for the same reason the scenario parser needs it:
        // the file's format is fixed, the machine's culture is not.
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0)
        {
            throw new MapFormatException(source, lineNumber, $"'{keyword}' must be a positive integer, found '{value}'");
        }

        return count;
    }
}
