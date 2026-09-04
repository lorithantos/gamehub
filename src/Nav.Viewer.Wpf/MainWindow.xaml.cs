using System.Windows;

namespace Nav.Viewer.Wpf;

/// <summary>
/// Layout only. Every decision about what to draw and when lives in
/// <see cref="WpfHost"/> and, above it, in the shared <c>ViewerApp</c>.
/// </summary>
internal partial class MainWindow : Window
{
    /// <summary>
    /// Realises the named elements the host drives: the nearest-neighbour
    /// <c>Surface</c> image the Direct3D back buffer is attached to, the
    /// <c>Status</c> line under it, and the empty <c>Inspector</c> stack beside
    /// it that <see cref="InspectorView"/> fills. The window opens auto-sizing
    /// to that content; <see cref="WpfHost"/> locks the sizing once the first
    /// layout has run, and sizes the surface and the two chrome elements from
    /// the map.
    /// </summary>
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// Sizes the three elements the map decides: the surface, the status line
    /// under it and the inspector beside it.
    /// </summary>
    /// <remarks>
    /// <b>Here rather than in the host so it can be driven without a device.</b>
    /// <see cref="WpfHost"/> cannot be stood up in a test -- it creates a
    /// Direct3D 11 device in its first event -- and sizing arithmetic a test has
    /// to restate is arithmetic the test is no longer checking. This window can
    /// be built on any STA thread, so the rule lives where it can be asked.
    /// <para>
    /// <b>The status line is measured across the WHOLE window, map plus panel,
    /// and takes the panel's width from the panel.</b> It sat at the map's width
    /// for as long as there was nothing beside it; a constant repeating the
    /// panel's 260 here would be right until the day somebody widened the panel
    /// and clipped the line again without touching this file.
    /// </para>
    /// <para>
    /// The panel's HEIGHT comes the other way, from the surface, so the map
    /// alone decides how tall the window is -- see the remarks on
    /// <c>WpfHost.SizeChrome</c> for why both ends of the panel are pinned.
    /// </para>
    /// </remarks>
    /// <param name="layout">The map's pixel geometry.</param>
    /// <param name="dpiScale">
    /// Physical pixels per device-independent point. Everything the host holds
    /// is in physical pixels and WPF lays out in points, and this is the only
    /// place the two meet.
    /// </param>
    internal void SizeTo(GridLayout layout, double dpiScale)
    {
        Surface.Width = layout.PixelWidth / dpiScale;
        Surface.Height = layout.PixelHeight / dpiScale;
        InspectorPanel.Height = layout.PixelHeight / dpiScale;
        StatusBar.Width = Surface.Width + InspectorPanel.Width;
    }
}
