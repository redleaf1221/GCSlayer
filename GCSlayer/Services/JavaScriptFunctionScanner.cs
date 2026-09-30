using Acornima;
using Acornima.Ast;

namespace GCSlayer.Services;

/// <summary>The value a compiled custom extension function expects for one parameter.</summary>
public enum JsParameterKind {
    /// <summary>A plain number (a constant the user types in).</summary>
    Number,
    /// <summary>A game number variable id, read through Game.player.variable.getVariable.</summary>
    NumberVariable,
    /// <summary>A string constant.</summary>
    String,
    /// <summary>A game string variable id, read through Game.player.variable.getString.</summary>
    StringVariable,
    /// <summary>A game switch variable id, read through Game.player.variable.getSwitch.</summary>
    SwitchVariable,
    /// <summary>A yes/no flag.</summary>
    Boolean,
    /// <summary>A small set of numbered alternatives, i.e. a dropdown.</summary>
    Enum,
    /// <summary>A structure the function reads fields from.</summary>
    Object,
    /// <summary>An indexed tuple such as p.numData2[0] / p.numData2[1].</summary>
    Array,
    /// <summary>A custom game number reference, i.e. CustomGameNumber["f" + p.x[0]](trigger, p.x[1]).</summary>
    NumberReference,
    /// <summary>A custom condition reference, i.e. CustomCondition["f" + p.x[0]](trigger, p.x[1]).</summary>
    ConditionReference,
    /// <summary>A custom game string reference.</summary>
    StringReference,
}

/// <summary>One parameter of a compiled custom extension function.</summary>
/// <param name="Name">Property name the function reads from its parameter object (p.Name).</param>
/// <param name="Kind">Value kind derived from how the function uses the parameter.</param>
/// <param name="Options">Numbered alternatives, for <see cref="JsParameterKind.Enum"/>.</param>
/// <param name="ArrayLength">Number of used slots, for <see cref="JsParameterKind.Array"/>.</param>
/// <param name="Inferred">
/// False when nothing in the function body constrains the value, i.e. <see cref="Kind"/> is only the
/// fallback. Whether the name suggests a flag or a string is deliberately ignored: such a parameter
/// gets the shipped data's observed kind when there is one, and the kind default otherwise.
/// </param>
/// <param name="Members">Fields the function reads from the value, for structures.</param>
public sealed record JsParameter(string Name, JsParameterKind Kind, IReadOnlyList<int> Options, int ArrayLength,
    bool Inferred, IReadOnlyList<string> Members);

/// <summary>One entry of an editor extension list, as the game script compiled it.</summary>
public sealed record JsExtensionFunction(string Module, int Id, IReadOnlyList<JsParameter> Parameters);

/// <summary>
/// Recovers the parameters of the custom editor extensions from the compiled game script.
///
/// The editor compiles every custom condition / game number / game string into
/// <c>CustomCondition.f7 = f7</c> with the signature <c>function f7(trigger, p)</c>; the second
/// argument is the parameter block the user fills in, so every <c>p.&lt;name&gt;</c> the function reads
/// is one editor control. The release pipeline deletes the extension documents, so this scan is the
/// only surviving record of what those controls were.
///
/// The script is parsed into an ESTree AST (Acornima, the acorn/Esprima port), so comments,
/// string literals, template literals and regular expressions can never be mistaken for code, and
/// each parameter access is classified by the expression node it sits in.
/// </summary>
public static class JavaScriptFunctionScanner {
    private static readonly string[] ModuleNames = [
        "CustomCondition",
        "CustomGameNumber",
        "CustomGameString",
        "CustomValueFunction",
        "CustomSceneObjectBehavior",
    ];

    /// <summary>The reference modules a custom extension parameter can point into.</summary>
    private static readonly Dictionary<string, JsParameterKind> ReferenceModules = new(StringComparer.Ordinal) {
        ["CustomCondition"] = JsParameterKind.ConditionReference,
        ["CustomGameNumber"] = JsParameterKind.NumberReference,
        ["CustomGameString"] = JsParameterKind.StringReference,
    };


