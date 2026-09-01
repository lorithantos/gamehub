namespace Nav.Tactics.Interfaces;

/// <summary>
/// The moves a squad may make: everyone, one member away, one member back.
/// </summary>
/// <remarks>
/// Each verb is confined to the squad's own members and refuses any other id
/// before anything changes, the same rule the movement seam applies to its
/// formation. A verb takes effect at the next tick; reads on
/// <see cref="ISquadView"/> keep answering for the tick the pass began in.
/// </remarks>
public interface ISquadMovement
{
    /// <summary>
    /// The group move: every member, detached ones included, is ordered to
    /// <paramref name="destination"/> as one formation, and the squad's
    /// <see cref="ISquadView.Anchor"/> becomes that cell. Any errand ends here,
    /// which is what a group move means.
    /// </summary>
    void MoveAll(int destination);

    /// <summary>
    /// Sends one member to <paramref name="destination"/> on its own. It stays a
    /// member, its place in the formation is kept, and it reads as
    /// <see cref="ISquadView.Away"/> until it is brought back or the squad moves.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="id"/> is not a member, or the destination is off the map
    /// or impassable.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The squad has never been moved as a group, so there is no formation for
    /// the member to leave.
    /// </exception>
    void Detach(int id, int destination);

    /// <summary>
    /// Brings a member back to its formation. A no-op on one that is not away.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not a member.</exception>
    void Rejoin(int id);
}
