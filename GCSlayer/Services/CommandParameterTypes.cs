using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;

namespace GCSlayer.Services;

/// <summary>
/// The kind of variable a command parameter holds, in the engine's own encoding:
/// <c>resultSetTypes[resultType]</c> / <c>resultGetTypes[resultType]</c> of
/// <c>CommandExecute.precompile_13</c> list world number/switch/string, player number/switch/string
/// and the scene object switch in exactly this order, and the data of the "set variable" command
/// stores the same number as <c>[13, resultType, varID, …]</c>.
/// </summary>
public enum VariableKind {
    WorldNumber = 0,
    WorldSwitch = 1,
    WorldString = 2,
    PlayerNumber = 3,
    PlayerSwitch = 4,
    PlayerString = 5,
    /// <summary>A switch of a scene object; no such editor list exists, so it is never harvested.</summary>
    SceneObjectSwitch = 6,
}

/// <summary>One step into a command parameter value: an array index or a field name.</summary>
public readonly record struct ParameterStep(int? Index, string? Member) {
    public static ParameterStep At(int index) => new(index, null);
    public static ParameterStep Field(string member) => new(null, member);
    public override string ToString() => Index is { } index ? $"[{index}]" : $".{Member}";
}

/// <summary>How the engine decides between a constant and a variable reference in the same slot.</summary>
public enum VariableGuardOperator {
    /// <summary>The value is used as a variable id when it is set (the usual "use variable" flag).</summary>
    Truthy,
    /// <summary>The value is used as a variable id when it is not set.</summary>
    Falsy,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

/// <summary>
/// A condition of the command instance that has to hold for a slot to mean what the script says.
///
/// The editor stores a constant and a variable id in the same slot and separates them with a flag or a
/// mode: <c>CommandExecute.command_17</c> waits <c>cmd.params[0]</c> milliseconds and reads
/// <c>triggerPlayer.variable.getVariable(cmd.params[0])</c> only when <c>cmd.params[3]</c> is set, so
/// without the guard the wait duration (30000) would be read as a variable id. The path is relative to
/// the command instance, i.e. its first step is a <c>cmd.params</c> index.
/// </summary>
/// <param name="Path">Where the deciding value sits, as <c>cmd.params</c> steps.</param>
/// <param name="Operator">Comparison the value has to satisfy.</param>
/// <param name="Value">Constant of the comparison; unused by truthiness.</param>
public sealed record VariableGuard(IReadOnlyList<ParameterStep> Path, VariableGuardOperator Operator, long Value);

/// <summary>
/// A command parameter that holds a variable id, at a path inside the parameter value
/// (empty when the parameter value itself is the id), plus the condition under which it does.
/// </summary>
public sealed record VariableSlot(VariableKind Kind, IReadOnlyList<ParameterStep> Path, VariableGuard? Guard = null);

/// <summary>
/// What every command does with <c>cmd.params[N]</c>, extracted from the game's editor script.
///
/// The editor compiles every command into <c>CommandExecute.command_&lt;cmdID&gt;(commandPage, cmd,
/// trigger, triggerPlayer)</c>, and the commands that need to prepare something into
/// <c>CommandExecute.precompile_&lt;cmdID&gt;</c>. Those functions are the only surviving description
/// of the parameter layout: which slot the engine passes to
/// <c>Command.gameWorld.getWorldVariable</c> / <c>triggerPlayer.variable.getString</c> / …, and which
/// slot it expands into <c>CustomCondition["f" + id](trigger, block)</c>.
///
/// The script is parsed into an ESTree AST (Acornima), so comments, strings and regular expressions
/// can never be mistaken for code — the game still carries a commented out earlier revision of some
/// functions, which must not contribute. Values are followed from <c>cmd.params[N]</c> through local
/// variables, through the helper functions the command declares (for example <c>getValue</c> of the
/// "if" command, whose <c>param[1]</c> is the world number variable of <c>cmd.params[2]</c>) and
/// through the shared helper objects of the script, so a nested slot is typed as precisely as the
/// engine reads it. Accessors the engine only builds as text for <c>eval</c> (the whole body of
/// <c>precompile_13</c>) are recovered from the string concatenation that assembles them.
///
/// Nothing is cached on disk; the table is rebuilt for every run from the project's own script.
/// </summary>
public sealed class CommandParameterTypes {
    /// <summary>Where the editor keeps its script, in the order the recovery tries them.</summary>
    private static readonly string[] ScriptFiles = ["GameCreator/script.js", "GameCreatorLib/gamecreator.js"];

    private readonly Dictionary<(int Command, int Parameter), List<VariableSlot>> _variables = [];
    private readonly Dictionary<(int Command, int Parameter), string> _modules = [];

    private CommandParameterTypes() { }

    /// <summary>True when a script was found and parsed.</summary>
    public bool Available { get; private init; }
    /// <summary>The script the table was extracted from, or the relative path that failed.</summary>
    public string? ScriptPath { get; private init; }
    /// <summary>Why no table could be extracted, for the log.</summary>
    public string? Error { get; private init; }
    /// <summary>Number of commands the script describes.</summary>
    public int Commands { get; private init; }
    /// <summary>Number of (command, parameter) pairs the script types.</summary>
    public int TypedParameters => _variables.Keys.Union(_modules.Keys).Count();
    /// <summary>Number of variable slots (command, parameter, path) extracted.</summary>
    public int VariableTypings => _variables.Values.Sum(slots => slots.Count);
    /// <summary>Number of custom condition/number/string references extracted.</summary>
    public int CustomTypings => _modules.Count;

    /// <summary>An empty table: everything falls back to the sources that do not need the script.</summary>
    public static CommandParameterTypes Empty { get; } = new();