    /// <summary>Methods that turn their receiver into a string, so the receiver is a string.</summary>
    private static readonly HashSet<string> StringMembers = new(StringComparer.Ordinal) {
        "length", "toString", "indexOf", "lastIndexOf", "startsWith", "endsWith", "includes",
        "split", "replace", "substring", "substr", "charAt", "charCodeAt", "trim", "toUpperCase",
        "toLowerCase", "slice", "padStart", "padEnd", "localeCompare", "match", "search",
    };

    /// <summary>Methods that only work on numbers.</summary>
    private static readonly HashSet<string> NumberMembers = new(StringComparer.Ordinal) {
        "toFixed", "toPrecision", "toExponential",
    };

    /// <summary>Variable accessors, mapped to the kind of variable id they take.</summary>
    private static readonly Dictionary<string, JsParameterKind> VariableAccessors = new(StringComparer.Ordinal) {
        ["getVariable"] = JsParameterKind.NumberVariable,
        ["setVariable"] = JsParameterKind.NumberVariable,
        ["getSwitch"] = JsParameterKind.SwitchVariable,
        ["setSwitch"] = JsParameterKind.SwitchVariable,
        ["getString"] = JsParameterKind.StringVariable,
        ["setString"] = JsParameterKind.StringVariable,
    };

    /// <summary>Global functions that coerce their argument, and the kind they coerce it to.</summary>
    private static readonly Dictionary<string, JsParameterKind> Coercions = new(StringComparer.Ordinal) {
        ["String"] = JsParameterKind.String,
        ["Number"] = JsParameterKind.Number,
        ["parseInt"] = JsParameterKind.Number,
        ["parseFloat"] = JsParameterKind.Number,
    };


    /// <summary>Parameters of every custom extension the script still defines.</summary>
    /// <remarks>Throws the parser's syntax exception when the script cannot be parsed at all.</remarks>
    public static IReadOnlyList<JsExtensionFunction> Scan(string script) {
        var program = new Parser(new ParserOptions { EcmaVersion = EcmaVersion.Latest, Tolerant = true })
            .ParseScript(script);
        var parents = BuildParentMap(program);

        var results = new List<JsExtensionFunction>();
        var seen = new HashSet<(string Module, int Id)>();
        foreach (var node in Descendants(program)) {
            if (!TryDescribeFunction(node, parents, out var module, out var id, out var body))
                continue;
            if (!seen.Add((module, id)))
                continue;
            results.Add(new JsExtensionFunction(module, id, Analyze(body, parents)));
        }
        return results;
    }

    // ---------------------------------------------------------------- function discovery

    /// <summary>Matches a function declaration or expression that the extension modules consume.</summary>
    private static bool TryDescribeFunction(Node node, Dictionary<Node, Node> parents,
        out string module, out int id, out Node body) {
        module = string.Empty;
        id = 0;
        body = node;

        Identifier? name;
        NodeList<Node> parameters;
        switch (node) {
            case FunctionDeclaration declaration:
                name = declaration.Id;
                parameters = declaration.Params;
                body = declaration.Body;
                break;
            case FunctionExpression { Id: not null } expression:
                name = expression.Id;
                parameters = expression.Params;
                body = expression.Body;
                break;
            default:
                return false;
        }

        if (name is null || !TryParseFunctionName(name.Name, out id))
            return false;
        // Every compiled extension takes (trigger, p).
        if (parameters.Count != 2 || parameters[1] is not Identifier { Name: "p" })
            return false;

        var resolved = ResolveModule(node, parents, id, name.Name);
        if (resolved is null)
            return false;
        module = resolved;
        return true;
    }

    private static bool TryParseFunctionName(string name, out int id) {
        id = 0;
        if (name.Length < 2 || name[0] != 'f')
            return false;
        for (var i = 1; i < name.Length; i++) {
            if (!char.IsAsciiDigit(name[i]))
                return false;
        }
        return int.TryParse(name.AsSpan(1), out id);
    }

