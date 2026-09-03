namespace Nav.Viewer.Models;

/// <summary>
/// A key on the keyboard, named for the keycap, in the only vocabulary both
/// hosts can speak.
/// </summary>
/// <remarks>
/// The other half of <see cref="ViewerKeys"/>, which is named for what a key
/// DOES. Two enums exist so that a <see cref="Keymap"/> can sit between them; a
/// host that switched straight from its own key type onto <see cref="ViewerKeys"/>
/// has hard-coded the binding and nothing can rebind it.
/// <para>
/// Not flags. One press is one key, and a set of them is a set.
/// </para>
/// <para>
/// Deliberately short: only keys the viewer has a use for appear. A host key
/// that never becomes a <see cref="ViewerKeys"/> — Escape, Ctrl+O — is the
/// host's own chrome and stays out of here entirely, because putting it here
/// would invite somebody to rebind the way out of the window.
/// </para>
/// </remarks>
public enum PhysicalKey
{
    /// <summary>Nothing. What a host reports for a key the viewer has no name for.</summary>
    None = 0,

    /// <summary>The space bar.</summary>
    Space,

    /// <summary>The R key.</summary>
    R,

    /// <summary>The S key.</summary>
    S,

    /// <summary>The T key.</summary>
    T,

    /// <summary>The V key.</summary>
    V,

    /// <summary>The P key.</summary>
    P,

    /// <summary>The L key.</summary>
    L,

    /// <summary>The left arrow.</summary>
    Left,

    /// <summary>The right arrow.</summary>
    Right,

    /// <summary>The up arrow.</summary>
    Up,

    /// <summary>The down arrow.</summary>
    Down,

    /// <summary>
    /// Plus, from wherever a host finds one -- the main row's key and the
    /// numeric keypad's are one identity here, because nobody zooming in cares
    /// which of them they hit.
    /// </summary>
    Plus,

    /// <summary>Minus, from either the main row or the keypad.</summary>
    Minus,

    /// <summary>Home.</summary>
    Home,
}