    /// <summary>Extracts the table from the editor script of a project.</summary>
    public static CommandParameterTypes Build(string projectPath) {
        foreach (var relative in ScriptFiles) {
            var path = Path.Combine(projectPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;
            try {
                var analysis = CommandScriptAnalyzer.Analyze(File.ReadAllText(path));
                var types = new CommandParameterTypes { Available = true, ScriptPath = relative, Commands = analysis.Commands };
                foreach (var (key, slots) in analysis.Variables)
                    types._variables[key] = slots;
                foreach (var (key, module) in analysis.Modules)
                    types._modules[key] = module;
                return types;
            }
            catch (Exception exception) when (exception is SyntaxErrorException or IOException
                or InvalidOperationException or OutOfMemoryException) {
                return Failed(relative, exception.Message);
            }
        }
        return Failed(string.Join(", ", ScriptFiles), "not found");
    }

    private static CommandParameterTypes Failed(string path, string message) =>
        new() { ScriptPath = path, Error = message };

    /// <summary>Extracts the table from a script, for tests and for callers that hold the text.</summary>
    /// <exception cref="SyntaxErrorException">The script is not valid JavaScript.</exception>
    public static CommandParameterTypes FromScript(string script) {
        var analysis = CommandScriptAnalyzer.Analyze(script);
        var types = new CommandParameterTypes { Available = true, Commands = analysis.Commands };
        foreach (var (key, slots) in analysis.Variables)
            types._variables[key] = slots;
        foreach (var (key, module) in analysis.Modules)
            types._modules[key] = module;
        return types;
    }

    /// <summary>The variable slots of one command parameter, empty when the script says nothing.</summary>
    public IReadOnlyList<VariableSlot> VariableSlots(int commandId, int parameterIndex) =>
        _variables.TryGetValue((commandId, parameterIndex), out var slots) ? slots : [];

    /// <summary>
    /// The kind of variable the parameter itself holds, when the script agrees on a single kind.
    /// A parameter that carries a union (the "if" command stores a constant, a world id and a player
    /// id in the same slot) has no single kind and returns null; use <see cref="VariableSlots"/> to
    /// see the paths and the conditions the script puts on them.
    /// </summary>
    public VariableKind? Variable(int commandId, int parameterIndex) {
        var kinds = VariableSlots(commandId, parameterIndex)
            .Where(slot => slot.Path.Count == 0)
            .Select(slot => slot.Kind)
            .Distinct()
            .ToArray();
        return kinds.Length == 1 ? kinds[0] : null;
    }

    /// <summary>
    /// The custom condition/number/string module the parameter holds a reference to, or null.
    /// The value is stored as <c>[id, { parameter values }, [ { attribute ids } ]]</c>.
    /// </summary>
    public string? CustomModule(int commandId, int parameterIndex) =>
        _modules.TryGetValue((commandId, parameterIndex), out var module) ? module : null;
}

/// <summary>The raw result of the script analysis.</summary>
internal sealed record CommandScriptAnalysis(
    Dictionary<(int Command, int Parameter), List<VariableSlot>> Variables,
    Dictionary<(int Command, int Parameter), string> Modules,
    int Commands);

/// <summary>
/// Reads <c>CommandExecute.command_&lt;id&gt;</c> / <c>precompile_&lt;id&gt;</c> out of the editor script and
/// derives from the way they use <c>cmd.params</c> which slot is a variable id and which slot is a
/// reference to a custom condition/number/string.
///
/// The engine encodes a parameter at JSON index <c>i</c> of a command instance as <c>params[i - 1]</c>:
/// a page command instance is <c>[cmdID, p0, p1, …]</c> and <c>CommandPage.parse</c> builds the command
/// with <c>cmdData.slice(1)</c>, which was verified against <c>[13, resultType, varID, …]</c> (the engine
/// reads <c>cmd.params[0]</c> as the result type and <c>cmd.params[1]</c> as the variable id) and against
/// the custom command form <c>[10002, { attributeID: value }, ___cmdID]</c>, whose <c>cmd.params[0]</c>
/// is exactly the attribute keyed object.
/// </summary>
internal static class CommandScriptAnalyzer {
    public static CommandScriptAnalysis Analyze(string script) {
        var program = new Parser(new ParserOptions { EcmaVersion = EcmaVersion.Latest, Tolerant = true })
            .ParseScript(script);
        return new Analyzer(program).Run();
    }

    // ---------------------------------------------------------------- functions

    /// <summary>One function of the script, with the names of its parameters.</summary>
    private sealed record ScriptFunction(Node Node, IReadOnlyList<string> Parameters, Node Body, int? CommandId);

    /// <summary>A value the analyser can follow back to a command parameter.</summary>
    private sealed record Flow(FlowRoot Root, int CommandId, int ParameterIndex, IReadOnlyList<ParameterStep> Path) {
        public static Flow Cmd(int commandId) => new(FlowRoot.Cmd, commandId, -1, []);
        public static Flow Params(int commandId) => new(FlowRoot.CmdParams, commandId, -1, []);
        public static Flow Parameter(int index) => new(FlowRoot.Parameter, -1, index, []);
        public Flow Step(ParameterStep step) => this with { Path = [.. Path, step] };
    }

    private enum FlowRoot { Cmd, CmdParams, Parameter }

    /// <summary>Condition the engine evaluates before it uses a value; see <see cref="VariableGuard"/>.</summary>
    private sealed record Guard(Flow Flow, VariableGuardOperator Operator, long Value) {
        public Guard Negate() => this with { Operator = Operator switch {
            VariableGuardOperator.Truthy => VariableGuardOperator.Falsy,
            VariableGuardOperator.Falsy => VariableGuardOperator.Truthy,
            VariableGuardOperator.Equal => VariableGuardOperator.NotEqual,
            VariableGuardOperator.NotEqual => VariableGuardOperator.Equal,
            VariableGuardOperator.LessThan => VariableGuardOperator.GreaterThanOrEqual,
            VariableGuardOperator.LessThanOrEqual => VariableGuardOperator.GreaterThan,
            VariableGuardOperator.GreaterThan => VariableGuardOperator.LessThanOrEqual,
            _ => VariableGuardOperator.LessThan,
        } };
    }

    /// <summary>One typing derived from a function parameter, used to substitute call arguments.</summary>
    private sealed record ParameterTyping(int ParameterIndex, IReadOnlyList<ParameterStep> Path, VariableKind? Kind,
        string? Module, Guard? Guard = null);

    private sealed class LocalValue(Flow? flow, string? module) {
        public Flow? Flow { get; } = flow;
        public string? Module { get; } = module;
        public static readonly LocalValue None = new(null, null);
    }

