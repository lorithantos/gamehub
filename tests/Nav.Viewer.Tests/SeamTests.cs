using System.Reflection;

namespace Nav.Viewer.Tests;

/// <summary>
/// The seam, asserted against the built article rather than against the project
/// file: nothing the viewer is compiled from can name a kit, a sighting or a
/// squad.
/// </summary>
/// <remarks>
/// <b>What this proves and what it does not.</b> It reads the assembly
/// references the compiler actually emitted, walked transitively, so it fails
/// the moment anything the viewer is built on USES a tactics or worlds type --
/// including through a dependency, which is the leak nobody would notice by
/// reading one project file. What it cannot see is an unused reference: the
/// compiler emits no entry for an assembly no type was taken from, so a
/// <c>ProjectReference</c> added and not yet used passes here and is caught only
/// by the comment in <c>Nav.Viewer.Shared.csproj</c> and by whoever is reading
/// the diff. That is the honest limit of the check, and it is still the right
/// check: a reference that is never used has not leaked anything yet, and the
/// first line of code that uses it turns this red.
/// <para>
/// The test project is walked too, and for the same reason its csproj gives:
/// what is being measured is a viewer driven with no tactics layer anywhere in
/// the process.
/// </para>
/// </remarks>
public sealed class SeamTests
{
    /// <summary>What may not be on the far side of the seam.</summary>
    private static readonly string[] Forbidden = ["Nav.Tactics", "Nav.Worlds", "Nav.Viewer.Tactics"];

    [Fact]
    public void TheViewerIsBuiltOnNothingThatCanNameAKitOrASquad()
    {
        var reached = Closure(typeof(ViewerApp).Assembly);

        // The walk found something, so an empty answer below would be the walk
        // failing rather than the seam holding.
        Assert.Contains("Nav.Core", reached);

        Assert.Empty(reached.Intersect(Forbidden, StringComparer.Ordinal));
    }

    [Fact]
    public void NorIsThisTestProject()
    {
        var reached = Closure(typeof(SeamTests).Assembly);

        Assert.Contains("Nav.Viewer.Shared", reached);
        Assert.Empty(reached.Intersect(Forbidden, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every assembly reachable from <paramref name="root"/> by reference, by
    /// name.
    /// </summary>
    /// <remarks>
    /// One that cannot be loaded is counted by name and not followed. A framework
    /// assembly resolving differently under a test host is not what this is
    /// looking for, and refusing to answer at all because of one would make the
    /// check fragile in exactly the way that gets a test deleted.
    /// </remarks>
    private static HashSet<string> Closure(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>();
        pending.Enqueue(root);

        while (pending.TryDequeue(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (!seen.Add(reference.Name!))
                {
                    continue;
                }

                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch (Exception e) when (e is FileNotFoundException or BadImageFormatException)
                {
                    // Named, counted, not followed.
                }
            }
        }

        return seen;
    }
}
