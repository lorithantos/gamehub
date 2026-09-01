namespace Nav.Core;

/// <summary>
/// A benchmark file contradicted the format it declares.
/// </summary>
/// <remarks>
/// The line number is the point of this type. A map whose body disagrees with its
/// header is not a file you can usefully diff by eye -- 512 rows that all look
/// alike -- so the parser refuses to guess and says which line broke the contract.
/// </remarks>
public sealed class MapFormatException : Exception
{
    /// <summary>
    /// Composes the message as <c>source, line N: problem</c> and keeps the three
    /// pieces as properties, so a caller can assert on the location without
    /// picking the string back apart.
    /// </summary>
    /// <param name="source">The file being parsed, or null for text held in memory -- the message then reads "map text".</param>
    /// <param name="lineNumber">1-based, counting the four header lines. Line 5 is the first map row.</param>
    /// <param name="problem">The complaint alone, without the location prefix.</param>
    public MapFormatException(string? source, int lineNumber, string problem)
        : base($"{source ?? "map text"}, line {lineNumber}: {problem}")
    {
        SourcePath = source;
        LineNumber = lineNumber;
        Problem = problem;
    }

    /// <summary>
    /// The file the text came from, or null when parsed from a string. Named
    /// <c>SourcePath</c> rather than <c>Source</c> so it does not shadow
    /// <see cref="Exception.Source"/>, which logging infrastructure reads.
    /// </summary>
    public string? SourcePath { get; }

    /// <summary>1-based, counting the four header lines. Line 5 is the first map row.</summary>
    public int LineNumber { get; }

    /// <summary>The complaint on its own, without the location prefix.</summary>
    public string Problem { get; }
}
