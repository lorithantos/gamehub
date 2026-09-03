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
/// <remarks>
/// The journal these make up is how a layer above learns where things are
/// without reaching into the system or keeping a copy of its state. A reader
/// keeps a cursor and reads what is new, in the order it happened, and two
/// readers of the same journal agree on everything. Fog of war is the same
/// stream with the events a side could not have witnessed left out.
/// </remarks>
public readonly record struct MovementEvent(int Tick, MovementEventKind Kind, int Agent, int Cell, int From = -1);
