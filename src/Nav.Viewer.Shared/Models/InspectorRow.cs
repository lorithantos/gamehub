namespace Nav.Viewer.Models;

/// <summary>
/// One fact about the watched unit, as a line a host can lay out.
/// </summary>
/// <param name="Group">
/// Which heading the row belongs under. Rows arrive already in group order, so a
/// host renders headings by watching this change rather than by sorting.
/// </param>
/// <param name="Label">What the fact is called.</param>
/// <param name="Value">The fact, already formatted -- see the remarks.</param>
/// <remarks>
/// Three strings and no types, because the alternative is the app exporting a
/// shape per field and every host learning to render each one. A cell is
/// "col,row" here and not a pair, for the same reason the status line is a
/// string: the app decides what it says, the host decides how it looks.
/// </remarks>
public readonly record struct InspectorRow(string Group, string Label, string Value);
