using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nav.Viewer.Wpf;

/// <summary>
/// The inspector panel as real controls: a foldable heading per group, a row per
/// fact, and the row's note on hover.
/// </summary>
/// <remarks>
/// <b>What this replaced and why.</b> The panel used to be one monospace
/// <c>TextBlock</c> rebuilt into a string every frame, with the key padded to a
/// common width and the value written after it. That was the right shape while a
/// row was one line of text, and it stopped being right the moment a row had two
/// things to show: the column was fixed at 260px and the block did not wrap or
/// trim, so every value longer than the column was hard-clipped mid-word with
/// nothing on screen saying so.
/// <para>
/// <b>Rebuilt on SHAPE, updated on value.</b> The elements are built once for a
/// given set of groups and keys and then only their text is written. Two reasons,
/// and the second is the one that bites: laying out thirty elements sixty times a
/// second is work nobody asked for, and a rebuilt tree loses which groups the
/// reader folded -- so a panel that rebuilt each frame would present as folders
/// that will not stay shut. It is the same discipline the fog overlay keeps,
/// where <c>TerrainImage</c> is rebuilt only when the visible cell set actually
/// changes rather than every time it is drawn.
/// </para>
/// <para>
/// <b>The fold state belongs here.</b> It is a fact about this window and about
/// nothing else -- not about the simulation, not about the app -- so it lives in
/// the host beside the elements it hides, and no <c>[Observes]</c> member can
/// reach it. It also survives a rebuild, keyed by group name: a group that comes
/// back after a shape change comes back folded if the reader folded it.
/// </para>
/// </remarks>
/// <param name="host">
/// The panel to fill. Its children are owned entirely by this view.
/// </param>
internal sealed class InspectorView(Panel host)
{
    /// <summary>Joins a group and a key into one comparable shape entry.</summary>
    /// <remarks>
    /// A unit separator rather than a space or a colon, because a source names
    /// its own groups and keys and either of those could occur inside one --
    /// which would let "Plan" plus "next" collide with "Plan next" plus "".
    /// </remarks>
    private const char Separator = (char)0x1F;

    private static readonly SolidColorBrush HeadingBrush = Frozen(0xA0, 0xC8, 0xFF);
    private static readonly SolidColorBrush KeyBrush = Frozen(0x9A, 0x9A, 0x9A);
    private static readonly SolidColorBrush ValueBrush = Frozen(0xF5, 0xF5, 0xF5);

    /// <summary>Folded groups, by group name. Absent means expanded.</summary>
    /// <remarks>
    /// Expanded is the default because expanded is what the panel has always
    /// done. Folding is the reader's tool, not a state they have to discover
    /// their way out of.
    /// </remarks>
    private readonly Dictionary<string, bool> _folded = new(StringComparer.Ordinal);

    private readonly List<Group> _groups = [];
    private readonly List<Row> _rows = [];

    private string[] _shape = [];

    /// <summary>How many times the element tree has been built from scratch.</summary>
    /// <remarks>
    /// Kept so the rebuild rule is a property a test can assert rather than a
    /// claim in this comment. A frame that only changed values must not move it.
    /// </remarks>
    internal int Rebuilds { get; private set; }

    /// <summary>Whether <paramref name="group"/> is currently folded shut.</summary>
    internal bool IsFolded(string group) => _folded.TryGetValue(group, out var shut) && shut;

    /// <summary>Folds or unfolds a group, as a click on its heading would.</summary>
    internal void Fold(string group, bool shut)
    {
        _folded[group] = shut;
        Apply(group);
    }

