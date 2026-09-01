using System.Windows;

namespace Nav.Viewer.Wpf;

/// <summary>
/// Layout only. Every decision about what to draw and when lives in
/// <see cref="WpfHost"/> and, above it, in the shared <c>ViewerApp</c>.
/// </summary>
internal partial class MainWindow : Window
{
    /// <summary>
    /// Realises the two named elements the host drives: the nearest-neighbour
    /// <c>Surface</c> image the Direct3D back buffer is attached to, and the
    /// <c>Status</c> line under it. The window opens auto-sizing to that content;
    /// <see cref="WpfHost"/> locks the sizing once the first layout has run.
    /// </summary>
    public MainWindow() => InitializeComponent();
}
