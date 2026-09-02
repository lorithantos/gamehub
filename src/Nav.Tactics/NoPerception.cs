namespace Nav.Tactics;

/// <summary>
/// A quiet world: nobody hurt, nobody hostile, nowhere to repair.
/// </summary>
/// <remarks>
/// The default a squad advances against when the caller has no perception to
/// offer, and the thing to hand a doctrine under test when only its movement
/// matters. A guard given this never retreats, which is correct: it has nothing
/// to retreat from.
/// </remarks>
public sealed class NoPerception : IPerception
{
    /// <summary>The one instance; it holds no state.</summary>
    public static NoPerception Instance { get; } = new();

    private NoPerception()
    {
    }

    /// <inheritdoc/>
    public double HealthOf(int agent) => 1.0;

    /// <inheritdoc/>
    /// <remarks>Nobody has been under fire, so nobody outranks anybody.</remarks>
    public int RankOf(int agent) => 0;

    /// <inheritdoc/>
    public IReadOnlyList<int> Hostiles => [];

    /// <inheritdoc/>
    public IReadOnlyList<int> RepairPoints => [];
}