    /// <summary>
    /// Finds the extension module a function belongs to: either the single parameter of the
    /// enclosing IIFE, or the "Module.f&lt;id&gt; = f&lt;id&gt;" assignment next to the declaration.
    /// </summary>
    private static string? ResolveModule(Node function, Dictionary<Node, Node> parents, int id, string functionName) {
        for (var current = function; parents.TryGetValue(current, out var parent); current = parent) {
            if (parent is FunctionExpression { Params.Count: 1 } iife
                && iife.Params[0] is Identifier { Name: var parameter }
                && ModuleNames.Contains(parameter, StringComparer.Ordinal))
                return parameter;
        }

        // Assignment form: CustomCondition.f7 = function (trigger, p) { ... }.
        if (parents.TryGetValue(function, out var assignment)
            && assignment is AssignmentExpression { Left: MemberExpression target }
            && target.Object is Identifier { Name: var module }
            && ModuleNames.Contains(module, StringComparer.Ordinal))
            return module;

        // Declaration that is assigned right afterwards: "CustomCondition.f7 = f7;".
        var statements = parents.GetValueOrDefault(function) switch {
            BlockStatement block => block.Body,
            Acornima.Ast.Program program => program.Body,
            _ => (IEnumerable<Statement>)[],
        };
        foreach (var statement in statements) {
            if (statement is not ExpressionStatement { Expression: AssignmentExpression assignment2 })
                continue;
            if (assignment2.Left is not MemberExpression { Object: Identifier { Name: var declaredModule } } left
                || !ModuleNames.Contains(declaredModule, StringComparer.Ordinal))
                continue;
            if (left.Property is Identifier { Name: var assigned } && assigned == "f" + id
                && assignment2.Right is Identifier { Name: var assignedFunction } && assignedFunction == functionName)
                return declaredModule;
        }
        return null;
    }

    // ---------------------------------------------------------------- parameter analysis

    private sealed class Evidence {
        public int Order = int.MaxValue;
        public int Numbers, Strings, Booleans, Objects, ArrayUses;
        public JsParameterKind? Variable;
        public int VariableHits;
        public JsParameterKind? Reference;
        public readonly SortedSet<int> Options = [];
        /// <summary>Fields read from the value, which identify the structure a control must pick.</summary>
        public readonly SortedSet<string> Members = new(StringComparer.Ordinal);
        public int MaxIndex = -1;
        /// <summary>Branch of a "flag ? a : b" whose counterpart kind constrains this parameter.</summary>
        public JsParameterKind? Counterpart;

        public bool HasEvidence => Numbers > 0 || Strings > 0 || Booleans > 0 || Objects > 0
            || ArrayUses > 0 || Variable is not null || Reference is not null || Counterpart is not null;

        public JsParameter ToParameter(string name) {
            var kind = Decide();
            var options = kind == JsParameterKind.Enum ? Options.ToArray() : [];
            return new JsParameter(name, kind, options, kind == JsParameterKind.Array ? MaxIndex + 1 : 0, HasEvidence,
                Members.ToArray());
        }

        private JsParameterKind Decide() {
            if (Reference is { } reference)
                return reference;
            if (Variable is { } variable)
                return variable;
            if (Counterpart is { } counterpart)
                return counterpart;
            if (Objects > 0 && Objects >= Numbers && Objects >= Strings)
                return JsParameterKind.Object;
            if (ArrayUses > 0)
                return JsParameterKind.Array;
            if (Options.Count >= 2 && Strings == 0 && Objects == 0)
                return JsParameterKind.Enum;
            if (Strings > 0 && Numbers == 0 && Booleans == 0)
                return JsParameterKind.String;
            if (Booleans > 0 && Numbers == 0 && Strings == 0)
                return JsParameterKind.Boolean;
            if (Strings > Numbers && Strings > Booleans)
                return JsParameterKind.String;
            if (Booleans > Numbers && Booleans > Strings)
                return JsParameterKind.Boolean;
            return JsParameterKind.Number;
        }
    }

