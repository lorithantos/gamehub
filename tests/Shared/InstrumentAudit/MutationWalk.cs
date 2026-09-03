using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using Nav.Core;

namespace Nav.InstrumentAudit;

/// <summary>
/// Follows calls from a marked instrument through IL and reports every state
/// change it can reach.
/// </summary>
/// <remarks>
/// <b>The graph cannot answer this and neither can grep.</b> An extractor records
/// <c>_stale = false</c> as a write and <c>_visible[side] = seen</c> as a READ,
/// because the second is a field load followed by a mutating call on what it
/// points at. In this codebase that second shape is most state change, so a walk
/// that wants to find instruments causing things has to read the instructions.
/// <para>
/// <b>What makes it hard is that mutating your own scratch is constant.</b> An
/// instrument building rows calls <c>List.Add</c> thirty times and none of it
/// matters. So every mutation site is classified by where its receiver came
/// from, traced back through a small abstract stack: a <c>newobj</c> in the same
/// method is <see cref="Origin.Owned"/> and dropped, a field load is
/// <see cref="Origin.Field"/> and reported.
/// </para>
/// <para>
/// <b>A delegate is followed to what it was built from, not to what it is.</b>
/// Every body in an owned assembly is read once up front for the shape
/// <c>ldftn M</c> into a delegate <c>newobj</c> into a field, and a later
/// <c>Invoke</c> on that field expands to what was recorded. The store and the
/// call are almost never the same method -- a handler subscribed in a
/// constructor fires ten files away -- so nothing reachable from an instrument
/// would ever show it.
/// </para>
/// <para>
/// <b>The stack machine is linear and does not merge branches.</b> A value that
/// arrives at a join from two arms is whatever the last arm left, and a value
/// returned by a call is <see cref="Origin.Unknown"/> rather than traced into the
/// callee. Both are holes, both are visible in the output as an origin the
/// reader can weigh, and neither is papered over.
/// </para>
/// </remarks>
public sealed class MutationWalk
{
    private static readonly Dictionary<short, OpCode> Ops = BuildOps();

    private static readonly Dictionary<string, HashSet<string>> Mutators = BuildMutators();

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private readonly HashSet<Assembly> _own;
    private readonly Dictionary<(Guid Module, int Token), List<MethodBase>> _overrides = [];
    private readonly Dictionary<(Guid Module, int Token), HashSet<Landing>> _delegates = [];
    private readonly List<string> _notes = [];

    private bool _indexing;
    private int _visited;
    private int _ownedDropped;

    /// <param name="own">
    /// The assemblies whose bodies may be walked and whose virtual members may be
    /// expanded to their implementations. Everything else is a wall: a call into
    /// it is judged by <see cref="Mutators"/> alone and never followed.
    /// </param>
    public MutationWalk(params Assembly[] own)
    {
        ArgumentNullException.ThrowIfNull(own);
        _own = [.. own];
        foreach (var assembly in own)
        {
            Index(assembly);
        }

        foreach (var assembly in own)
        {
            IndexDelegates(assembly);
        }
    }

    /// <summary>Every method the walk gave up on, and why.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>Methods whose IL was read across every walk so far.</summary>
    public int Visited => _visited;

    /// <summary>Mutations dropped because their receiver was made in the same method.</summary>
    public int OwnedDropped => _ownedDropped;

    /// <summary>
    /// Members carrying <see cref="ObservesAttribute"/>, in name order. An
    /// interface member counts once here; the walk expands it.
    /// </summary>
    public static IReadOnlyList<MethodBase> Instruments(Assembly marked)
    {
        ArgumentNullException.ThrowIfNull(marked);

        var found = new List<MethodBase>();
        foreach (var type in marked.GetTypes())
        {
            foreach (var method in type.GetMethods(Declared))
            {
                if (method.IsDefined(typeof(ObservesAttribute), inherit: false))
                {
                    found.Add(method);
                }
            }

            foreach (var property in type.GetProperties(Declared))
            {
                if (property.IsDefined(typeof(ObservesAttribute), inherit: false) &&
                    property.GetGetMethod(nonPublic: true) is { } getter)
                {
                    found.Add(getter);
                }
            }
        }

        found.Sort((a, b) => string.CompareOrdinal(Name(a), Name(b)));
        return found;
    }

