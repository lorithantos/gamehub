using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nav.Demos;

/// <summary>
/// Refreshes a replay page's data from the trace the demo just wrote, leaving
/// every other line of the page alone.
/// </summary>
/// <remarks>
/// The pages under <c>web/</c> are hand-built and stay that way -- the layout,
/// the palette, the drawing and the prose are design work and no generator
/// should touch them. What a generator MUST own is the data, because that is
/// the part that goes stale silently. These pages carried their trace as a
/// hand-packed array, and by the time anyone looked, the guard page was showing
/// four units on a map that had six, no enemy on a map that had one, and no
/// rank at all. Nothing said so; it simply played the wrong demo.
/// <para>
/// So the page is rewritten IN PLACE and only between the boundaries of its
/// <c>trace-data</c> script tag. The file stays a complete, openable page
/// rather than becoming a template that has to be built before it can be read,
/// and refreshing it twice produces the same bytes as refreshing it once.
/// </para>
/// <para>
/// The trace file is the single source. The packer reads what the demo wrote
/// rather than re-running or re-deriving anything, so the page cannot disagree
/// with the recording it claims to be showing -- if they differ, the page is
/// simply out of date and one run fixes it.
/// </para>
/// </remarks>
internal static class ReplayPage
{
    /// <summary>The trace shape this packer knows how to lay out.</summary>
    /// <remarks>
    /// Checked rather than assumed. A version-1 trace has no rank in it, and
    /// packing one at this stride would slide every field after health one place
    /// left -- a page that draws confidently and wrongly, which is worse than
    /// one that refuses.
    /// </remarks>
    private const int ExpectedVersion = 3;

    /// <summary>Numbers per unit in the packed frame array. Must match the reader's STRIDE.</summary>
    private const int Stride = 13;

    private const string Marker = "id=\"trace-data\"";

