namespace Nav.Tactics.Interfaces;

/// <summary>
/// What a <see cref="SquadDoctrine"/> is handed: the squad's reads and its moves,
/// as one object.
/// </summary>
/// <remarks>
/// The two facets exist so a consumer can ask for less — a display or a metric
/// takes <see cref="ISquadView"/> and is structurally unable to move anything.
/// <para>
/// What is NOT here is the guarantee: no plan, no reservation, no ring, no map.
/// A squad doctrine cannot reach the movement layer's internals because this
/// contract does not mention them.
/// </para>
/// </remarks>
public interface ISquadOps : ISquadView, ISquadMovement;