    /// <summary>
    /// Everything reachable from <paramref name="root"/> that changes state,
    /// suppressed findings included so the caller can see what was dropped.
    /// </summary>
    public IReadOnlyList<Mutation> From(MethodBase root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var rootName = Name(root);
        var found = new List<Mutation>();
        var seen = new HashSet<(Guid, int)>();
        var queue = new Queue<(MethodBase Method, string Path)>();

        foreach (var target in Targets(root))
        {
            if (seen.Add(Key(target)))
            {
                queue.Enqueue((target, Name(target)));
            }
        }

        while (queue.Count > 0)
        {
            var (method, path) = queue.Dequeue();
            Scan(method, rootName, path, found, (callee, calleePath) =>
            {
                foreach (var target in Targets(callee))
                {
                    if (seen.Add(Key(target)))
                    {
                        queue.Enqueue((target, calleePath));
                    }
                }
            });
        }

        return found;
    }

    /// <summary>The findings that survived suppression, as a block a failure can print.</summary>
    public static string Report(MethodBase root, IReadOnlyList<Mutation> found)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(found);

        var text = new StringBuilder();
        text.Append(Name(root)).Append(':');
        var kept = found.Where(m => m.Suppressed is null).ToList();
        if (kept.Count == 0)
        {
            text.Append(" clean");
        }

        foreach (var mutation in kept)
        {
            text.Append('\n').Append(mutation);
        }

        foreach (var reason in found.Where(m => m.Suppressed is not null)
                                    .GroupBy(m => m.Suppressed)
                                    .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.Append('\n').Append($"  ({reason.Count()} suppressed: {reason.Key})");
        }