    /// <summary>
    /// The shape of the run a page was just given: what its hand-written prose
    /// has to be describing for the page to be telling the truth.
    /// </summary>
    /// <remarks>
    /// Reported because the packer owns the data and NOT the words, and the
    /// words are what a reader reads. The guard demo went from four units to
    /// six and gained an enemy; the data followed in the same pass and the
    /// standfirst went on saying "Four guards hold a position in the middle of
    /// the map" until somebody looked at it. Nothing can check prose. What can
    /// be done is put the run's shape on screen at the moment the page is
    /// rewritten, so a change in it is in front of whoever ran the demos rather
    /// than discovered later by a reader.
    /// </remarks>
    /// <param name="Units">Units in every frame.</param>
    /// <param name="Ticks">Frames written.</param>
    /// <param name="Hostiles">Hostile cells in the first frame.</param>
    /// <param name="TopRank">The highest rank any unit reached.</param>
    internal readonly record struct Shape(int Units, int Ticks, int Hostiles, int TopRank)
    {
        /// <summary>One line for the console.</summary>
        public override string ToString() =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Units} units, {Ticks} ticks, {Hostiles} hostile, top rank {TopRank}");
    }

    /// <summary>
    /// Rewrites <paramref name="pagePath"/>'s trace-data block from
    /// <paramref name="tracePath"/>, and reports the shape of what it wrote.
    /// Returns null if there is no page to refresh, which is not an error --
    /// a demo may simply have no replay yet.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The page has no trace-data block, or the trace is a version this packer
    /// does not lay out.
    /// </exception>
    public static Shape? Refresh(string pagePath, string tracePath)
    {
        ArgumentNullException.ThrowIfNull(pagePath);
        ArgumentNullException.ThrowIfNull(tracePath);

        if (!File.Exists(pagePath))
        {
            return null;
        }

        var page = File.ReadAllText(pagePath);
        var marker = page.IndexOf(Marker, StringComparison.Ordinal);
        if (marker < 0)
        {
            throw new InvalidOperationException($"{pagePath} has no {Marker} block to refresh.");
        }

        var start = page.IndexOf('>', marker) + 1;
        var end = page.IndexOf("</script>", start, StringComparison.Ordinal);
        if (start <= 0 || end < 0)
        {
            throw new InvalidOperationException($"{pagePath} has an unterminated {Marker} block.");
        }

        var (packed, shape) = Pack(tracePath);
        File.WriteAllText(pagePath, string.Concat(page.AsSpan(0, start), packed, page.AsSpan(end)));
        return shape;
    }

    private static (string Json, Shape Shape) Pack(string tracePath)
    {
        using var reader = File.OpenText(tracePath);

        var headerLine = reader.ReadLine()
            ?? throw new InvalidOperationException($"{tracePath} is empty.");

        using var header = JsonDocument.Parse(headerLine);
        var root = header.RootElement;

        var version = root.GetProperty("version").GetInt32();
        if (version != ExpectedVersion)
        {
            throw new InvalidOperationException(
                $"{tracePath} is version {version}; this packer lays out version {ExpectedVersion}.");
        }

        var json = new StringBuilder(64 * 1024);
        json.Append("{\"w\":").Append(root.GetProperty("width").GetInt32());
        json.Append(",\"h\":").Append(root.GetProperty("height").GetInt32());
        json.Append(",\"walls\":").Append(JsonSerializer.Serialize(root.GetProperty("walls").GetString()));
        json.Append(",\"repair\":");
        AppendInts(json, root.GetProperty("repairPoints"));
        json.Append(",\"route\":");
        AppendInts(json, root.GetProperty("route"));
        json.Append(",\"leash\":").Append(Number(root.GetProperty("leash").GetDouble()));
        json.Append(",\"exposure\":").Append(Number(root.GetProperty("exposureRadius").GetDouble()));
        json.Append(",\"frames\":[");

        var frames = 0;
        var units = 0;
        var hostiles = 0;
        var topRank = 0;

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (frames++ > 0)
            {
                json.Append(',');
            }

            var frame = AppendFrame(json, line);
            units = Math.Max(units, frame.Units);
            hostiles = Math.Max(hostiles, frame.Hostiles);
            topRank = Math.Max(topRank, frame.TopRank);
        }

        json.Append("]}");
        return (json.ToString(), new Shape(units, frames, hostiles, topRank));
    }

    /// <summary>
    /// One tick as <c>[tick, units, note, anchorX, anchorY, hostiles]</c>, with
    /// the units flattened to bare numbers.
    /// </summary>
    /// <remarks>
    /// Flat because 320 ticks of six units is two thousand objects, and spelling
    /// out thirteen field names on every one of them is most of the page's
    /// weight for no information. The reader walks it by <see cref="Stride"/>,
    /// so the two constants have to agree; there is no other coupling.
    /// </remarks>
    private static (int Units, int Hostiles, int TopRank) AppendFrame(StringBuilder json, string line)
    {
        using var tick = JsonDocument.Parse(line);
        var root = tick.RootElement;

        json.Append('[').Append(root.GetProperty("tick").GetInt32()).Append(",[");

        var units = 0;
        var topRank = 0;
        foreach (var unit in root.GetProperty("units").EnumerateArray())
        {
            if (units++ > 0)
            {
                json.Append(',');
            }

            topRank = Math.Max(topRank, unit.GetProperty("rank").GetInt32());

            json.Append(unit.GetProperty("id").GetInt32()).Append(',');
            json.Append(unit.GetProperty("x").GetInt32()).Append(',');
            json.Append(unit.GetProperty("y").GetInt32()).Append(',');
            json.Append(unit.GetProperty("goalX").GetInt32()).Append(',');
            json.Append(unit.GetProperty("goalY").GetInt32()).Append(',');
            json.Append(Number(unit.GetProperty("health").GetDouble())).Append(',');
            json.Append(unit.GetProperty("rank").GetInt32()).Append(',');
            json.Append(unit.GetProperty("errandX").GetInt32()).Append(',');
            json.Append(unit.GetProperty("errandY").GetInt32()).Append(',');
            json.Append(Flag(unit, "arrived")).Append(',');
            json.Append(Flag(unit, "thinking")).Append(',');
            json.Append(Flag(unit, "waiting")).Append(',');
            json.Append(unit.GetProperty("stalled").GetInt32());
        }

        json.Append("],");

        var note = root.GetProperty("note");
        json.Append(note.ValueKind == JsonValueKind.Null ? "null" : JsonSerializer.Serialize(note.GetString()));

        json.Append(',').Append(root.GetProperty("anchorX").GetInt32());
        json.Append(',').Append(root.GetProperty("anchorY").GetInt32());
        json.Append(',');
        var hostiles = root.GetProperty("hostiles");
        AppendInts(json, hostiles);
        json.Append(']');

        return (units, hostiles.GetArrayLength(), topRank);
    }

    private static void AppendInts(StringBuilder json, JsonElement array)
    {
        json.Append('[');
        var written = 0;
        foreach (var value in array.EnumerateArray())
        {
            if (written++ > 0)
            {
                json.Append(',');
            }

            json.Append(value.GetInt32());
        }

        json.Append(']');
    }

    private static int Flag(JsonElement unit, string name) => unit.GetProperty(name).GetBoolean() ? 1 : 0;

    /// <summary>
    /// A double as the shortest round-trippable text, invariant. Culture matters
    /// here in a way it does not in most of this repo: a machine with a comma
    /// decimal separator would write 0.65 as "0,65" and split one number into
    /// two array elements, which shifts every field after it.
    /// </summary>
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
