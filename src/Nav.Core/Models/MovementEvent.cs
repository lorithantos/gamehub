namespace Nav.Core.Models;

/// <summary>What kind of thing happened to an agent.</summary>
public enum MovementEventKind
{
    /// <summary>Placed on the map by <see cref="MovementSystem.AddAgent"/>.</summary>
    Added,

    /// <summary>Stepped from one cell to another during a <see cref="MovementSystem.Tick"/>.</summary>
    Moved,

    /// <summary>Taken out of the world by <see cref="MovementSystem.Remove"/>.</summary>
    Removed,
}

/// <summary>
/// One thing that happened to one agent, as the movement system broadcasts it.
/// </summary>
/// <param name="Tick">The system's clock when it happened.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Agent">To whom.</param>
/// <param name="Cell">Where it is now: the cell placed on, stepped onto, or last stood on.</param>
/// <param name="From">The cell stepped off, for a move; -1 otherwise.</param>
/// <param name="Side">Whose unit it is. Carried on every event so a listener need not remember placements.</param>
/// <remarks>
/// Raised through <see cref="MovementSystem.Happened"/>: how a layer above
/// learns where things are without reaching into the system or keeping a copy
/// of its state. Every listener hears the same things in the same order. Fog
/// of war is a listener that passes on only what a side could have witnessed.
/// </remarks>
public readonly record struct MovementEvent(int Tick, MovementEventKind Kind, int Agent, int Cell, int From = -1, int Side = 0);
