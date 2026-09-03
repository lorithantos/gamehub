namespace Nav.InstrumentAudit;

/// <summary>
/// Where the thing being mutated came from, as far as the walk could see it.
/// </summary>
/// <remarks>
/// The whole question the walk exists to answer. Mutating something you made a
/// line ago is what every instrument does dozens of times; mutating something
/// the run owns is the bug. Everything except <see cref="Owned"/> is a claim the
/// walk is making with partial information, which is why the origin travels with
/// the finding instead of being collapsed into a yes or a no.
/// </remarks>
public enum Origin
{
    /// <summary>A <c>newobj</c> in the same method, or a local holding one.</summary>
    Owned,

    /// <summary>The receiver the method was called on.</summary>
    This,

    /// <summary>Loaded out of an instance field.</summary>
    Field,

    /// <summary>Loaded out of a static field.</summary>
    StaticField,

    /// <summary>A parameter. Who owns it is the caller's business, not visible from here.</summary>
    Argument,

    /// <summary>Out of an array or an indexer.</summary>
    Element,

    /// <summary>A return value, an out parameter, or a merge the walk gave up on.</summary>
    Unknown,
}

/// <summary>One state change the walk could reach from a marked instrument.</summary>
/// <param name="Root">The instrument the walk started at.</param>
/// <param name="Site">The method the mutation is written in.</param>
/// <param name="What">The field assigned, or the mutating member called.</param>
/// <param name="Origin">Where the mutated thing came from.</param>
/// <param name="Path">Calls from the root to the site.</param>
/// <param name="Suppressed">Why this is not counted, or null if it is.</param>
public sealed record Mutation(
    string Root,
    string Site,
    string What,
    Origin Origin,
    string Path,
    string? Suppressed)
{
    /// <summary>One line, origin first, because that is what a reader is judging.</summary>
    public override string ToString() => Suppressed is null
        ? $"  [{Origin,-11}] {What}  in {Site}\n                 via {Path}"
        : $"  [suppressed ] {What}  in {Site}  ({Suppressed})";
}
