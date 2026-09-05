using Nav.Core;
using Nav.Core.Interfaces;

namespace Nav.Viewer.Tests;

/// <summary>
/// A world whose units stand still: placed once, on the sides the test asked
/// for, and never ordered anywhere.
/// </summary>
/// <remarks>
/// Standing still is the point. What is being measured is which units reach the
/// renderer and where their marks land, and a unit that walks moves the answer
/// to both between two frames of the same test.
/// <para>
/// <b>Shared rather than nested</b>, for the reason <see cref="FakeEyes"/> is:
/// the fog tests and the health-bar tests measure marks over the same standing
/// units, and one board they agree on is worth more than two they cannot be made
/// to disagree about.
/// </para>
/// </remarks>
public sealed class StillWorld : IWorld
{
    public StillWorld(Grid grid, IReadOnlyList<(int Cell, int Side)> units)
    {
        Grid = grid;
        Board = new MovementSystem(grid);
        foreach (var (cell, side) in units)
        {
            Board.AddAgent(cell, side);
        }
    }

    public Grid Grid { get; }

    public MovementSystem Board { get; }

    public void Step(int tick) => Board.Tick();
}
