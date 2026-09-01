namespace Nav.Core.Interfaces;

/// <summary>
/// What a <see cref="GroupDoctrine"/> is handed: the whole seam, as one object.
/// </summary>
/// <remarks>
/// The three facets exist so a consumer can ask for less. This composite exists
/// because the doctrine entry point has to be one parameter, and because the
/// implementation is a single coherent object -- one class serving three
/// contracts, which is the point rather than an accident.
/// <para>
/// <b>What is NOT here is the guarantee.</b> There is no plan, no reservation, no
/// search and no grid. A doctrine cannot reach the collision layer because this
/// contract does not mention it, so no doctrine -- including one written outside
/// this assembly -- can break collision-freedom. The implementing class is
/// internal, so a third party sees these contracts and no concrete type at all.
/// </para>
/// </remarks>
public interface IGroupOps : IGroupView, IGroupClaiming, IGroupPacing;
