namespace Nav.Core;

/// <summary>
/// Turns variable frame times into a whole number of fixed steps.
/// </summary>
/// <remarks>
/// Lives in the core rather than the viewer so that the thing the brief asks to
/// be reproducible can be tested without a window open.
/// <para>
/// <see cref="MaxStepsPerFrame"/> is a circuit breaker. After a stall -- a
/// breakpoint, a swapped-out process, a laptop lid -- the accumulator holds
/// several seconds and would ask for hundreds of steps, each of which takes
/// longer than the frame it is trying to catch up on. That is the spiral of
/// death, and its symptom is an application that appears to hang. Dropping the
/// surplus makes the simulation lose time it can never have caught up on anyway.
/// </para>
/// </remarks>
public sealed class FixedTimestep(double step = 1.0 / 60.0, int maxStepsPerFrame = 8)
{
    private double _accumulated;

    /// <summary>
    /// Seconds of simulated time one step stands for. Fixed for the object's life:
    /// a step that varied would make the run depend on the frame rate, which is the
    /// whole thing this type exists to prevent.
    /// </summary>
    public double Step { get; } = step > 0.0
        ? step
        : throw new ArgumentOutOfRangeException(nameof(step), step, "The step must be positive.");

    /// <summary>
    /// The most steps one <see cref="Accumulate"/> call will ever ask for. On the
    /// frame that hits it the ENTIRE bank is discarded, not merely trimmed: the
    /// simulation gives up that time for good rather than chasing it forever.
    /// </summary>
    public int MaxStepsPerFrame { get; } = maxStepsPerFrame > 0
        ? maxStepsPerFrame
        : throw new ArgumentOutOfRangeException(nameof(maxStepsPerFrame), maxStepsPerFrame, "There must be at least one step.");

    /// <summary>Seconds banked but not yet worth a whole step.</summary>
    public double Pending => _accumulated;

    /// <summary>
    /// Banks <paramref name="deltaSeconds"/> and reports how many fixed steps are
    /// now due, discarding any beyond <see cref="MaxStepsPerFrame"/>.
    /// </summary>
    public int Accumulate(double deltaSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deltaSeconds);

        _accumulated += deltaSeconds;

        var steps = (int)(_accumulated / Step);
        if (steps <= 0)
        {
            return 0;
        }

        if (steps > MaxStepsPerFrame)
        {
            _accumulated = 0.0;
            return MaxStepsPerFrame;
        }

        _accumulated -= steps * Step;
        return steps;
    }

    /// <summary>
    /// Drops what was banked without ever emitting it, so a pause or a reload is not
    /// paid back as a burst of steps on the frame that follows it.
    /// </summary>
    public void Reset() => _accumulated = 0.0;
}
