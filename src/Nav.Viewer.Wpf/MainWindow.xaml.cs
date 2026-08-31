using System.Windows;

namespace Nav.Viewer.Wpf;

/// <summary>
/// Layout only. Every decision about what to draw and when lives in
/// <see cref="WpfHost"/> and, above it, in the shared <c>ViewerApp</c>.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
