namespace Nav.Tactics.Interfaces;

/// <summary>
/// What a squad can perceive of the world beyond movement: who is hurt, where
/// the enemy is, where repair can be had.
/// </summary>
/// <remarks>
/// Fed by the game and faked in tests, which is the whole reason it is an
/// interface. Nothing in the movement layer knows any of this exists, and no
/// squad doctrine learns it any other way, so a guard that retreats at a health
/// threshold is testable against a scripted world before a single point of
/// damage has been modelled anywhere.
/// <para>
/// Answers are for the tick they are asked in. Determinism binds here as it does
/// everywhere below: the same tick asked twice must answer the same, or replay
/// stops being a test.
/// </para>
/// </remarks>
public interface IPerception
{
    /// <summary>
    /// An agent's health as a fraction, 1 for undamaged and 0 for destroyed.
    /// An agent the perception knows nothing about is 1.
    /// </summary>
    double HealthOf(int agent);

    /// <summary>
    /// An agent's rank: 0 for one that has not proved anything, rising with
    /// what it has survived. An agent the perception knows nothing about is 0.
    /// </summary>
    /// <remarks>
    /// It sits beside health because it is the same kind of fact -- a property
    /// of the unit that the world keeps and a doctrine only reads. WHERE rank
    /// comes from is the world's business: <see cref="DemoWorld"/> earns it from
    /// time spent within reach of <see cref="Hostiles"/>, a game would have its
    /// own rule, and a world with no notion of veterancy answers 0 for
    /// everybody, which every doctrine here treats as the ordinary case.
    /// Deliberately NOT a default implementation: a world that models damage and
    /// forgets rank should have to say so rather than inherit a silent zero.
    /// </remarks>
    int RankOf(int agent);

    /// <summary>Cells hostile units currently occupy, ascending. Empty for a quiet map.</summary>
    IReadOnlyList<int> Hostiles { get; }

    /// <summary>Cells where a unit standing there is repaired, ascending.</summary>
    IReadOnlyList<int> RepairPoints { get; }
}
