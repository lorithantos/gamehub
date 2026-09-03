namespace Nav.Tactics.Models;

/// <summary>A unit that reached zero health, and who was resolving damage when it did.</summary>
/// <param name="Victim">Who fell.</param>
/// <param name="Killer">Who landed the blow that did it. Last hit, as the credit rule says.</param>
public readonly record struct Casualty(int Victim, int Killer);