    private static IReadOnlyList<JsParameter> Analyze(Node body, Dictionary<Node, Node> parents) {
        var evidence = new Dictionary<string, Evidence>(StringComparer.Ordinal);
        var pairs = new List<(string Test, string Branch, string Other)>();

        Evidence For(string name) {
            if (!evidence.TryGetValue(name, out var found))
                evidence[name] = found = new Evidence();
            return found;
        }

        foreach (var node in Reachable(body)) {
            switch (node) {
                case MemberExpression member when IsOutermostParameterExpression(member, parents):
                    if (!TryReadParameter(member, out var name, out var index, out var indexed, out var trailing))
                        break;
                    var e = For(name);
                    e.Order = Math.Min(e.Order, member.Start);
                    if (indexed) {
                        e.ArrayUses++;
                        if (index >= 0)
                            e.MaxIndex = Math.Max(e.MaxIndex, index);
                    }
                    Classify(e, member, trailing, parents);
                    break;

                case CallExpression { Callee: MemberExpression callee } call
                    when callee.Object is Identifier { Name: var module }
                        && ReferenceModules.TryGetValue(module, out var referenceKind):
                    ClassifyReference(referenceKind, callee, call, For);
                    break;

                case ConditionalExpression conditional:
                    ClassifyConditional(conditional, For, pairs);
                    break;
            }
        }

        ResolveConditionalPairs(pairs, evidence);
        return evidence
            .OrderBy(pair => pair.Value.Order)
            .Select(pair => pair.Value.ToParameter(pair.Key))
            .ToArray();
    }

    /// <summary>True when the node is not the object part of a longer "p.x.y" chain.</summary>
    private static bool IsOutermostParameterExpression(Node node, Dictionary<Node, Node> parents) {
        if (!parents.TryGetValue(node, out var parent))
            return true;
        if (parent is MemberExpression outer && ReferenceEquals(outer.Object, node))
            return false;
        return true;
    }

