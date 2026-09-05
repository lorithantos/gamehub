namespace Nav.Viewer.Tests;

/// <summary>
/// A scripted <see cref="IVisibilityView"/>: whatever the test says each side
/// can see, remembers and has found.
/// </summary>
/// <remarks>
/// <b>It hands back a FRESH list every call</b>, which is deliberate. The app
/// is supposed to decide whether the fog it drew is still right by comparing
/// what it was told, and a fake that returned the same object would let an
/// app comparing references pass.
/// <para>
/// <b>Shared rather than nested, because more than one question is asked
/// through it.</b> It began inside the fog tests; the health bars are drawn from
/// the same visibility decision, and a second copy of this fake would be two
/// definitions of what a side can see for two tests that have to agree about it.
/// </para>
/// </remarks>
public sealed class FakeEyes(IReadOnlyList<int> sides) : IVisibilityView
{
    public Dictionary<int, List<int>> Seen { get; } = [];

    public Dictionary<int, List<int>> Pads { get; } = [];

    public Dictionary<int, List<RememberedUnit>> Memory { get; } = [];

    /// <summary>How many times the app asked what a side can see.</summary>
    public int Asked { get; private set; }

    public IReadOnlyList<int> Sides => sides;

    public IReadOnlyList<int> VisibleCells(int side)
    {
        Asked++;
        return Seen.TryGetValue(side, out var cells) ? [.. cells] : [];
    }

    public IReadOnlyList<int> RepairPoints(int side) =>
        Pads.TryGetValue(side, out var cells) ? [.. cells] : [];

    public IReadOnlyList<RememberedUnit> Remembered(int side) =>
        Memory.TryGetValue(side, out var known) ? [.. known] : [];
}