    private sealed class Scope(Scope? parent) {
        public Scope? Parent { get; } = parent;
        public Dictionary<string, LocalValue> Locals { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ScriptFunction> Functions { get; } = new(StringComparer.Ordinal);
        /// <summary>Command the analysed function belongs to, so nested helpers type the same command.</summary>
        public int? CommandId { get; init; }
        /// <summary>Where typings of <see cref="FlowRoot.Parameter"/> flows are collected.</summary>
        public List<ParameterTyping>? Signature { get; init; }
        /// <summary>Condition of the branch being analysed, e.g. "cmd.params[3]" of a use-variable flag.</summary>
        public Guard? Guard { get; set; }

        public LocalValue? Lookup(string name) =>
            Locals.TryGetValue(name, out var value) ? value : Parent?.Lookup(name);

        public ScriptFunction? LookupFunction(string name) =>
            Functions.TryGetValue(name, out var function) ? function : Parent?.LookupFunction(name);
    }

    private sealed class Analyzer {
        private static readonly Regex ParameterReference = new(@"\b(?:cmd\.)?params\[(\d+)\]", RegexOptions.Compiled);
        private static readonly Regex AccessorReference = new(
            @"([A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*)\.((?:get|set)(?:World)?(?:Variable|Switch|String))\s*\(",
            RegexOptions.Compiled);
        private static readonly Regex ModuleReference = new(
            @"\b(Custom[A-Za-z]+|Cumtom[A-Za-z]+)\b", RegexOptions.Compiled);

        /// <summary>Coercions and clones that only pass their first argument through.</summary>
        private static readonly HashSet<string> IdentityFunctions = new(StringComparer.Ordinal) {
            "int", "float", "str", "string", "number", "parseInt", "parseFloat", "Number", "String",
            "depthClone", "clone",
        };

        private readonly Dictionary<(int Command, int Parameter), List<VariableSlot>> _variables = [];
        private readonly Dictionary<(int Command, int Parameter), string> _modules = [];
        private readonly HashSet<int> _commands = [];
        private readonly Dictionary<Node, ScriptFunction> _functions = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, ScriptFunction> _globals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScriptFunction> _members = new(StringComparer.Ordinal);
        private readonly Dictionary<Node, IReadOnlyList<ParameterTyping>> _signatures =
            new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Node> _signatureInProgress = new(ReferenceEqualityComparer.Instance);
        private readonly List<ScriptFunction> _commandFunctions = [];

        public Analyzer(Node program) {
            Collect(program);
        }

        public CommandScriptAnalysis Run() {
            foreach (var function in _commandFunctions)
                AnalyzeCommand(function);
            return new CommandScriptAnalysis(_variables, _modules, _commands.Count);
        }

        // ------------------------------------------------------------ discovery

        private void Collect(Node program) {
            var parents = BuildParentMap(program);
            var byName = new Dictionary<string, List<ScriptFunction>>(StringComparer.Ordinal);

            foreach (var node in Descendants(program)) {
                if (!TryReadFunction(node, parents, out var function))
                    continue;
                _functions[node] = function;
                if (node is FunctionDeclaration { Id: { } declaration })
                    Add(byName, declaration.Name, function);
                // "X.y = <function>" and "X.y = <declared function>": the shared helpers of the script.
                if (parents.GetValueOrDefault(node) is AssignmentExpression { Left: MemberExpression left }
                    && ChainText(left.Object) is { } owner && PropertyName(left) is { } property)
                    _members[owner + "." + property] = function;
            }

            // A member assignment whose right side names a declared function ("CustomCompData.x = f;").
            foreach (var node in Descendants(program)) {
                if (node is not AssignmentExpression {
                        Left: MemberExpression left,
                        Right: Identifier { Name: var called },
                    }) 
                    continue;
                if (ChainText(left.Object) is not { } owner || PropertyName(left) is not { } property)
                    continue;
                if (byName.TryGetValue(called, out var candidates) && candidates.Count == 1)
                    _members[owner + "." + property] = candidates[0];
            }

            foreach (var (name, candidates) in byName)
                if (candidates.Count == 1)
                    _globals[name] = candidates[0];

            foreach (var function in _functions.Values) {
                if (function.CommandId is not { } id)
                    continue;
                _commands.Add(id);
                _commandFunctions.Add(function);
            }
        }

        private static void Add(Dictionary<string, List<ScriptFunction>> map, string name, ScriptFunction function) {
            if (!map.TryGetValue(name, out var list))
                map[name] = list = [];
            list.Add(function);
        }

        /// <summary>
        /// Recognises a function and works out which command it belongs to. The engine registers it as
        /// "CommandExecute.command_N = command_N;" next to the declaration, and the release keeps the
        /// assignment, so the module never has to be guessed from the function name.
        /// </summary>
        private static bool TryReadFunction(Node node, Dictionary<Node, Node> parents, out ScriptFunction function) {
            function = null!;
            NodeList<Node> parameters;
            Node body;
            switch (node) {
                case FunctionDeclaration declaration:
                    parameters = declaration.Params;
                    body = declaration.Body;
                    break;
                case FunctionExpression expression:
                    parameters = expression.Params;
                    body = expression.Body;
                    break;
                case ArrowFunctionExpression arrow:
                    parameters = arrow.Params;
                    body = arrow.Body;
                    break;
                default:
                    return false;
            }

            var names = new List<string>();
            foreach (var parameter in parameters)
                names.Add(parameter is Identifier identifier ? identifier.Name : "_");
            function = new ScriptFunction(node, names, body, CommandIdOf(node, parents));
            return true;
        }

