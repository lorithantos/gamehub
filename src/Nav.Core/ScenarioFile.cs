using System.Globalization;

namespace Nav.Core;

/// <summary>
/// Reads Moving AI <c>.scen</c> files: the published single-agent pathfinding
/// benchmark, one start/goal problem per line.
/// </summary>
/// <remarks>
/// This is the corpus format, not the replay format. A <c>.scen</c> file is a
/// list of independent problems with a published optimal cost apiece -- see
/// <see cref="ScenarioRecord"/>. The project's own multi-agent recordings, with
/// agents and timed orders, are <see cref="RecordedScenario"/> instead.
/// </remarks>
public static class ScenarioFile
{
    private const int FieldCount = 9;

    /// <summary>
    /// Reads every record in a <c>.scen</c> file, in file order.
    /// <paramref name="path"/> travels into any
    /// <see cref="MapFormatException"/>, so a malformed row is reported as file
    /// <em>and</em> line number.
    /// </summary>
    /// <param name="path">The <c>.scen</c> file to read.</param>
    /// <returns>One <see cref="ScenarioRecord"/> per non-blank row after the version line.</returns>
    /// <exception cref="MapFormatException">The version line is missing or a row is malformed.</exception>
    public static IReadOnlyList<ScenarioRecord> FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>
    /// Reads records from text already in memory -- the form tests use.
    /// Identical parsing to <see cref="FromFile"/>, except that errors carry only
    /// a line number, since there is no file to name.
    /// </summary>
    /// <param name="text">The scenario text.</param>
    /// <returns>One <see cref="ScenarioRecord"/> per non-blank row after the version line.</returns>
    /// <exception cref="MapFormatException">The version line is missing or a row is malformed.</exception>
    public static IReadOnlyList<ScenarioRecord> FromText(string text) => Parse(text, source: null);

    private static List<ScenarioRecord> Parse(string text, string? source)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].TrimEnd('\r').Trim().Length == 0)
        {
            throw new MapFormatException(source, 1, "expected a 'version' line");
        }

        RequireVersion(lines[0].TrimEnd('\r').Trim(), source);

        var records = new List<ScenarioRecord>(lines.Length - 1);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Trim().Length == 0)
            {
                continue;
            }

            records.Add(ParseRecord(line, source, lineNumber: i + 1));
        }

        return records;
    }

    /// <remarks>
    /// The version line is <c>version 1</c>, and real files in the wild also
    /// write <c>version 1.0</c>. Both mean the same format, so both are accepted;
    /// anything else is refused rather than assumed compatible.
    /// </remarks>
    private static void RequireVersion(string line, string? source)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !string.Equals(parts[0], "version", StringComparison.Ordinal) ||
            !(parts[1] is "1" or "1.0"))
        {
            throw new MapFormatException(source, 1, $"expected 'version 1', found '{line}'");
        }
    }

    private static ScenarioRecord ParseRecord(string line, string? source, int lineNumber)
    {
        // Tab-separated by the format. RemoveEmptyEntries absorbs a doubled or
        // trailing tab, which real files carry and which says nothing about the
        // data; a genuinely missing field still lands as a count mismatch below.
        var fields = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length != FieldCount)
        {
            throw new MapFormatException(
                source,
                lineNumber,
                $"expected {FieldCount} tab-separated fields, found {fields.Length}");
        }

        return new ScenarioRecord(
            lineNumber,
            Bucket: RequireInt(fields[0], "bucket", source, lineNumber),
            MapName: fields[1],
            MapWidth: RequireInt(fields[2], "mapWidth", source, lineNumber),
            MapHeight: RequireInt(fields[3], "mapHeight", source, lineNumber),
            StartX: RequireInt(fields[4], "startX", source, lineNumber),
            StartY: RequireInt(fields[5], "startY", source, lineNumber),
            GoalX: RequireInt(fields[6], "goalX", source, lineNumber),
            GoalY: RequireInt(fields[7], "goalY", source, lineNumber),
            OptimalLength: RequireDouble(fields[8], "optimalLength", source, lineNumber));
    }

    private static int RequireInt(string value, string field, string? source, int lineNumber)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new MapFormatException(source, lineNumber, $"'{field}' is not an integer: '{value}'");
        }

        return parsed;
    }

    /// <remarks>
    /// InvariantCulture is not optional here. On a machine whose culture uses a
    /// comma decimal separator, <c>double.Parse("3.00000000")</c> reads three
    /// hundred million.
    /// <para>
    /// It does not throw, so every expected value is silently corrupted and every
    /// oracle comparison fails for a reason nowhere near the pathfinder.
    /// </para>
    /// </remarks>
    private static double RequireDouble(string value, string field, string? source, int lineNumber)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new MapFormatException(source, lineNumber, $"'{field}' is not a number: '{value}'");
        }

        return parsed;
    }
}
