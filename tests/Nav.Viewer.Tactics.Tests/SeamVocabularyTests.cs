using System.Reflection;
using System.Reflection.Emit;
using System.Text;

using Nav.Viewer.Models;
using Nav.Viewer.Tactics;

namespace Nav.Viewer.Tactics.Tests;

/// <summary>
/// The other half of the seam: what <c>Nav.Viewer.Shared</c> is allowed to SAY,
/// as opposed to what it is allowed to reference.
/// </summary>
/// <remarks>
/// <b>WHY THIS EXISTS.</b> Three guards stand on the seam and every one of them
/// is type-aware: the compiler, through the project reference
/// <c>Nav.Viewer.Shared</c> does not have; the reference-closure walk in
/// <c>Nav.Viewer.Tests.SeamTests</c>; and the code graph. A quoted word carries
/// no type, so all three passed <c>52b7dbc</c>, which put this layer's entire
/// vocabulary -- <c>{ "Squad", "Condition", "Kit", "Fight", "Perception",
/// "World", "Rates", "Rank" }</c> -- into an array in <c>InspectorLayout</c>. It
/// was found by a hand-run grep. This is that grep, made a build failure.
/// <para>
/// <b>HERE AND NOT IN Nav.Viewer.Tests.</b> The check needs to see BOTH halves:
/// the authority for what is forbidden is <see cref="DemoWorldGroups.All"/>, and
/// the suspect is the <c>Nav.Viewer.Shared</c> assembly. Nav.Viewer.Tests
/// references Nav.Viewer.Shared and nothing else and must stay that way -- its
/// csproj says the absence IS the seam. This project already references
/// Nav.Viewer.Tactics, which is built on Nav.Viewer.Shared, so it sees both with
/// nothing added; it is also plain <c>net10.0</c>, so the audit runs without WPF
/// or Direct3D in the process, which Nav.Viewer.Wpf.Tests -- the other project
/// that can see both -- would have dragged in for a metadata read that needs
/// neither.
/// </para>
/// <para>
/// <b>Read out of the built article, in the idiom of <c>MutationWalk</c>.</b>
/// Method bodies are walked as IL and every <c>ldstr</c> is resolved, so a
/// finding carries the member it is written in rather than just the word --
/// which is what makes the failure actionable. Declared <c>const string</c>s are
/// read as well, because a constant used only by a HOST is inlined at the host's
/// call site and leaves no <c>ldstr</c> behind here at all.
/// </para>
/// </remarks>
public sealed class SeamVocabularyTests
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> Ops = BuildOps();

    /// <summary>The layer whose words are being looked for.</summary>
    private static Assembly Suspect => typeof(InspectorLayout).Assembly;

    [Fact]
    public void TheViewerNamesNoSuppliedSourcesVocabulary()
    {
        var forbidden = Forbidden();
        var literals = Literals(Suspect);

        // A SCAN THAT READ NOTHING WOULD BE GREEN, so say what was read before
        // saying nothing was wrong with it. Every expected string is named
        // through the production symbol, so a rename is a build error here
        // rather than a guard that quietly stops guarding.
        Assert.True(
            forbidden.Count > 0,
            $"{nameof(DemoWorldGroups)}.{nameof(DemoWorldGroups.All)} came back with nothing this layer is not "
            + "already entitled to say, so there is no word left to look for and this check would pass on "
            + "anything. Either the source declared no headings, or everything it declares now collides with "
            + $"{nameof(MovementGroups)} or {nameof(InspectorLayout)}'s own words -- see Forbidden().");

        Assert.True(
            literals.Count > 150,
            $"only {literals.Count} string literals read out of {Suspect.GetName().Name}, which is far short of "
            + "what it holds. The walk stopped early, so a green here would mean nothing was looked at rather "
            + "than that nothing was found -- fix the walk before trusting the result.");

        Assert.True(
            literals.Any(l => l.Text == InspectorLayout.SourcesGroup),
            $"the constant {nameof(InspectorLayout)}.{nameof(InspectorLayout.SourcesGroup)} was not among the "
            + $"{literals.Count} strings read, so the declared-constant half of the scan found nothing.");

        Assert.True(
            literals.Any(l => l.Text == ViewerOptions.KnownWorlds[0]),
            $"the only entry in {nameof(ViewerOptions)}.{nameof(ViewerOptions.KnownWorlds)} was not among the "
            + $"{literals.Count} strings read, and it lives in a type initializer rather than a constant, so "
            + "the IL half of the scan found nothing.");

        var breaches = literals.Where(l => forbidden.Contains(l.Text)).ToList();

        Assert.True(breaches.Count == 0, Report(breaches, literals.Count));
    }

    /// <summary>
    /// The words a supplied source owns and this layer may not say, which is
    /// everything the source declares LESS everything the layer is entitled to
    /// say already.
    /// </summary>
    /// <remarks>
    /// <b>Both halves are named, not listed.</b> The forbidden half is
    /// <see cref="DemoWorldGroups.All"/> itself -- writing the eight words down
    /// here would be the same second copy this whole check exists to catch. The
    /// subtracted half is what Nav.Viewer.Shared is allowed to name: Nav.Core is
    /// a permitted reference, so <see cref="MovementGroups"/> is its vocabulary
    /// as much as the movement layer's, and the viewer's own words are its own.
    /// <para>
    /// The subtraction removes nothing today, because the two vocabularies do not
    /// collide. It is here for the day a source declares a group called Field or
    /// Plan: that word would then be legal in this layer for a reason that has
    /// nothing to do with the source, and a check without the subtraction would
    /// fire on the movement layer's own heading.
    /// </para>
    /// </remarks>
    private static HashSet<string> Forbidden()
    {
        var forbidden = new HashSet<string>(DemoWorldGroups.All, StringComparer.Ordinal);
        forbidden.ExceptWith(MovementGroups.All);
        forbidden.ExceptWith(InspectorLayout.ViewerGroups);
        forbidden.ExceptWith(
            [InspectorLayout.MovementSection, InspectorLayout.TacticsSection, InspectorLayout.ViewerSection]);
        return forbidden;
    }

    /// <summary>
    /// What a breach reads like to whoever caused it, months from now, knowing
    /// none of this.
    /// </summary>
    private static string Report(IReadOnlyList<Literal> breaches, int scanned)
    {
        var text = new StringBuilder();
        text.Append(Suspect.GetName().Name)
            .Append(" names a supplied source's vocabulary in a string literal.\n\n");

        foreach (var breach in breaches)
        {
            text.Append("  \"").Append(breach.Text).Append("\"  in ").Append(breach.Where).Append('\n')
                .Append("      matches ").Append(Owner(breach.Text))
                .Append(", a heading declared by Nav.Viewer.Tactics.\n");
        }

        text.Append("\n")
            .Append("NOTHING ELSE CATCHES THIS. A quoted word carries no type, so the missing\n")
            .Append("project reference, the reference-closure walk in Nav.Viewer.Tests.SeamTests\n")
            .Append("and the code graph all see a clean assembly. This exact array of eight words\n")
            .Append("shipped once already, in 52b7dbc, and was found by a hand-run grep.\n\n")
            .Append("A GROUP NAME BELONGS TO THE LAYER THAT PRODUCES IT. Nav.Viewer.Shared may say\n")
            .Append("Nav.Core's headings and its own; a supplied source's it must not know, because\n")
            .Append("a source is somebody else's code and may name a group anything. Take the words\n")
            .Append("as an argument instead -- InspectorArrangement is that shape, and the\n")
            .Append("composition root is the one place entitled to see both halves of the seam at\n")
            .Append("once. Said there, the name is a symbol, so a rename fails to compile rather\n")
            .Append("than dropping a heading to the bottom of its section in a running window\n")
            .Append("nobody has open.\n\n")
            .Append(breaches.Count).Append(" of ").Append(scanned)
            .Append(" string literals read out of the assembly.");

        return text.ToString();
    }

    /// <summary>The constant on <see cref="DemoWorldGroups"/> a breach matched.</summary>
    private static string Owner(string word)
    {
        foreach (var field in typeof(DemoWorldGroups).GetFields(Declared))
        {
            if (field.IsLiteral && Equals(field.GetRawConstantValue(), word))
            {
                return $"{nameof(DemoWorldGroups)}.{field.Name}";
            }
        }

        return $"{nameof(DemoWorldGroups)}.{nameof(DemoWorldGroups.All)}";
    }

    /// <summary>Every string literal in <paramref name="assembly"/>, with where it is written.</summary>
    private static List<Literal> Literals(Assembly assembly)
    {
        var found = new List<Literal>();
        foreach (var type in assembly.GetTypes())
        {
            foreach (var field in type.GetFields(Declared))
            {
                if (field.IsLiteral && field.FieldType == typeof(string) &&
                    field.GetRawConstantValue() is string constant)
                {
                    found.Add(new Literal(constant, $"{type.Name}.{field.Name}"));
                }
            }

            // The type initializer by name as well as by the constructor sweep:
            // a static property with an initializer -- which is how every
            // vocabulary in this codebase is declared -- puts its words there.
            var bodies = type.GetMethods(Declared).Cast<MethodBase>()
                             .Concat(type.GetConstructors(Declared))
                             .Concat(type.TypeInitializer is { } initializer ? [initializer] : []);
            foreach (var method in bodies.DistinctBy(m => m.MetadataToken))
            {
                Read(method, found);
            }
        }

        return found;
    }

    /// <summary>Every <c>ldstr</c> in one body.</summary>
    /// <remarks>
    /// The operand has to be stepped over rather than searched for: the byte
    /// <c>0x72</c> sitting inside a token or a branch offset is not an
    /// instruction, and a scan that did not decode would report it as one.
    /// </remarks>
    private static void Read(MethodBase method, List<Literal> found)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or BadImageFormatException)
        {
            return;
        }

        if (il is null)
        {
            return;
        }

        var module = method.Module;
        var at = 0;
        while (at < il.Length)
        {
            short code = il[at];
            if (il[at] == 0xFE && at + 1 < il.Length)
            {
                code = unchecked((short)((0xFE << 8) | il[at + 1]));
                at += 2;
            }
            else
            {
                at++;
            }

            // An instruction this walk cannot size is an instruction it cannot
            // step over, so it stops reading this body rather than guessing and
            // reporting operand bytes as literals.
            if (!Ops.TryGetValue(code, out var op))
            {
                return;
            }

            if (op.OperandType == OperandType.InlineString)
            {
                try
                {
                    found.Add(new Literal(module.ResolveString(BitConverter.ToInt32(il, at)), Name(method)));
                }
                catch (Exception e) when (e is ArgumentException or BadImageFormatException)
                {
                    // A token this module cannot resolve is not a word anybody wrote here.
                }
            }

            at += OperandSize(op, il, at);
        }
    }

    private static string Name(MethodBase method) => $"{method.DeclaringType?.Name}.{method.Name}";

    private static int OperandSize(OpCode op, byte[] il, int operand) => op.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, operand)),
        _ => 4,
    };

    private static Dictionary<short, OpCode> BuildOps()
    {
        var ops = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode op)
            {
                ops[op.Value] = op;
            }
        }

        return ops;
    }

    /// <summary>One string literal, and the member it is written in.</summary>
    private readonly record struct Literal(string Text, string Where);
}