        private static int? CommandIdOf(Node function, Dictionary<Node, Node> parents) {
            var name = function switch {
                FunctionDeclaration declaration => declaration.Id?.Name,
                FunctionExpression expression => expression.Id?.Name,
                _ => null,
            };
            if (name is null)
                return null;
            var prefix = name.StartsWith("precompile_", StringComparison.Ordinal) ? "precompile_"
                : name.StartsWith("command_", StringComparison.Ordinal) ? "command_"
                : null;
            if (prefix is null || !int.TryParse(name[prefix.Length..], out var id))
                return null;

            // The registration statement next to the declaration names the module and the id.
            var statements = parents.GetValueOrDefault(function) switch {
                BlockStatement block => block.Body,
                _ => (IEnumerable<Statement>)[],
            };
            foreach (var statement in statements) {
                if (statement is not ExpressionStatement {
                        Expression: AssignmentExpression {
                            Left: MemberExpression { Object: Identifier { Name: var module } } left,
                            Right: Identifier { Name: var assigned },
                        },
                    } || assigned != name)
                    continue;
                if (PropertyName(left) == prefix + id && module.EndsWith("CommandExecute", StringComparison.Ordinal))
                    return id;
            }
            return null;
        }

        // ------------------------------------------------------------ command analysis

        private void AnalyzeCommand(ScriptFunction function) {
            var scope = new Scope(null) { CommandId = function.CommandId };
            if (function.Parameters.Count > 1)
                scope.Locals[function.Parameters[1]] = new LocalValue(Flow.Cmd(function.CommandId!.Value), null);
            foreach (var nested in Descendants(function.Body)) {
                if (TryReadNested(nested, scope, out var declared))
                    scope.Functions[declared] = _functions[nested];
            }
            AnalyzeBody(function.Body, scope);

            // A helper the command never calls can still hold a direct cmd.params access.
            foreach (var nested in Descendants(function.Body)) {
                if (_functions.TryGetValue(nested, out var helper) && helper.Node != function.Node)
                    Signature(helper, scope);
            }
        }

