namespace Nav.Core.Interfaces;

/// <summary>
/// What a <see cref="GroupDoctrine"/> is handed: the whole seam, as one object.
/// </summary>
/// <remarks>
/// The three facets exist so a consumer can ask for less. This composite exists
/// because the doctrine entry point has to be one parameter.
/// <para>
/// <b>What is NOT here is the guarantee.</b> No plan, no reservation, no search,
/// no grid.
/// </para>
/// <para>
/// A doctrine cannot reach the collision layer because this contract does not
/// mention it — so no doctrine, including one written outside this assembly, can
/// break collision-freedom.
/// </para>
/// </remarks>
public interface IGroupOps : IGroupView, IGroupClaiming, IGroupPacing;
