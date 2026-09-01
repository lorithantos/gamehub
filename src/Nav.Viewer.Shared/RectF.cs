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
    /// <summary>
    /// <c>X + Width</c>, derived rather than stored, in the same physical pixels
    /// <see cref="GridLayout"/> works in -- so it is directly comparable with a
    /// mouse position out of <see cref="InputState.MousePosition"/>.
    /// </summary>
    public float Right => X + Width;

    /// <summary>
    /// <c>Y + Height</c>, the twin of <see cref="Right"/>. The pair exists so a
    /// hit test or a quad's four corners read as an expression instead of the
    /// same addition repeated at every call site.
    /// </summary>
    public float Bottom => Y + Height;
}