        return text.ToString();
    }

    private void Index(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsInterface)
            {
                continue;
            }

            foreach (var method in type.GetMethods(Declared))
            {
                var root = method.GetBaseDefinition();
                if (Key(root) != Key(method))
                {
                    Add(root, method);
                }
            }

            foreach (var contract in type.GetInterfaces())
            {
                InterfaceMapping map;
                try
                {
                    map = type.GetInterfaceMap(contract);
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    continue;
                }

                for (var i = 0; i < map.InterfaceMethods.Length; i++)
                {
                    Add(map.InterfaceMethods[i], map.TargetMethods[i]);
                }
            }
        }

        void Add(MethodBase contract, MethodBase implementation)
        {
            var key = Key(contract);
            if (!_overrides.TryGetValue(key, out var list))
            {
                _overrides[key] = list = [];
            }

            if (!list.Any(m => Key(m) == Key(implementation)))
            {
                list.Add(implementation);
            }
        }
    }

    /// <summary>
    /// Reads every body in the assembly for delegates being built and put
    /// somewhere, so that an <c>Invoke</c> met later has a set to expand to.
    /// </summary>
    /// <remarks>
    /// Nothing here is reported and nothing is counted. It is the same walk with
    /// its findings thrown away, because a subscription written in a constructor
    /// is not reachable from the instrument that eventually fires it, and a pass
    /// that only read what the instrument reaches would find no delegates at all.
    /// </remarks>
    private void IndexDelegates(Assembly assembly)
    {
        _indexing = true;
        var sink = new List<Mutation>();
        try
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in type.GetMethods(Declared).Cast<MethodBase>()
                                           .Concat(type.GetConstructors(Declared)))
                {
                    Scan(method, string.Empty, string.Empty, sink, static (_, _) => { });
                }

                if (type.TypeInitializer is { } initializer)
                {
                    Scan(initializer, string.Empty, string.Empty, sink, static (_, _) => { });
                }
            }
        }
        finally
        {
            _indexing = false;
        }
    }

    /// <summary>
    /// Adds what a delegate value can land on to what <paramref name="field"/> is
    /// already known to hold.
    /// </summary>
    /// <remarks>
    /// A UNION, NOT A SLOT. A field written in a constructor and pointed
    /// somewhere else later holds either at runtime, and <c>+=</c> makes it hold
    /// both at once. The walk cannot tell which, so it answers for all of them.
    /// </remarks>
    private void Remember(FieldInfo? field, Val value)
    {
        if (field is null || value.Lands is not { Count: > 0 } lands)
        {
            return;
        }

        var key = Key(field);
        if (!_delegates.TryGetValue(key, out var set))
        {
            _delegates[key] = set = [];
        }

        set.UnionWith(lands);
    }

    /// <summary>Everything a load of <paramref name="field"/> could be holding.</summary>
    private IReadOnlyList<Landing>? Lands(FieldInfo? field) =>
        field is not null && _delegates.TryGetValue(Key(field), out var set) ? [.. set] : null;

    /// <summary>
    /// The method itself and, when the call could land elsewhere, every
    /// implementation of it in an owned assembly.
    /// </summary>
    /// <remarks>
    /// Only expanded for members DECLARED in an owned assembly. A callback
    /// through <c>IComparer</c> or <c>object.ToString</c> would otherwise fan out
    /// to every implementation in the library, and the answers would be about the
    /// framework's dispatch rather than about this code.
    /// </remarks>
    private IEnumerable<MethodBase> Targets(MethodBase method)
    {
        yield return method;

        if (!IsOwn(method))
        {
            yield break;
        }

        var pending = new Queue<MethodBase>();
        pending.Enqueue(method);
        var seen = new HashSet<(Guid, int)> { Key(method) };
        while (pending.Count > 0)
        {
            if (!_overrides.TryGetValue(Key(pending.Dequeue()), out var list))
            {
                continue;
            }

            foreach (var implementation in list)
            {
                if (seen.Add(Key(implementation)))
                {
                    pending.Enqueue(implementation);
                    yield return implementation;
                }
            }
        }
    }

    private void Scan(
        MethodBase method,
        string root,
        string path,
        List<Mutation> found,
        Action<MethodBase, string> enqueue)
    {
        byte[]? il;
        MethodBody? body;
        try
        {
            body = method.GetMethodBody();
            il = body?.GetILAsByteArray();
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or BadImageFormatException)
        {
            Note($"no body for {Name(method)}: {e.GetType().Name}");
            return;
        }

        if (body is null || il is null)
        {
            return;
        }

        if (!_indexing)
        {
            _visited++;
        }

        var module = method.Module;
        var typeArgs = method.DeclaringType is { IsGenericType: true } declaring
            ? declaring.GetGenericArguments()
            : null;
        var methodArgs = method is MethodInfo { IsGenericMethod: true } generic
            ? generic.GetGenericArguments()
            : null;

        var isConstructor = method is ConstructorInfo;
        var stack = new List<Val>();
        var locals = new Val[body.LocalVariables.Count];
        var known = new bool[body.LocalVariables.Count];

        // WHAT THE STACK LOOKS LIKE WHERE A BRANCH LANDS. Without this a ternary
        // inside an argument list loses the receiver that was pushed before it,
        // and every rows.Add in a debug view reports as a mutation of something
        // unknown. Forward edges are carried and merged; a back edge sees only
        // what the first arrival left, which is the loop's cost of being cheap.
        var landings = BranchTargets(il);
        var incoming = new Dictionary<int, List<Val>>();
        foreach (var clause in body.ExceptionHandlingClauses)
        {
            landings.Add(clause.HandlerOffset);
            incoming[clause.HandlerOffset] = clause.Flags == ExceptionHandlingClauseOptions.Finally ? [] : [Unknown];
        }

        var unreachable = false;
        var at = 0;
        while (at < il.Length)
        {
            if (landings.Contains(at))
            {
                if (incoming.TryGetValue(at, out var saved))
                {
                    var merged = unreachable ? saved : Merge(stack, saved);
                    stack.Clear();
                    stack.AddRange(merged);
                }
                else if (unreachable)
                {
                    stack.Clear();
                }

                unreachable = false;
            }

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

            if (!Ops.TryGetValue(code, out var op))
            {
                Note($"unknown opcode 0x{code:X4} in {Name(method)}; stopped reading it");
                return;
            }

            var operand = at;
            at += OperandSize(op, il, operand);

            if (op.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget)
            {
                var pops = Pops(op.StackBehaviourPop);
                if (pops > 0)
                {
                    Take(pops);
                }

                var target = at + (op.OperandType == OperandType.ShortInlineBrTarget
                    ? (sbyte)il[operand]
                    : BitConverter.ToInt32(il, operand));

                // Leaving a protected region empties the evaluation stack, so
                // what the handler's target sees is nothing, not this.
                Save(target, op.Name is "leave" or "leave.s" ? [] : stack);
                if (op.Name is "br" or "br.s" or "leave" or "leave.s")
                {
                    stack.Clear();
                    unreachable = true;
                }

                continue;
            }

            if (op.OperandType == OperandType.InlineSwitch)
            {
                Pop();
                var count = BitConverter.ToInt32(il, operand);
                for (var i = 0; i < count; i++)
                {
                    Save(at + BitConverter.ToInt32(il, operand + 4 + (4 * i)), stack);
                }

                continue;
            }

            switch (op.Name)
            {
                case "ldarg.0" or "ldarg.1" or "ldarg.2" or "ldarg.3":
                    Push(Arg(method, op.Name[^1] - '0'));
                    break;

                case "ldarg.s" or "ldarga.s":
                    Push(Arg(method, il[operand]));
                    break;

                case "ldarg" or "ldarga":
                    Push(Arg(method, BitConverter.ToUInt16(il, operand)));
                    break;

                case "ldloc.0" or "ldloc.1" or "ldloc.2" or "ldloc.3":
                    Push(Local(op.Name[^1] - '0'));
                    break;

                case "ldloc.s":
                    Push(Local(il[operand]));
                    break;

                case "ldloc":
                    Push(Local(BitConverter.ToUInt16(il, operand)));
                    break;

                case "ldloca.s":
                    Push(Escape(il[operand]));
                    break;

                case "ldloca":
                    Push(Escape(BitConverter.ToUInt16(il, operand)));
                    break;

                case "stloc.0" or "stloc.1" or "stloc.2" or "stloc.3":
                    Store(op.Name[^1] - '0', Pop());
                    break;

                case "stloc.s":
                    Store(il[operand], Pop());
                    break;

                case "stloc":
                    Store(BitConverter.ToUInt16(il, operand), Pop());
                    break;

                case "ldfld" or "ldflda":
                {
                    var owner = Pop();
                    var field = Field(operand);
                    Push(owner.Origin == Origin.Owned
                        ? new Val(Origin.Owned, $"{owner.Text}.{field?.Name}", Lands(field))
                        : new Val(Origin.Field, field?.Name ?? "?", Lands(field)));
                    break;
                }

                case "ldsfld" or "ldsflda":
                {
                    var field = Field(operand);
                    Push(new Val(Origin.StaticField, field?.Name ?? "?", Lands(field)));
                    break;
                }

                case "stfld":
                {
                    var value = Pop();
                    var owner = Pop();
                    var field = Field(operand);
                    Remember(field, value);
                    var suppressed = isConstructor && owner.Origin == Origin.This
                        ? "a constructor writing its own fields"
                        : Compiler(field);
                    Record($"{field?.Name ?? "?"} =", owner, suppressed);
                    break;
                }

                case "stsfld":
                {
                    var value = Pop();
                    var field = Field(operand);
                    Remember(field, value);
                    Record(
                        $"{field?.Name ?? "?"} =",
                        new Val(Origin.StaticField, field?.DeclaringType?.Name ?? "?"),
                        Compiler(field));
                    break;
                }

                case "newobj":
                {
                    var made = Resolve(operand);
                    var args = Take(made is null ? 0 : made.GetParameters().Length);
                    if (made is not null && IsOwn(made))
                    {
                        enqueue(made, $"{path} -> {Name(made)}");
                    }

                    Push(new Val(Origin.Owned, $"new {made?.DeclaringType?.Name ?? "?"}", Bind(made, args)));
                    break;
                }

                case "newarr":
                    Pop();
                    Push(new Val(Origin.Owned, "new[]"));
                    break;

                case "dup":
                    Push(stack.Count > 0 ? stack[^1] : Unknown);
                    break;

                case "ldftn" or "ldvirtftn":
                {
                    if (op.Name == "ldvirtftn")
                    {
                        Pop();
                    }

                    var target = Resolve(operand);
                    if (target is not null && IsOwn(target))
                    {
                        enqueue(target, $"{path} -> {Name(target)}");
                    }

                    // What it is bound to is still underneath, so the pair is only
                    // whole at the newobj that makes the delegate out of them.
                    Push(target is null
                        ? Unknown
                        : new Val(Origin.Unknown, $"ftn {Name(target)}", [new Landing(target, Unknown)]));
                    break;
                }

                case "call" or "callvirt":
                {
                    var callee = Resolve(operand);
                    if (callee is null)
                    {
                        break;
                    }

                    var count = callee.GetParameters().Length + (callee.IsStatic ? 0 : 1);
                    var args = Take(count);
                    var receiver = args.Length > 0 ? args[0] : Unknown;
                    if (IsMutator(callee))
                    {
                        Record($"{callee.Name}()", receiver, Compiler(receiver));
                    }

                    if (IsInvoke(callee))
                    {
                        Land(receiver);
                    }
                    else if (args.Length > 0 && Backing(callee) is { } backing)
                    {
                        Remember(backing, args[^1]);
                    }

                    if (callee is MethodInfo { ReturnType: var returns } && returns != typeof(void))
                    {
                        // A fluent member hands back what it was called on, so the
                        // second Append of a chain is the same object as the first.
                        Push(IsCombine(callee)
                            ? new Val(Origin.Owned, "combined", Union(args))
                            : returns == callee.DeclaringType && args.Length > 0
                                ? receiver
                                : new Val(Origin.Unknown, $"{Name(callee)}()"));
                    }

                    if (IsOwn(callee))
                    {
                        enqueue(callee, $"{path} -> {Name(callee)}");
                    }

                    break;
                }

                case "stelem" or "stelem.i" or "stelem.i1" or "stelem.i2" or "stelem.i4"
                    or "stelem.i8" or "stelem.r4" or "stelem.r8" or "stelem.ref":
                {
                    Pop();
                    Pop();
                    Record("[i] =", Pop(), null);
                    break;
                }

                case "ldelem" or "ldelem.i" or "ldelem.i1" or "ldelem.i2" or "ldelem.i4"
                    or "ldelem.i8" or "ldelem.r4" or "ldelem.r8" or "ldelem.ref"
                    or "ldelem.u1" or "ldelem.u2" or "ldelem.u4" or "ldelema":
                {
                    Pop();
                    var array = Pop();
                    Push(array.Origin == Origin.Owned
                        ? new Val(Origin.Owned, $"{array.Text}[]")
                        : new Val(Origin.Element, $"{array.Text}[]"));
                    break;
                }

                case "stobj" or "stind.i" or "stind.i1" or "stind.i2" or "stind.i4"
                    or "stind.i8" or "stind.r4" or "stind.r8" or "stind.ref":
                {
                    Pop();
                    var address = Pop();
                    Record("*p =", address, address.Origin == Origin.Argument ? "writing an out parameter" : null);
                    break;
                }

                case "initobj":
                {
                    var address = Pop();
                    Record("*p = default", address, address.Origin == Origin.Argument ? "writing an out parameter" : null);
                    break;
                }

                case "castclass" or "isinst" or "box" or "unbox.any" or "unbox":
                    Push(Pop());
                    break;

                case "ret" or "throw" or "rethrow" or "endfinally" or "endfilter":
                    stack.Clear();
                    unreachable = true;
                    break;

                default:
                {
                    var pops = Pops(op.StackBehaviourPop);
                    if (pops < 0)
                    {
                        _notes.Add($"variable stack at {op.Name} in {Name(method)}; the rest of it is guesswork");
                        stack.Clear();
                        break;
                    }

                    Take(pops);
                    for (var i = 0; i < Pushes(op.StackBehaviourPush); i++)
                    {
                        Push(Unknown);
                    }

                    break;
                }
            }
        }

        void Save(int target, List<Val> state) =>
            incoming[target] = incoming.TryGetValue(target, out var saved) ? Merge(saved, state) : [.. state];

        void Push(Val value) => stack.Add(value);

        Val Pop()
        {
            if (stack.Count == 0)
            {
                return Unknown;
            }

            var value = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return value;
        }

        Val[] Take(int count)
        {
            var taken = new Val[count];
            for (var i = count - 1; i >= 0; i--)
            {
                taken[i] = Pop();
            }

            return taken;
        }

        Val Local(int slot) => slot < known.Length && known[slot] ? locals[slot] : Unknown;

        Val Escape(int slot)
        {
            if (slot < known.Length)
            {
                known[slot] = true;
                locals[slot] = Unknown;
            }

            return Unknown;
        }

        void Store(int slot, Val value)
        {
            if (slot >= known.Length)
            {
                return;
            }

            locals[slot] = known[slot] ? Join(locals[slot], value) : value;
            known[slot] = true;
        }

        FieldInfo? Field(int operand)
        {
            try
            {
                return module.ResolveField(BitConverter.ToInt32(il, operand), typeArgs, methodArgs);
            }
            catch (Exception e) when (e is ArgumentException or BadImageFormatException)
            {
                Note($"unresolved field token in {Name(method)}");
                return null;
            }
        }

        MethodBase? Resolve(int operand)
        {
            try
            {
                return module.ResolveMethod(BitConverter.ToInt32(il, operand), typeArgs, methodArgs);
            }
            catch (Exception e) when (e is ArgumentException or BadImageFormatException)
            {
                Note($"unresolved method token in {Name(method)}");
                return null;
            }
        }

        // A DELEGATE CALL, EXPANDED TO WHAT THE FIELD WAS EVER SEEN HOLDING. Where
        // that is nothing the walk says so out loud: Invoke has no body to read
        // and no name on the mutator list, so silence here is the one answer that
        // would be indistinguishable from clean.
        void Land(Val target)
        {
            if (target.Lands is not { Count: > 0 } lands)
            {
                Note($"unresolved delegate {target.Text}.Invoke in {Name(method)}");
                return;
            }

            foreach (var landing in lands)
            {
                if (IsMutator(landing.Method))
                {
                    Record($"{landing.Method.Name}()", landing.On, Compiler(landing.On));
                }

                if (IsOwn(landing.Method))
                {
                    enqueue(landing.Method, $"{path} -> {Name(landing.Method)}");
                }
            }
        }

        void Record(string what, Val target, string? suppressed)
        {
            if (_indexing)
            {
                return;
            }

            if (target.Origin == Origin.Owned)
            {
                _ownedDropped++;
                return;
            }

            found.Add(new Mutation(root, Name(method), $"{target.Text}.{what}", target.Origin, path, suppressed));
        }
    }

    private static Val Arg(MethodBase method, int index)
    {
        if (!method.IsStatic)
        {
            if (index == 0)
            {
                return new Val(Origin.This, "this");
            }

            index--;
        }

        var parameters = method.GetParameters();
        return new Val(Origin.Argument, index < parameters.Length ? parameters[index].Name ?? "?" : "?");
    }

    /// <summary>
    /// The lambda cache and the closure. A delegate cached in a
    /// <c>&lt;&gt;c</c> field is a mutation the compiler wrote, and reporting it
    /// would put a finding on every instrument that passes a comparison.
    /// </summary>
    private static string? Compiler(MemberInfo? member) =>
        member is not null &&
        (member.Name.StartsWith('<') ||
         member.DeclaringType?.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) == true)
            ? "compiler-generated"
            : null;

    private static string? Compiler(Val receiver) =>
        receiver.Text.StartsWith('<') ? "compiler-generated" : null;

    /// <summary>
    /// A delegate's constructor takes the object it is bound to and the method it
    /// will land on, so the two values it pops are the whole answer.
    /// </summary>
    private static IReadOnlyList<Landing>? Bind(MethodBase? made, Val[] args) =>
        made?.DeclaringType is { } type &&
        typeof(Delegate).IsAssignableFrom(type) &&
        args.Length == 2 &&
        args[1].Lands is { Count: > 0 } lands
            ? [.. lands.Select(l => l with { On = args[0] })]
            : null;

    /// <summary>Everything any of these values could land on, counted once each.</summary>
    private static IReadOnlyList<Landing>? Union(params Val[] values)
    {
        List<Landing>? all = null;
        foreach (var value in values)
        {
            if (value.Lands is not { Count: > 0 } lands)
            {
                continue;
            }

            all ??= [];
            foreach (var landing in lands)
            {
                if (!all.Contains(landing))
                {
                    all.Add(landing);
                }
            }
        }

        return all;
    }

    private static bool IsInvoke(MethodBase method) =>
        method.Name == "Invoke" &&
        method.DeclaringType is { } declaring &&
        typeof(Delegate).IsAssignableFrom(declaring);

    /// <summary>
    /// <c>+=</c> and <c>-=</c> on a delegate, which the compiler writes as a
    /// combine and a store. Removal unions too: which arm ran is a runtime
    /// question, and dropping the target would be the answer that lies.
    /// </summary>
    private static bool IsCombine(MethodBase method) =>
        method.DeclaringType == typeof(Delegate) && method.Name is "Combine" or "Remove" or "RemoveAll";

    /// <summary>
    /// The backing field of a field-like event, so a subscription through the
    /// accessor lands where a plain assignment to the field would.
    /// </summary>
    private static FieldInfo? Backing(MethodBase method) =>
        method.Name.StartsWith("add_", StringComparison.Ordinal) &&
        method.DeclaringType?.GetField(method.Name[4..], Declared) is { } field &&
        typeof(Delegate).IsAssignableFrom(field.FieldType)
            ? field
            : null;

    private void Note(string text)
    {
        if (!_indexing)
        {
            _notes.Add(text);
        }
    }

    private static bool IsMutator(MethodBase method)
    {
        var declaring = method.DeclaringType;
        if (declaring is null)
        {
            return false;
        }

        var definition = declaring.IsGenericType ? declaring.GetGenericTypeDefinition() : declaring;
        return Mutators.TryGetValue(definition.FullName ?? definition.Name, out var members) &&
               members.Contains(method.Name);
    }

    private bool IsOwn(MethodBase method) =>
        method.DeclaringType is { } declaring && _own.Contains(declaring.Assembly);

    private static (Guid, int) Key(MemberInfo member) => (member.Module.ModuleVersionId, member.MetadataToken);

    private static string Name(MethodBase method)
    {
        var declaring = method.DeclaringType;
        var owner = declaring is null
            ? "?"
            : declaring.DeclaringType is null ? declaring.Name : $"{declaring.DeclaringType.Name}.{declaring.Name}";
        return $"{owner}.{method.Name}";
    }

    /// <summary>
    /// One stack per arm of a branch, reduced to what both agree on. Depths that
    /// disagree mean the walk lost track, and the shorter one is kept from the
    /// bottom because that is the part the arms share.
    /// </summary>
    private static List<Val> Merge(List<Val> left, List<Val> right)
    {
        var depth = Math.Min(left.Count, right.Count);
        var merged = new List<Val>(depth);
        for (var i = 0; i < depth; i++)
        {
            merged.Add(Join(left[i], right[i]));
        }

        return merged;
    }

    /// <summary>
    /// Two values for one slot. Where they disagree the slot is unknown, but what
    /// each could land on survives the disagreement: a delegate chosen by a
    /// ternary still gets invoked, and both arms are answerable for it.
    /// </summary>
    private static Val Join(Val left, Val right)
    {
        if (left == right)
        {
            return left;
        }

        var lands = Union(left, right);
        return left.Origin == right.Origin && left.Text == right.Text
            ? new Val(left.Origin, left.Text, lands)
            : new Val(Origin.Unknown, "?", lands);
    }

    /// <summary>Every offset something branches to, found by decoding once.</summary>
    private static HashSet<int> BranchTargets(byte[] il)
    {
        var targets = new HashSet<int>();
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

            if (!Ops.TryGetValue(code, out var op))
            {
                return targets;
            }

            var operand = at;
            at += OperandSize(op, il, operand);
            switch (op.OperandType)
            {
                case OperandType.ShortInlineBrTarget:
                    targets.Add(at + (sbyte)il[operand]);
                    break;

                case OperandType.InlineBrTarget:
                    targets.Add(at + BitConverter.ToInt32(il, operand));
                    break;

                case OperandType.InlineSwitch:
                {
                    var count = BitConverter.ToInt32(il, operand);
                    for (var i = 0; i < count; i++)
                    {
                        targets.Add(at + BitConverter.ToInt32(il, operand + 4 + (4 * i)));
                    }

                    break;
                }
            }
        }

        return targets;
    }

    private static readonly Val Unknown = new(Origin.Unknown, "?");

    /// <summary>
    /// One value on the abstract stack. <c>Lands</c> is what it can be invoked
    /// into, when it is a delegate the walk followed back to an <c>ldftn</c>, and
    /// null everywhere else.
    /// </summary>
    private readonly record struct Val(Origin Origin, string Text, IReadOnlyList<Landing>? Lands = null);

    /// <summary>One method a delegate can land on, and what it was bound to.</summary>
    /// <remarks>
    /// The receiver travels with the method because it is the whole finding for a
    /// framework target: <c>_seen.Add</c> as an <see cref="Action{T}"/> mutates
    /// <c>_seen</c>, and which list that is cannot be read off <c>List.Add</c>.
    /// </remarks>
    private sealed record Landing(MethodBase Method, Val On);

    private static int OperandSize(OpCode op, byte[] il, int operand) => op.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, operand)),
        _ => 4,
    };

    private static int Pops(StackBehaviour behaviour) => behaviour switch
    {
        StackBehaviour.Pop0 => 0,
        StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
        StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or
            StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or
            StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
        StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or
            StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or
            StackBehaviour.Popref_popi_popref or StackBehaviour.Popref_popi_pop1 => 3,
        _ => -1,
    };

    private static int Pushes(StackBehaviour behaviour) => behaviour switch
    {
        StackBehaviour.Push0 => 0,
        StackBehaviour.Push1_push1 => 2,
        StackBehaviour.Varpush => 0,
        _ => 1,
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

    /// <summary>
    /// Members of framework types that change what they are called on. The walk
    /// never reads a body outside the owned assemblies, so this list is the only
    /// thing standing between it and every collection write in the codebase.
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildMutators()
    {
        var mutators = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        Add("System.Collections.Generic.Dictionary`2", "set_Item", "Add", "TryAdd", "Remove", "Clear");
        Add("System.Collections.Generic.SortedDictionary`2", "set_Item", "Add", "Remove", "Clear");
        Add("System.Collections.Generic.SortedList`2", "set_Item", "Add", "Remove", "RemoveAt", "Clear");
        Add(
            "System.Collections.Generic.List`1",
            "set_Item", "Add", "AddRange", "Insert", "InsertRange", "Remove", "RemoveAt", "RemoveAll",
            "RemoveRange", "Clear", "Sort", "Reverse");
        Add(
            "System.Collections.Generic.HashSet`1",
            "Add", "Remove", "RemoveWhere", "Clear", "UnionWith", "IntersectWith", "ExceptWith", "SymmetricExceptWith");
        Add(
            "System.Collections.Generic.SortedSet`1",
            "Add", "Remove", "RemoveWhere", "Clear", "UnionWith", "IntersectWith", "ExceptWith", "SymmetricExceptWith");
        Add("System.Collections.Generic.Queue`1", "Enqueue", "Dequeue", "Clear", "TrimExcess");
        Add("System.Collections.Generic.Stack`1", "Push", "Pop", "Clear", "TrimExcess");
        Add("System.Collections.Generic.ICollection`1", "Add", "Remove", "Clear");
        Add("System.Collections.Generic.IList`1", "set_Item", "Insert", "RemoveAt");
        Add("System.Collections.Generic.IDictionary`2", "set_Item", "Add", "Remove");
        Add("System.Collections.Generic.ISet`1", "Add", "Remove", "Clear");
        // Not Array.Copy: what it writes is its SECOND argument, and the walk
        // judges a static mutator by its first.
        Add("System.Array", "Sort", "Clear", "Reverse", "Fill", "Resize");
        Add("System.Text.StringBuilder", "Append", "AppendLine", "AppendFormat", "Insert", "Remove", "Clear");

        return mutators;

        void Add(string type, params string[] members) => mutators[type] = [.. members];
    }
}
