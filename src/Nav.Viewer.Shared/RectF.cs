namespace Nav.Viewer;

/// <summary>
/// An axis-aligned rectangle in pixels.
/// </summary>
/// <remarks>
/// The viewer's own, rather than <c>System.Drawing.RectangleF</c> or either
/// renderer's: this type crosses the seam, and the ones that ship with a
/// graphics stack drag that stack's assembly along with them.
/// </remarks>
public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;
}