    /// <summary>Shows <paramref name="rows"/>, building elements only if the shape changed.</summary>
    /// <param name="rows">
    /// The panel's rows, already in group order. Rows are read and never held.
    /// </param>
    internal void Update(IReadOnlyList<DebugRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (!SameShape(rows))
        {
            Rebuild(rows);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            _rows[i].Write(rows[i]);
        }
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Whether the built elements still describe these rows: the same groups and
    /// keys, in the same order.
    /// </summary>
    /// <remarks>
    /// GROUP AND KEY, NOT VALUE. Value is what changes every frame and note is
    /// what changes with it; neither adds or removes an element, so neither may
    /// force a rebuild. The unit separator cannot occur in either half, so a key
    /// containing whatever a source likes cannot collide with a group name.
    /// </remarks>
    private bool SameShape(IReadOnlyList<DebugRow> rows)
    {
        if (_shape.Length != rows.Count)
        {
            return false;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (!string.Equals(_shape[i], Shape(rows[i]), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Shape(DebugRow row) => $"{row.Group}{Separator}{row.Key}";

    private void Rebuild(IReadOnlyList<DebugRow> rows)
    {
        Rebuilds++;
        host.Children.Clear();
        _groups.Clear();
        _rows.Clear();

        var shape = new string[rows.Count];
        Group? current = null;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            shape[i] = Shape(row);

            if (current is null || !string.Equals(current.Name, row.Group, StringComparison.Ordinal))
            {
                current = NewGroup(row.Group);
                _groups.Add(current);
            }

            var built = NewRow();
            current.Body.Children.Add(built.Element);
            current.Count++;
            _rows.Add(built);
        }

        _shape = shape;

        foreach (var group in _groups)
        {
            // The tooltip counts rows, so it is written once the group is whole.
            // It says nothing about what the group MEANS: group names come from
            // sources this host has never heard of, and a heading that explained
            // "Fight" would be a host inventing a claim about somebody else's
            // rows.
            group.Heading.ToolTip = group.Count == 1
                ? "1 row -- click to fold"
                : $"{group.Count} rows -- click to fold";

            Apply(group.Name);
        }
    }

    private Group NewGroup(string name)
    {
        var chevron = new TextBlock { Foreground = HeadingBrush, Margin = new Thickness(0, 0, 4, 0) };
        var caption = new TextBlock { Foreground = HeadingBrush, Text = name.ToUpperInvariant() };

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(chevron);
        line.Children.Add(caption);

        // A Border rather than a bare panel: Background must be set for the whole
        // strip to be hit-testable, so the click target is the heading line and
        // not just the glyphs on it.
        var heading = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Padding = new Thickness(0, 6, 0, 2),
            Child = line,
        };

        var body = new StackPanel();
        var group = new Group(name, heading, chevron, body);

        heading.MouseLeftButtonDown += (_, e) =>
        {
            Fold(name, !IsFolded(name));
            e.Handled = true;
        };

        host.Children.Add(heading);
        host.Children.Add(body);
        return group;
    }

    private static Row NewRow()
    {
        var key = new TextBlock { Foreground = KeyBrush, Margin = new Thickness(0, 0, 8, 0) };
        var value = new TextBlock { Foreground = ValueBrush, TextWrapping = TextWrapping.Wrap };

        // Wrapping, not trimming. A trimmed value hides its own tail behind an
        // ellipsis the reader cannot open; a wrapped one costs a second line on
        // the few rows that need it and never hides anything.
        var element = new Grid();
        element.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "InspectorKey",
        });
        element.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(key, 0);
        Grid.SetColumn(value, 1);
        element.Children.Add(key);
        element.Children.Add(value);

        return new Row(element, key, value);
    }

    /// <summary>Puts a group's body where its fold state says it should be.</summary>
    private void Apply(string name)
    {
        var shut = IsFolded(name);
        foreach (var group in _groups)
        {
            if (string.Equals(group.Name, name, StringComparison.Ordinal))
            {
                group.Body.Visibility = shut ? Visibility.Collapsed : Visibility.Visible;
                group.Chevron.Text = shut ? "▸" : "▾";
            }
        }
    }

    /// <summary>One heading and the rows under it.</summary>
    private sealed class Group(string name, Border heading, TextBlock chevron, StackPanel body)
    {
        public string Name { get; } = name;

        public Border Heading { get; } = heading;

        public TextBlock Chevron { get; } = chevron;

        public StackPanel Body { get; } = body;

        public int Count { get; set; }
    }

    /// <summary>One row's elements, and the last thing written into them.</summary>
    /// <remarks>
    /// Every write is guarded by an equality check. An unconditional assignment
    /// invalidates measure and arrange for the row, and most rows say the same
    /// thing this frame as they did last -- which is the whole reason the string
    /// version was guarded too.
    /// </remarks>
    private sealed class Row(Grid element, TextBlock key, TextBlock value)
    {
        private string? _note;

        public Grid Element { get; } = element;

        public TextBlock Key { get; } = key;

        public TextBlock Value { get; } = value;

        public void Write(DebugRow row)
        {
            if (!string.Equals(Key.Text, row.Key, StringComparison.Ordinal))
            {
                Key.Text = row.Key;
            }

            if (!string.Equals(Value.Text, row.Value, StringComparison.Ordinal))
            {
                Value.Text = row.Value;
            }

            if (string.Equals(_note, row.Note, StringComparison.Ordinal))
            {
                return;
            }

            _note = row.Note;

            // NULL, NOT EMPTY. A tooltip set to "" still pops up -- an empty
            // grey box over the panel on every hover, which is worse than the
            // row simply having nothing to add.
            Element.ToolTip = row.Note;
        }
    }
}
