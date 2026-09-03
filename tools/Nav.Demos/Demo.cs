namespace Nav.Demos;

/// <summary>
/// One behaviour, played out and recorded: what it is called, what it is meant
/// to show, and how to run it.
/// </summary>
/// <remarks>
/// A demo is not a test. A test asserts and says nothing when it passes; a demo
/// narrates, because the point is that somebody watches it and sees the
/// doctrine deciding. The narration is the <c>note</c> on a tick.
/// </remarks>
internal abstract class Demo
{
    /// <summary>File-safe short name; also the trace's file name.</summary>
    public abstract string Name { get; }

    /// <summary>One line on what this shows.</summary>
    public abstract string Description { get; }

    /// <summary>How many ticks to play.</summary>
    public virtual int Ticks => 400;

    /// <summary>Builds the world, then plays it, writing a line per tick.</summary>
    public abstract Run Play(TextWriter trace);

    /// <summary>
    /// A file under the repository's <c>config/</c>, found by walking up from
    /// the binary, so a demo run from the root and one run from bin read the
    /// same numbers. Throws rather than falling back: a demo on numbers nobody
    /// chose is the failure the config files exist to prevent.
    /// </summary>
    /// <exception cref="FileNotFoundException">No <c>config/</c> holding the file above the binary.</exception>
    protected static string ConfigPath(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config", file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"No config/{file} above {AppContext.BaseDirectory}.", file);
    }
}

/// <summary>What a demo produced: the state at the end, and what it amounted to.</summary>
/// <param name="Ticks">Ticks played.</param>
/// <param name="Agents">Final unit states.</param>
/// <param name="World">The world as it ended, for final health.</param>
/// <param name="Headline">
/// What happened, in the demo's own terms. Each demo writes its own because the
/// generic counters mislead: a patrol that is still walking at the end reports
/// nobody "in place", which is the correct outcome and reads like a failure.
/// </param>
internal sealed record Run(int Ticks, IReadOnlyList<AgentState> Agents, IPerception World, string Headline);