        private bool TryReadNested(Node node, Scope scope, out string name) {
            name = string.Empty;
            if (!_functions.ContainsKey(node))
                return false;
            switch (node) {
                case FunctionDeclaration { Id: { } declaration }:
                    name = declaration.Name;
                    return true;
                case FunctionExpression { Id: { } identifier }:
                    name = identifier.Name;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Types of one function's parameters, derived by analysing its body once with every parameter
        /// bound to a symbolic slot. The result is substituted at the call sites, which is what makes
        /// <c>getValue(params[1], params[2], params[16])</c> type <c>cmd.params[2][1]</c> as a world
        /// number variable without any command specific rule.
        /// </summary>
        private IReadOnlyList<ParameterTyping> Signature(ScriptFunction function, Scope outer) {
            if (_signatures.TryGetValue(function.Node, out var cached))
                return cached;
            if (!_signatureInProgress.Add(function.Node))
                return [];
            var typings = new List<ParameterTyping>();
            var scope = new Scope(outer) { CommandId = outer.CommandId, Signature = typings };
            for (var i = 0; i < function.Parameters.Count; i++)
                if (function.Parameters[i] != "_")
                    scope.Locals[function.Parameters[i]] = new LocalValue(Flow.Parameter(i), null);
            AnalyzeBody(function.Body, scope);
            _signatureInProgress.Remove(function.Node);
            _signatures[function.Node] = typings;
            return typings;
        }

        private void AnalyzeBody(Node body, Scope scope) {
            switch (body) {
                case BlockStatement block:
                    foreach (var statement in block.Body)
                        Visit(statement, scope);
                    break;
                case Expression expression:
                    Eval(expression, scope);
                    break;
            }
        }

        // ------------------------------------------------------------ statements

        /// <param name="guard">
        /// Condition of the branch this statement sits in; the inner branch wins over an outer one.
        /// </param>
        private void Visit(Statement statement, Scope scope, Guard? guard = null) {
            var outer = scope.Guard;
            if (guard is not null)
                scope.Guard = guard;
            switch (statement) {
                case BlockStatement block:
                    foreach (var child in block.Body)
                        Visit(child, scope);
                    break;
                case ExpressionStatement expression:
                    Eval(expression.Expression, scope);
                    break;
                case VariableDeclaration declaration:
                    foreach (var declarator in declaration.Declarations)
                        VisitDeclarator(declarator, scope);
                    break;
                case FunctionDeclaration:
                    break; // registered up front, analysed on demand
                case IfStatement conditional: {
                    var test = Eval(conditional.Test, scope);
                    // "if (cmd.params[3]) … getVariable(cmd.params[0]) …" only means a variable id
                    // inside the branch, which the harvest checks against the instance.
                    Visit(conditional.Consequent, scope, GuardOf(conditional.Test, test, true, scope));
                    if (conditional.Alternate is not null)
                        Visit(conditional.Alternate, scope, GuardOf(conditional.Test, test, false, scope));
                    break;
                }
                case ForStatement loop:
                    if (loop.Init is VariableDeclaration initializer)
                        Visit(initializer, scope);
                    else if (loop.Init is Expression initExpression)
                        Eval(initExpression, scope);
                    if (loop.Test is not null)
                        Eval(loop.Test, scope);
                    if (loop.Update is not null)
                        Eval(loop.Update, scope);
                    Visit(loop.Body, scope);
                    break;
                case ForInStatement forIn:
                    VisitForHead(forIn.Left, forIn.Right, scope);
                    Visit(forIn.Body, scope);
                    break;
                case ForOfStatement forOf:
                    VisitForHead(forOf.Left, forOf.Right, scope);
                    Visit(forOf.Body, scope);
                    break;
                case WhileStatement loop:
                    Eval(loop.Test, scope);
                    Visit(loop.Body, scope);
                    break;
                case DoWhileStatement loop:
                    Visit(loop.Body, scope);
                    Eval(loop.Test, scope);
                    break;
                case ReturnStatement returned:
                    if (returned.Argument is not null)
                        Eval(returned.Argument, scope);
                    break;
                case ThrowStatement thrown:
                    Eval(thrown.Argument, scope);
                    break;
                case SwitchStatement switchStatement: {
                    // "case 1:" of switch (cmd.params[6]) decides which variable kind a slot holds.
                    var discriminant = Eval(switchStatement.Discriminant, scope);
                    foreach (var switchCase in switchStatement.Cases) {
                        var caseGuard = switchCase.Test is null ? null : GuardOfConstant(discriminant, switchCase.Test);
                        if (switchCase.Test is not null)
                            Eval(switchCase.Test, scope);
                        foreach (var child in switchCase.Consequent)
                            Visit(child, scope, caseGuard);
                    }
                    break;
                }
                case TryStatement attempt:
                    Visit(attempt.Block, scope);
                    if (attempt.Handler is not null)
                        Visit(attempt.Handler.Body, scope);
                    if (attempt.Finalizer is not null)
                        Visit(attempt.Finalizer, scope);
                    break;
                case LabeledStatement labeled:
                    Visit(labeled.Body, scope);
                    break;
                case WithStatement with:
                    Visit(with.Body, scope);
                    break;
            }
            scope.Guard = outer;
        }

        /// <summary>
        /// The condition a branch of "test ? … : …" or "if (test) … else …" is under, as far as the
        /// engine decides the meaning of a slot with it: a flag parameter, or a comparison of a
        /// parameter against a constant. Anything else stays unconstrained rather than guessed.
        /// </summary>
        private Guard? GuardOf(Expression test, EvalResult value, bool truthy, Scope scope) {
            switch (test) {
                case UnaryExpression { Operator: Operator.LogicalNot } negation:
                    return GuardOf(negation.Argument, value, !truthy, scope);
                case MemberExpression or Identifier when value.Flow is { } flow:
                    return new Guard(flow, truthy ? VariableGuardOperator.Truthy : VariableGuardOperator.Falsy, 0);
                case BinaryExpression binary when IsComparison(binary.Operator) && value.Flow is not null: {
                    var left = Eval(binary.Left, scope);
                    var right = Eval(binary.Right, scope);
                    if (TryConstant(binary.Right, out var constant) && left.Flow is { } leftFlow)
                        return new Guard(leftFlow, Comparison(binary.Operator, truthy, false), constant);
                    if (TryConstant(binary.Left, out var mirrored) && right.Flow is { } rightFlow)
                        return new Guard(rightFlow, Comparison(binary.Operator, truthy, true), mirrored);
                    return null;
                }
                default:
                    return null;
            }
        }

        /// <summary>The condition of one "case value:" of a switch over a command parameter.</summary>
        private Guard? GuardOfConstant(EvalResult discriminant, Expression test) =>
            discriminant.Flow is { } flow && TryConstant(test, out var constant)
                ? new Guard(flow, VariableGuardOperator.Equal, constant)
                : null;

        private static bool IsComparison(Operator op) => op is Operator.Equality or Operator.Inequality
            or Operator.StrictEquality or Operator.StrictInequality or Operator.LessThan
            or Operator.LessThanOrEqual or Operator.GreaterThan or Operator.GreaterThanOrEqual;

        /// <summary>Maps a comparison, mirrored when the parameter sits on the right, and negated when
        /// the slot is used in the "else" branch.</summary>
        private static VariableGuardOperator Comparison(Operator op, bool truthy, bool mirrored) {
            var result = op switch {
                Operator.Equality or Operator.StrictEquality => VariableGuardOperator.Equal,
                Operator.Inequality or Operator.StrictInequality => VariableGuardOperator.NotEqual,
                Operator.LessThan => mirrored ? VariableGuardOperator.GreaterThan : VariableGuardOperator.LessThan,
                Operator.LessThanOrEqual => mirrored ? VariableGuardOperator.GreaterThanOrEqual : VariableGuardOperator.LessThanOrEqual,
                Operator.GreaterThan => mirrored ? VariableGuardOperator.LessThan : VariableGuardOperator.GreaterThan,
                _ => mirrored ? VariableGuardOperator.LessThanOrEqual : VariableGuardOperator.GreaterThanOrEqual,
            };
            if (truthy)
                return result;
            return result switch {
                VariableGuardOperator.Equal => VariableGuardOperator.NotEqual,
                VariableGuardOperator.NotEqual => VariableGuardOperator.Equal,
                VariableGuardOperator.LessThan => VariableGuardOperator.GreaterThanOrEqual,
                VariableGuardOperator.LessThanOrEqual => VariableGuardOperator.GreaterThan,
                VariableGuardOperator.GreaterThan => VariableGuardOperator.LessThanOrEqual,
                _ => VariableGuardOperator.LessThan,
            };
        }

        /// <summary>The numeric constant of a literal comparison.</summary>
        private static bool TryConstant(Expression expression, out long value) {
            switch (expression) {
                case NumericLiteral number when number.Value == Math.Floor(number.Value):
                    value = (long)number.Value;
                    return true;
                case UnaryExpression { Operator: Operator.UnaryNegation, Argument: NumericLiteral negative }
                    when negative.Value == Math.Floor(negative.Value):
                    value = -(long)negative.Value;
                    return true;
                case BooleanLiteral boolean:
                    value = boolean.Value ? 1 : 0;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private void VisitForHead(Node left, Expression right, Scope scope) {
            if (left is VariableDeclaration declaration)
                foreach (var declarator in declaration.Declarations)
                    VisitDeclarator(declarator, scope);
            else if (left is Expression expression)
                Eval(expression, scope);
            Eval(right, scope);
        }

        private void VisitDeclarator(VariableDeclarator declarator, Scope scope) {
            var value = declarator.Init is null ? default : Eval(declarator.Init, scope);
            if (declarator.Id is not Identifier identifier)
                return;
            // A local function value is resolvable by name at its call site.
            if (declarator.Init is not null && _functions.TryGetValue(declarator.Init, out var nested))
                scope.Functions[identifier.Name] = nested;
            scope.Locals[identifier.Name] = new LocalValue(value.Flow, value.Module);
        }

        // ------------------------------------------------------------ expressions

        private readonly record struct EvalResult(Flow? Flow, string? Module) {
            public static readonly EvalResult None = new(null, null);
        }

        private EvalResult Eval(Expression expression, Scope scope) {
            switch (expression) {
                case Identifier identifier:
                    return scope.Lookup(identifier.Name) is { } local
                        ? new EvalResult(local.Flow, local.Module)
                        : EvalResult.None;

                case MemberExpression member:
                    return EvalMember(member, scope);

                case CallExpression call:
                    return EvalCall(call, scope);

                case BinaryExpression binary:
                    return EvalBinary(binary, scope);

                case AssignmentExpression assignment: {
                    var value = Eval(assignment.Right, scope);
                    if (assignment.Left is Identifier target)
                        scope.Locals[target.Name] = new LocalValue(value.Flow, value.Module);
                    return value;
                }

                case UnaryExpression unary:
                    return Eval(unary.Argument, scope);

                case ConditionalExpression conditional: {
                    var test = Eval(conditional.Test, scope);
                    var consequent = EvalGuarded(conditional.Consequent, scope,
                        GuardOf(conditional.Test, test, true, scope));
                    var alternate = EvalGuarded(conditional.Alternate, scope,
                        GuardOf(conditional.Test, test, false, scope));
                    return consequent.Flow is not null || consequent.Module is not null ? consequent : alternate;
                }

                case SequenceExpression sequence: {
                    var last = EvalResult.None;
                    foreach (var item in sequence.Expressions)
                        last = Eval(item, scope);
                    return last;
                }

                case ArrayExpression array:
                    foreach (var element in array.Elements)
                        if (element is not null)
                            Eval(element, scope);
                    return EvalResult.None;

                case ObjectExpression json:
                    foreach (var property in json.Properties) {
                        if (property is Property { Value: Expression propertyValue })
                            Eval(propertyValue, scope);
                    }
                    return EvalResult.None;

                case TemplateLiteral template: {
                    var parts = new List<ChainPart>();
                    for (var i = 0; i < template.Quasis.Count; i++) {
                        parts.Add(ChainPart.Literal(template.Quasis[i].Value.Raw));
                        if (i < template.Expressions.Count)
                            parts.Add(ChainPart.Result(Eval(template.Expressions[i], scope)));
                    }
                    return EvaluateChain(parts, scope);
                }

                case FunctionExpression or ArrowFunctionExpression:
                    if (_functions.TryGetValue(expression, out var literal))
                        Signature(literal, scope); // direct cmd.params uses inside callbacks still count
                    return EvalResult.None;

                default:
                    // Anything else may still hold a nested call or assignment: walk it once.
                    foreach (var child in expression.ChildNodes)
                        if (child is Expression childExpression)
                            Eval(childExpression, scope);
                        else if (child is FunctionDeclaration or FunctionExpression or ArrowFunctionExpression
                            && _functions.TryGetValue(child, out var nested))
                            Signature(nested, scope);
                    return EvalResult.None;
            }
        }

        /// <summary>Evaluates an expression under the condition of the branch it sits in.</summary>
        private EvalResult EvalGuarded(Expression expression, Scope scope, Guard? guard) {
            var outer = scope.Guard;
            if (guard is not null)
                scope.Guard = guard;
            var result = Eval(expression, scope);
            scope.Guard = outer;
            return result;
        }

        private EvalResult EvalMember(MemberExpression member, Scope scope) {            // CustomCondition["f" + x]: a reference to a compiled custom extension entry. The value
            // itself is the module function, which the "if" command calls with cmd.params[15][1].
            if (member.Computed && PropertyName(member) is null
                && member.Property is BinaryExpression { Operator: Operator.Addition } addition
                && addition.Left is StringLiteral { Value: "f" }
                && ChainText(member.Object) is { } module && IsCustomModule(module)) {
                var id = Eval(addition.Right, scope);
                if (id.Flow is { } idFlow)
                    RecordModule(scope, idFlow, module);
                return new EvalResult(null, module);
            }

            var target = Eval(member.Object, scope);

            // <player|world>.variable.variables[id] style tables.
            if (member.Computed
                && member.Object is MemberExpression { Computed: false } table
                && PropertyName(table) is { } collection
                && CollectionKind(collection) is { } collectionKind) {
                var key = Eval(member.Property, scope);
                if (key.Flow is { } keyFlow)
                    RecordVariable(scope, keyFlow, KindOf(collectionKind, ChainText(table.Object)));
                return EvalResult.None;
            }

            if (member.Computed) {
                if (TryReadIndex(member.Property, out var step) && target.Flow is { } indexed)
                    return new EvalResult(indexed.Step(step), null);
                return EvalResult.None;
            }

            if (PropertyName(member) is not { } name || target.Flow is not { } flow)
                return EvalResult.None;
            if (flow.Root == FlowRoot.Cmd && name == "params")
                return new EvalResult(Flow.Params(flow.CommandId), null);
            if (name is "length")
                return EvalResult.None;
            return new EvalResult(flow.Step(ParameterStep.Field(name)), null);
        }

        private EvalResult EvalCall(CallExpression call, Scope scope) {
            var callee = Eval(call.Callee, scope);
            var arguments = new EvalResult[call.Arguments.Count];
            for (var i = 0; i < call.Arguments.Count; i++)
                arguments[i] = Eval(call.Arguments[i], scope);

            // <player|world variable>.getVariable(x) and friends.
            if (DescribeAccessor(call.Callee) is { } accessor && AccessorKind(accessor.Name) is { } accessorKind) {
                var kind = KindOf(accessorKind, accessor.Receiver, accessor.Name);
                foreach (var argument in arguments)
                    if (argument.Flow is { } flow)
                        RecordVariable(scope, flow, kind);
            }

            // A call of a custom extension reference (directly or through a local alias).
            if (callee.Module is { } module)
                foreach (var argument in arguments)
                    if (argument.Flow is { } flow)
                        RecordModule(scope, flow, module);

            // Substituting the arguments into a known helper function's parameters.
            if (SignatureOfCall(call.Callee, scope) is { } signature) {
                for (var i = 0; i < arguments.Length; i++) {
                    if (arguments[i].Flow is not { } flow)
                        continue;
                    foreach (var typing in signature.Where(t => t.ParameterIndex == i)) {
                        var combined = flow;
                        foreach (var step in typing.Path)
                            combined = combined.Step(step);
                        var guard = Substitute(typing.Guard, arguments);
                        if (typing.Kind is { } kind)
                            RecordVariable(scope, combined, kind, guard);
                        if (typing.Module is { } helperModule)
                            RecordModule(scope, combined, helperModule);
                    }
                }
            }

            // MathUtils.int(cmd.params[5]) and friends only pass their argument through.
            if (arguments.Length > 0 && CalledName(call.Callee) is { } called && IdentityFunctions.Contains(called))
                return arguments[0];

            return EvalResult.None;
        }

        /// <summary>Last name of a callee, e.g. "int" for MathUtils.int(…).</summary>
        private static string? CalledName(Expression callee) => callee switch {
            Identifier identifier => identifier.Name,
            MemberExpression member => PropertyName(member),
            _ => null,
        };

        /// <summary>
        /// Rewrites the guard of a helper function's typing into the caller's terms: a guard on the
        /// helper's parameter becomes the guard on the argument the caller passed there.
        /// </summary>
        private static Guard? Substitute(Guard? guard, EvalResult[] arguments) {
            if (guard is null)
                return null;
            switch (guard.Flow.Root) {
                case FlowRoot.CmdParams:
                    return guard;
                case FlowRoot.Parameter when guard.Flow.ParameterIndex >= 0
                    && guard.Flow.ParameterIndex < arguments.Length
                    && arguments[guard.Flow.ParameterIndex].Flow is { } flow:
                    var combined = flow;
                    foreach (var step in guard.Flow.Path)
                        combined = combined.Step(step);
                    return guard with { Flow = combined };
                default:
                    // The condition cannot be tied to a command parameter, so the slot stays unguarded
                    // rather than claiming a condition that may not hold.
                    return null;
            }
        }

        private IReadOnlyList<ParameterTyping>? SignatureOfCall(Expression callee, Scope scope) {
            switch (callee) {
                case Identifier identifier:
                    if (scope.LookupFunction(identifier.Name) is { } local)
                        return Signature(local, scope);
                    if (_globals.TryGetValue(identifier.Name, out var global))
                        return Signature(global, scope);
                    return null;
                case MemberExpression { Computed: false } member
                    when member.Object is Identifier { Name: var owner } && PropertyName(member) is { } name:
                    // Only the shared helpers the script assigns once ("CustomCompData.getSuperNumber").
                    return _members.TryGetValue(owner + "." + name, out var helper) ? Signature(helper, scope) : null;
                default:
                    return null;
            }
        }

        private EvalResult EvalBinary(BinaryExpression binary, Scope scope) {
            if (binary.Operator == Operator.Addition)
                return EvaluateChain(Flatten(binary, scope), scope);
            var left = Eval(binary.Left, scope);
            var right = Eval(binary.Right, scope);
            // "cmd.params[0] - 3" and "x * 2" still carry the parameter the expression is built from.
            return left.Flow is not null || left.Module is not null ? left : right;
        }

        /// <summary>Operands of a "+" chain, in source order, with string literals kept as text.</summary>
        private List<ChainPart> Flatten(BinaryExpression binary, Scope scope) {
            var parts = new List<ChainPart>();
            void Walk(Expression expression) {
                if (expression is BinaryExpression { Operator: Operator.Addition } nested) {
                    Walk(nested.Left);
                    Walk(nested.Right);
                    return;
                }
                if (expression is StringLiteral literal) {
                    parts.Add(ChainPart.Literal(literal.Value));
                    return;
                }
                parts.Add(ChainPart.Result(Eval(expression, scope)));
            }
            Walk(binary);
            return parts;
        }

        private readonly record struct ChainPart(string? LiteralText, EvalResult Value) {
            public static ChainPart Literal(string text) => new(text, EvalResult.None);
            public static ChainPart Result(EvalResult value) => new(null, value);
        }

        /// <summary>
        /// Recovers the accessors the engine assembles as text for <c>eval</c> (the whole body of
        /// precompile_13 builds the assignment as a string). Within one "+" chain a string that names
        /// an accessor or a custom module applies to the next operand, and a string that itself spells
        /// "cmd.params[N]" is a parameter reference of the accessor named just before it.
        /// </summary>
        private EvalResult EvaluateChain(List<ChainPart> parts, Scope scope) {
            VariableType? pendingKind = null;
            string? pendingAccessor = null;
            string? pendingModule = null;
            EvalResult first = EvalResult.None;
            var buildsText = false;
            foreach (var part in parts) {
                if (part.LiteralText is { } text) {
                    buildsText = true;
                    foreach (Match match in ParameterReference.Matches(text)) {
                        if (!int.TryParse(match.Groups[1].Value, out var indexIndex))
                            continue;
                        var reference = Flow.Params(scope.CommandId ?? -1).Step(ParameterStep.At(indexIndex));
                        if (pendingKind is { } kind)
                            RecordVariable(scope, reference, KindOf(kind, null, pendingAccessor));
                        else if (pendingModule is { } referencedModule)
                            RecordModule(scope, reference, referencedModule);
                    }
                    var accessor = AccessorReference.Match(text);
                    if (accessor.Success) {
                        pendingKind = AccessorKind(accessor.Groups[2].Value);
                        pendingAccessor = accessor.Groups[2].Value;
                        pendingModule = null;
                        continue;
                    }
                    var moduleMatch = ModuleReference.Match(text);
                    if (moduleMatch.Success && IsCustomModule(moduleMatch.Groups[1].Value)) {
                        pendingModule = moduleMatch.Groups[1].Value;
                        pendingKind = null;
                        pendingAccessor = null;
                    }
                    continue;
                }
                if (first.Flow is null && first.Module is null && (part.Value.Flow is not null || part.Value.Module is not null))
                    first = part.Value;
                if (part.Value.Flow is not { } flow)
                    continue;
                if (pendingKind is { } pending)
                    RecordVariable(scope, flow, KindOf(pending, null, pendingAccessor));
                else if (pendingModule is { } pendingCustom)
                    RecordModule(scope, flow, pendingCustom);
            }
            // A chain that carries a string builds code or a message, not a value: following the
            // parameters it mentions any further would type unrelated slots (precompile_13 assigns
            // every generated expression to the same local).
            return buildsText ? EvalResult.None : first;
        }

        // ------------------------------------------------------------ recording

        /// <summary>Structural comparison, since a guard's path is a list and not a value.</summary>
        private static bool SameGuard(VariableGuard? left, VariableGuard? right) => (left, right) switch {
            (null, null) => true,
            ({ } a, { } b) => a.Operator == b.Operator && a.Value == b.Value && a.Path.SequenceEqual(b.Path),
            _ => false,
        };

        /// <param name="typedGuard">
        /// Guard recovered for the value itself; when it is missing the guard of the branch the use
        /// sits in applies. A guard the harvest cannot tie to the command instance is dropped, so a
        /// slot never claims a condition that cannot be checked.
        /// </param>
        private void RecordVariable(Scope scope, Flow flow, VariableKind kind, Guard? typedGuard = null) {
            var guard = typedGuard ?? scope.Guard;
            switch (flow.Root) {
                case FlowRoot.CmdParams when flow.Path.Count > 0 && flow.Path[0].Index is { } index:
                    var key = (flow.CommandId, index);
                    if (!_variables.TryGetValue(key, out var slots))
                        _variables[key] = slots = [];
                    var path = flow.Path.Skip(1).ToArray();
                    var publicGuard = guard is { Flow.Root: FlowRoot.CmdParams } absolute && absolute.Flow.Path.Count > 0
                        ? new VariableGuard(absolute.Flow.Path, absolute.Operator, absolute.Value)
                        : null;
                    if (!slots.Any(slot => slot.Kind == kind && slot.Path.SequenceEqual(path)
                        && SameGuard(slot.Guard, publicGuard)))
                        slots.Add(new VariableSlot(kind, path, publicGuard));
                    break;
                case FlowRoot.Parameter when scope.Signature is { } signature:
                    signature.Add(new ParameterTyping(flow.ParameterIndex, flow.Path, kind, null, guard));
                    break;
            }
        }

        private void RecordModule(Scope scope, Flow flow, string module) {
            switch (flow.Root) {
                case FlowRoot.CmdParams when flow.Path.Count > 0 && flow.Path[0].Index is { } index:
                    // A custom value call is only stored where the slot means one, so the guard of the
                    // branch is already part of the evidence and is not repeated here.
                    _modules.TryAdd((flow.CommandId, index), module);
                    break;
                case FlowRoot.Parameter when scope.Signature is { } signature:
                    signature.Add(new ParameterTyping(flow.ParameterIndex, flow.Path, null, module));
                    break;
            }
        }

        // ------------------------------------------------------------ helpers

        private enum VariableType { Number, Switch, String }

        private (string Name, string? Receiver)? DescribeAccessor(Expression callee) {
            if (callee is not MemberExpression member)
                return null;
            var name = PropertyName(member);
            return name is null ? null : (name, ChainText(member.Object));
        }

        /// <summary>
        /// The accessors that take a variable id. The engine spells the world side either as a
        /// "getWorld…" method of the game world or as a plain "get…" method of a world variable
        /// holder, and the player side as "get…" on the player's variables.
        /// </summary>
        private static VariableType? AccessorKind(string name) => name switch {
            "getVariable" or "setVariable" or "getWorldVariable" or "setWorldVariable" => VariableType.Number,
            "getSwitch" or "setSwitch" or "getWorldSwitch" or "setWorldSwitch" => VariableType.Switch,
            "getString" or "setString" or "getWorldString" or "setWorldString" => VariableType.String,
            _ => null,
        };

        /// <summary>The tables a variable holder exposes for direct indexing, e.g. variable.variables[id].</summary>
        private static VariableType? CollectionKind(string name) => name switch {
            "variables" => VariableType.Number,
            "switchs" or "switches" => VariableType.Switch,
            "strings" => VariableType.String,
            _ => null,
        };

        /// <summary>
        /// The engine spells the world side with "World" (Command.gameWorld, ClientWorld, ServerWorld,
        /// getWorldSwitch) and the player side with the player or plain variable receiver.
        /// </summary>
        private static VariableKind KindOf(VariableType type, string? receiver, string? accessor = null) {
            var world = accessor?.Contains("World", StringComparison.Ordinal) == true
                || receiver?.Contains("World", StringComparison.Ordinal) == true;
            return (world, type) switch {
                (true, VariableType.Number) => VariableKind.WorldNumber,
                (true, VariableType.Switch) => VariableKind.WorldSwitch,
                (true, VariableType.String) => VariableKind.WorldString,
                (false, VariableType.Number) => VariableKind.PlayerNumber,
                (false, VariableType.Switch) => VariableKind.PlayerSwitch,
                _ => VariableKind.PlayerString,
            };
        }

        /// <summary>
        /// The custom extension modules the editor registers. The misspelled registration of the game
        /// is taken as the name it is written with: a document list exists only under the real name,
        /// and the attribution then falls back instead of pretending the two are the same module.
        /// </summary>
        private static bool IsCustomModule(string name) =>
            name.StartsWith("Custom", StringComparison.Ordinal) && name.Length > "Custom".Length;

        private static bool TryReadIndex(Expression expression, out ParameterStep step) {
            switch (expression) {
                case NumericLiteral number when number.Value >= 0 && number.Value == Math.Floor(number.Value):
                    step = ParameterStep.At((int)number.Value);
                    return true;
                case StringLiteral text:
                    step = ParameterStep.Field(text.Value);
                    return true;
                case UnaryExpression { Operator: Operator.UnaryNegation, Argument: NumericLiteral negative }
                    when negative.Value == Math.Floor(negative.Value):
                    step = ParameterStep.At(-(int)negative.Value);
                    return true;
                default:
                    step = default;
                    return false;
            }
        }

        /// <summary>Dotted text of a plain member chain, e.g. "Game.player.variable".</summary>
        private static string? ChainText(Expression expression) => expression switch {
            Identifier identifier => identifier.Name,
            ThisExpression => "this",
            MemberExpression { Computed: false } member when PropertyName(member) is { } name
                && ChainText(member.Object) is { } owner => owner + "." + name,
            _ => null,
        };

        private static string? PropertyName(MemberExpression member) => member switch {
            { Computed: false, Property: Identifier identifier } => identifier.Name,
            { Computed: true, Property: StringLiteral literal } => literal.Value,
            _ => null,
        };

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
    }
}