    /// <summary>Reads "p.name", "p.name[2]", 'p["name"]' or "p.name.member".</summary>
    private static bool TryReadParameter(Expression expression, out string name, out int index, out bool indexed,
        out string? trailing) {
        name = string.Empty;
        index = -1;
        indexed = false;
        trailing = null;
        if (expression is not MemberExpression member)
            return false;

        if (member.Object is Identifier { Name: "p" }) {
            switch (member) {
                case { Computed: false, Property: Identifier property }:
                    name = property.Name;
                    return true;
                case { Computed: true, Property: StringLiteral literal }:
                    name = literal.Value;
                    return true;
                default:
                    return false;
            }
        }

        if (member.Object is MemberExpression inner && TryReadParameter(inner, out var innerName, out _, out _, out _)) {
            name = innerName;
            indexed = member.Computed;
            if (member is { Computed: true, Property: NumericLiteral number })
                index = (int)number.Value;
            else if (member is { Computed: false, Property: Identifier property })
                trailing = property.Name;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reads the kind off the expression the parameter sits in. Only structure decides here: the
    /// spelling of the parameter name carries no meaning and is never consulted.
    /// </summary>
    private static void Classify(Evidence e, MemberExpression node, string? trailing,
        Dictionary<Node, Node> parents) {
        if (trailing is not null) {
            ClassifyMemberName(e, trailing);
            return;
        }

        var parent = parents.GetValueOrDefault(node);
        switch (parent) {
            case UnaryExpression { Operator: Operator.LogicalNot }:
                e.Booleans++;
                break;
            case IfStatement { Test: var test } when ReferenceEquals(test, node):
            case WhileStatement { Test: var whileTest } when ReferenceEquals(whileTest, node):
            case DoWhileStatement { Test: var doTest } when ReferenceEquals(doTest, node):
                e.Booleans++;
                break;
            case ConditionalExpression { Test: var conditionalTest } when ReferenceEquals(conditionalTest, node):
                e.Booleans++;
                break;
            case BinaryExpression binary:
                ClassifyBinary(e, binary, node);
                break;
            case CallExpression call:
                ClassifyCall(e, call, node);
                break;
            case MemberExpression { Computed: true } outer when ReferenceEquals(outer.Property, node):
                // Used as a property key, e.g. ui[p.uiAttrName]: the value is a name.
                e.Strings++;
                break;
            case MemberExpression:
                // The chain continues ("p.x.y"): the outer member decided the kind already.
                break;
        }
    }

    private static void ClassifyBinary(Evidence e, BinaryExpression binary, Node node) {
        if (binary.Operator is Operator.LogicalAnd or Operator.LogicalOr or Operator.NullishCoalescing) {
            e.Booleans++;
            return;
        }
        var other = ReferenceEquals(binary.Left, node) ? binary.Right : binary.Left;
        switch (binary.Operator) {
            case Operator.Equality:
            case Operator.Inequality:
            case Operator.StrictEquality:
            case Operator.StrictInequality:
            case Operator.LessThan:
            case Operator.LessThanOrEqual:
            case Operator.GreaterThan:
            case Operator.GreaterThanOrEqual:
                switch (other) {
                    case StringLiteral:
                        e.Strings++;
                        break;
                    case NumericLiteral number:
                        e.Numbers++;
                        if (number.Value is >= 0 and < 1000 && number.Value == Math.Floor(number.Value))
                            e.Options.Add((int)number.Value);
                        break;
                    case BooleanLiteral:
                        e.Booleans++;
                        break;
                }
                break;
            case Operator.Addition:
                if (other is StringLiteral)
                    e.Strings++;
                else
                    e.Numbers++;
                break;
            case Operator.Subtraction:
            case Operator.Multiplication:
            case Operator.Division:
            case Operator.Remainder:
            case Operator.Exponentiation:
            case Operator.BitwiseAnd:
            case Operator.BitwiseOr:
            case Operator.BitwiseXor:
            case Operator.LeftShift:
            case Operator.RightShift:
            case Operator.UnsignedRightShift:
                e.Numbers++;
                break;
        }
    }

    private static void ClassifyMemberName(Evidence e, string member) {
        e.Members.Add(member);
        if (StringMembers.Contains(member))
            e.Strings++;
        else if (NumberMembers.Contains(member))
            e.Numbers++;
        else
            e.Objects++;
    }

    private static void ClassifyCall(Evidence e, CallExpression call, Node argument) {
        if (!IsArgument(call, argument))
            return;
        var callee = CalleeName(call.Callee);
        if (callee is null)
            return;
        if (VariableAccessors.TryGetValue(callee, out var variable)) {
            e.Variable = variable;
            e.VariableHits++;
            return;
        }
        if (Coercions.TryGetValue(callee, out var coercion))
            Add(e, coercion);
    }

    private static bool IsArgument(CallExpression call, Node node) {
        for (var i = 0; i < call.Arguments.Count; i++) {
            if (ReferenceEquals(call.Arguments[i], node))
                return true;
        }
        return false;
    }

    private static void Add(Evidence e, JsParameterKind kind) {
        switch (kind) {
            case JsParameterKind.String:
                e.Strings++;
                break;
            case JsParameterKind.Boolean:
                e.Booleans++;
                break;
            default:
                e.Numbers++;
                break;
        }
    }

    /// <summary>Last name of a callee chain, e.g. "getString" for Game.player.variable.getString(...).</summary>
    private static string? CalleeName(Expression callee) => callee switch {
        Identifier identifier => identifier.Name,
        MemberExpression { Computed: false, Property: Identifier property } => property.Name,
        MemberExpression { Computed: true, Property: StringLiteral literal } => literal.Value,
        _ => null,
    };

    /// <summary>
    /// Recognises CustomGameNumber["f" + p.x[0]](trigger, p.x[1]) and friends: such a parameter holds
    /// a reference to another custom extension, which the editor edits with a reference picker.
    /// </summary>
    private static void ClassifyReference(JsParameterKind kind, MemberExpression callee, CallExpression call,
        Func<string, Evidence> evidence) {
        if (!callee.Computed || callee.Property is not BinaryExpression { Operator: Operator.Addition } addition)
            return;
        if (addition.Left is not StringLiteral { Value: "f" })
            return;

        var candidates = new List<Expression> { addition.Right };
        if (call.Arguments.Count > 0)
            candidates.Add(call.Arguments[0]);
        foreach (var expression in candidates) {
            if (TryReadParameter(expression, out var name, out _, out _, out _))
                evidence(name).Reference = kind;
        }
    }

    /// <summary>Records "flag ? a : b" and "!flag ? a : b" so the constant branch can be typed.</summary>
    private static void ClassifyConditional(ConditionalExpression conditional, Func<string, Evidence> evidence,
        List<(string Test, string Branch, string Other)> pairs) {
        if (!TryReadParameter(StripNegation(conditional.Test), out var test, out _, out _, out _))
            return;
        evidence(test).Booleans++;

        if (!TryReadBranch(conditional.Consequent, evidence, out var consequent)
            || !TryReadBranch(conditional.Alternate, evidence, out var alternate))
            return;
        pairs.Add((test, consequent, alternate));
    }

    /// <summary>
    /// Resolves one branch of a conditional to the parameter it reads. The variable branch of
    /// "useVar ? getString(name1) : name" is a call, not a bare access, so the accessor's argument
    /// and kind are unwrapped here.
    /// </summary>
    private static bool TryReadBranch(Expression expression, Func<string, Evidence> evidence, out string name) {
        if (TryReadParameter(expression, out name, out _, out _, out _))
            return true;
        if (expression is CallExpression call
            && CalleeName(call.Callee) is { } callee
            && VariableAccessors.TryGetValue(callee, out var kind)) {
            for (var i = 0; i < call.Arguments.Count; i++) {
                if (TryReadParameter(call.Arguments[i], out name, out _, out _, out _)) {
                    var e = evidence(name);
                    e.Variable = kind;
                    e.VariableHits++;
                    return true;
                }
            }
        }
        name = string.Empty;
        return false;
    }

    private static Expression StripNegation(Expression expression) =>
        expression is UnaryExpression { Operator: Operator.LogicalNot } negation ? negation.Argument : expression;

    /// <summary>
    /// "useVar ? variableId : constant" pairs a flag with two values of the same kind, so an
    /// otherwise unclassifiable constant can borrow the kind of its variable counterpart.
    /// </summary>
    private static void ResolveConditionalPairs(List<(string Test, string Branch, string Other)> pairs,
        Dictionary<string, Evidence> evidence) {
        foreach (var (_, branch, other) in pairs) {
            if (!evidence.TryGetValue(branch, out var a) || !evidence.TryGetValue(other, out var b))
                continue;
            if (!a.HasEvidence && b.HasEvidence)
                a.Counterpart = ConstantOf(b.Variable ?? b.Reference);
            else if (!b.HasEvidence && a.HasEvidence)
                b.Counterpart = ConstantOf(a.Variable ?? a.Reference);
        }
    }

    private static JsParameterKind? ConstantOf(JsParameterKind? kind) => kind switch {
        JsParameterKind.NumberVariable => JsParameterKind.Number,
        JsParameterKind.StringVariable => JsParameterKind.String,
        JsParameterKind.SwitchVariable => JsParameterKind.Boolean,
        _ => null,
    };

    // ---------------------------------------------------------------- traversal

    private static Dictionary<Node, Node> BuildParentMap(Node root) {
        var parents = new Dictionary<Node, Node>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0) {
            var node = stack.Pop();
            foreach (var child in node.ChildNodes) {
                parents[child] = node;
                stack.Push(child);
            }
        }
        return parents;
    }

    private static IEnumerable<Node> Descendants(Node root) {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0) {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.ChildNodes)
                stack.Push(child);
        }
    }

    /// <summary>
    /// Nodes of a function body that still see the function's own parameter object. A nested
    /// function that declares its own "p" or "trigger" hides it, so its subtree is skipped.
    /// </summary>
    private static IEnumerable<Node> Reachable(Node body) {
        var stack = new Stack<(Node Node, bool Root)>();
        stack.Push((body, true));
        while (stack.Count > 0) {
            var (node, root) = stack.Pop();
            if (!root && HidesParameterObject(node))
                continue;
            yield return node;
            foreach (var child in node.ChildNodes)
                stack.Push((child, false));
        }
    }

    private static bool HidesParameterObject(Node node) => node switch {
        FunctionDeclaration declaration => DeclaresParameter(declaration.Params),
        FunctionExpression expression => DeclaresParameter(expression.Params),
        ArrowFunctionExpression arrow => DeclaresParameter(arrow.Params),
        _ => false,
    };

    private static bool DeclaresParameter(NodeList<Node> parameters) {
        for (var i = 0; i < parameters.Count; i++) {
            if (parameters[i] is Identifier { Name: "p" or "trigger" })
                return true;
        }
        return false;
    }
}
