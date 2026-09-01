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
}

/// <summary>What a demo produced: the state at the end, for the summary line.</summary>
/// <param name="Ticks">Ticks played.</param>
/// <param name="Agents">Final unit states.</param>
/// <param name="World">The world as it ended, for final health.</param>
internal sealed record Run(int Ticks, IReadOnlyList<AgentState> Agents, IPerception World);
