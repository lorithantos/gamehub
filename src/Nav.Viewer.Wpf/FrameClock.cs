namespace Nav.Viewer.Wpf;

/// <summary>
/// Turns WPF's rendering timestamp into the raw per-frame delta
/// <see cref="IViewerHost"/> promises the app.
/// </summary>
/// <remarks>
/// <c>CompositionTarget.Rendering</c> hands over a <em>running timestamp</em>,
/// not a delta, and it can fire twice with the same value for one frame. Three
/// ways to get this wrong, in descending order of how convincing the wreckage
/// looks:
/// <list type="bullet">
/// <item><description>Passing the timestamp straight through advances the
/// simulation by the session's total elapsed time, every frame.</description></item>
/// <item><description>Differencing without checking for a repeat feeds a
/// spurious zero, which is harmless but makes the frame count lie.</description></item>
/// <item><description>A compositor restart can move the timestamp backwards. The
/// resulting negative delta throws inside <c>FixedTimestep.Accumulate</c>, so
/// the failure is a crash rather than drift -- better, but still a crash.</description></item>
/// </list>
/// <para>
/// Lives in the host rather than the shared project on purpose. This is
/// compositor plumbing, and moving it into the app's vocabulary to make it
/// easier to reach from the shared test suite would be widening the seam to suit
/// a host -- the exact thing the experiment is measuring.
/// </para>
/// </remarks>
internal sealed class FrameClock
{
    private TimeSpan? _previous;

    /// <returns>
    /// False when this timestamp repeats the last one, meaning the frame should
    /// be skipped entirely rather than run with a zero delta.
    /// </returns>
    public bool TryAdvance(TimeSpan renderingTime, out float deltaSeconds)
    {
        if (_previous is { } last)
        {
            if (renderingTime == last)
            {
                deltaSeconds = 0f;
                return false;
            }

            if (renderingTime < last)
            {
                // Adopt the new baseline rather than emitting a negative delta.
                _previous = renderingTime;
                deltaSeconds = 0f;
                return true;
            }

            _previous = renderingTime;
            deltaSeconds = (float)(renderingTime - last).TotalSeconds;
            return true;
        }

        _previous = renderingTime;
        deltaSeconds = 0f;
        return true;
    }

    public void Reset() => _previous = null;
}
