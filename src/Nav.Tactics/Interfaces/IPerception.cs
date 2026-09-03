namespace Nav.Tactics.Interfaces;

/// <summary>
/// What a squad can perceive of the world beyond movement: who is hurt, where
/// the enemy is, where repair can be had.
/// </summary>
/// <remarks>
/// Fed by the game and faked in tests, which is the whole reason it is an
/// interface. Nothing in the movement layer knows any of this exists, and no
/// squad doctrine learns it any other way.
/// <para>
/// So a guard that retreats at a health threshold is testable against a scripted
/// world before a single point of damage has been modelled anywhere.
/// </para>
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
    /// Beside health because it is the same kind of fact: a property of the unit
    /// that the world keeps and a doctrine only reads.
    /// <para>
    /// WHERE rank comes from is the world's business. A world with no notion of
    /// veterancy answers 0 for everybody, which every doctrine here treats as
    /// the ordinary case.
    /// </para>
    /// <para>
    /// Deliberately NOT a default implementation: a world that models damage and
    /// forgets rank should have to say so rather than inherit a silent zero.
    /// </para>
    /// </remarks>
    int RankOf(int agent);

    /// <summary>
    /// Cells hostile units currently occupy, ascending. Empty for a quiet map.
    /// </summary>
    /// <remarks>
    /// Under fog this is what this side can SEE this tick, and nothing else. An
    /// enemy that has walked out of sight leaves here the moment it does, and is
    /// remembered in <see cref="Sightings"/> instead.
    /// </remarks>
    IReadOnlyList<int> Hostiles { get; }

    /// <summary>
    /// What this side knows about enemy units it has seen: where each was last
    /// seen and when, by agent, ascending. Empty for a world without fog.
    /// </summary>
    /// <remarks>
    /// The pair to <see cref="Hostiles"/>, and the split matters. Hostiles is
    /// what I can see; this is what I know, which is a larger and older set. A
    /// doctrine that reads only Hostiles behaves exactly as it did before fog
    /// existed — it forgets an enemy the instant it loses sight of it — and one
    /// that reads this can chase, hold a line against, or be baited by something
    /// it can no longer see.
    /// <para>
    /// Empty is the honest answer for a world that does not model fog: such a
    /// world sees everything, so everything it knows is already in
    /// <see cref="Hostiles"/> and there is nothing left to remember.
    /// </para>
    /// <para>
    /// A sighting is dropped when the cell it names is in plain view and the
    /// unit is not on it: looking straight at where something was and finding it
    /// gone is knowledge too, and without that rule every ghost would be
    /// permanent.
    /// </para>
    /// </remarks>
    IReadOnlyList<Sighting> Sightings => [];

    /// <summary>Cells where a unit standing there is repaired, ascending.</summary>
    IReadOnlyList<int> RepairPoints { get; }
}
