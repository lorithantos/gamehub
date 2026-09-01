namespace Nav.Core.Models;

/// <param name="Cell">Where the map forces paths together.</param>
/// <param name="Width">The passage's width in cells — its capacity for metering.</param>
public sealed record Chokepoint(int Cell, int Width);
