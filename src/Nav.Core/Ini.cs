using System.Globalization;

namespace Nav.Core;

/// <summary>
/// The smallest INI reader that does the job: sections, key = value, comments.
/// </summary>
/// <remarks>
/// Written rather than taken from a package because the whole need is forty
/// lines and a dependency would be larger than the thing it replaced. It is
/// deliberately dumb: no includes, no interpolation, no types beyond what a
/// caller asks for. Numbers everywhere are parsed with the INVARIANT culture,
/// which is not a detail — a machine with a comma decimal separator would read
/// <c>0.25</c> as 25 and quietly run the simulation two orders of magnitude
/// wrong.
/// <para>
/// <b>A fallback is recorded, not swallowed.</b> Missing keys do return the
/// caller's default — a partial file degrading to "as shipped" is useful — but
/// every one of them is added to <see cref="Defaulted"/>, so a caller that
/// cannot afford surprise values can look and refuse. Silent fallback is how a
/// thing ships running on numbers nobody chose, and it is the same shape as
/// every other quiet-degradation bug in this repository: a chokepoint scan that
/// answered nothing rather than failing, a viewer that drew a map at one pixel
/// per cell rather than refusing, a replay page that played a demo that no
/// longer existed. Each looked like working code.
/// </para>
/// <para>
/// So the policy is the CALLER's. <see cref="FromFile"/> refuses a missing file;
/// <see cref="FromFileOrEmpty"/> tolerates one and is the deliberate choice for
/// genuinely optional config; <see cref="Defaulted"/> tells either of them what
/// was not answered.
/// </para>
/// </remarks>
public sealed class Ini
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _defaulted = new(StringComparer.OrdinalIgnoreCase);

    private Ini()
    {
    }

    /// <summary>
    /// Every key asked for that this file did not answer, so a caller can decide
    /// whether running on compiled defaults is acceptable here.
    /// </summary>
    public IReadOnlyCollection<string> Defaulted => _defaulted;

    /// <summary>Parses INI text. Keys are stored as <c>section.key</c>.</summary>
    public static Ini Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var ini = new Ini();
        var section = string.Empty;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                continue;
            }

            var split = line.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            ini._values[section.Length == 0 ? key : section + "." + key] = value;
        }

        return ini;
    }

    /// <summary>Parses the file. Throws if it is not there.</summary>
    /// <remarks>
    /// The one to reach for by default. A config that must exist and does not is
    /// a deployment fault, and the moment to say so is now rather than after
    /// something has run on values nobody chose.
    /// </remarks>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    public static Ini FromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return File.Exists(path)
            ? Parse(File.ReadAllText(path))
            : throw new FileNotFoundException($"No configuration at '{path}'.", path);
    }

    /// <summary>Parses the file, or an empty set if it is not there.</summary>
    /// <remarks>
    /// For config that is GENUINELY optional, and a decision each time rather
    /// than a habit. Whatever it could not answer is still listed in
    /// <see cref="Defaulted"/>, so tolerating a missing file never means not
    /// knowing it was missing.
    /// </remarks>
    public static Ini FromFileOrEmpty(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : Parse(string.Empty);

    /// <summary>A number, or <paramref name="fallback"/> if absent or unreadable.</summary>
    public double Number(string section, string key, double fallback) =>
        Lookup(section, key) is { } text &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>A whole number, or <paramref name="fallback"/>.</summary>
    public int Int(string section, string key, int fallback) =>
        Lookup(section, key) is { } text &&
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>Raw text, or <paramref name="fallback"/>.</summary>
    public string Text(string section, string key, string fallback) =>
        Lookup(section, key) ?? fallback;

    /// <summary>
    /// The stored value, or null — recording the miss on the way out so a
    /// fallback is never invisible.
    /// </summary>
    private string? Lookup(string section, string key)
    {
        var full = section + "." + key;
        if (_values.TryGetValue(full, out var text))
        {
            return text;
        }

        _defaulted.Add(full);
        return null;
    }

    /// <summary>Every key in a section, without its prefix, in file-insensitive order.</summary>
    public IReadOnlyList<string> Keys(string section)
    {
        var prefix = section + ".";
        return
        [
            .. _values.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => k[prefix.Length..])
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
        ];
    }
}
