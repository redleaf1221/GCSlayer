using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CliFx.Infrastructure;

namespace GCSlayer.Services;

/// <summary>
/// Independently re-derives what a recovered project must contain and reports everything the IDE
/// would fail to resolve:
///
///  - list entries without a document and documents without a list entry;
///  - ids the shipped data references but the lists do not cover (item lists, variables);
///  - editor extension lists that disagree with the compiled game script;
///  - command parameters the shipped command definitions do not declare.
///
/// It shares no state with <see cref="ProjectRepairService"/>: expectations are taken from the
/// project data and the recovered script, so a green run is evidence and not a rubber stamp.
/// </summary>
public class ProjectVerifier {
    private static readonly Regex SetVariableCommand = new(
        @"\[\s*13\s*,\s*([0-6])\s*,\s*(\d+)\s*,", RegexOptions.Compiled);

    private static readonly Regex ScriptFunctionDefinition = new(
        @"(Custom[A-Za-z]+)\s*\.\s*f(\d+)\s*=", RegexOptions.Compiled);

    private static readonly Regex ScriptFunctionReference = new(
        @"(Custom[A-Za-z]+)\[""f""\s*\+\s*(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Signature the editor compiles every custom extension with: function f7(trigger, p).
    /// </summary>
    private static readonly Regex ScriptExtensionFunction = new(
        @"function\s+f(\d+)\s*\(\s*trigger\s*,\s*p\s*\)\s*\{", RegexOptions.Compiled);


    /// <summary>Attribute ids the editor generates look like 43114858493_0.09543223886965402.</summary>
    private static readonly Regex GeneratedAttributeId = new(@"^\d{6,}_0\.\d+$", RegexOptions.Compiled);

    private static readonly string[] VariableListFiles = [
        "server/variable/variable.json",
        "server/variable/switch.json",
        "server/variable/string.json",
        "variable/variable.json",
        "variable/switch.json",
        "variable/string.json"
    ];

    private static readonly string[] NamedFlatLists = [
        "animation/animationSignalList.json",
        "avatar/avatarActList.json",
        "avatar/avatarPartList.json",
        "standAvatar/expressionList.json",
        "standAvatar/standAvatarPartList.json"
    ];

    private readonly Func<string, Task> _write;
    private readonly List<string> _issues = [];
    private string _projectPath = "";
    private string _jsonRoot = "";
    private string[] _jsonFiles = [];

    public ProjectVerifier(IConsole console) : this(message => console.Output.WriteLineAsync(message)) { }

    public ProjectVerifier(Func<string, Task> write) => _write = write;

    /// <returns>True when the project can be loaded without unresolved references.</returns>
    public async Task<bool> VerifyAsync(string projectPath) {
        _projectPath = Path.GetFullPath(projectPath);
        _jsonRoot = Path.Combine(_projectPath, "asset", "json");
        if (!Directory.Exists(_jsonRoot))
            throw new DirectoryNotFoundException($"asset/json not found, not a project: {_projectPath}");
        _jsonFiles = Directory.GetFiles(_jsonRoot, "*.json", SearchOption.AllDirectories);

        await _write($"Verify {_projectPath} ({_jsonFiles.Length} json files)");

        CheckJsonFiles();
        CheckItemList("animation", "ani", "animationList.json", [@"\""[Aa]nimation(?:ID|Id|id)\""\s*:\s*(\d+)"]);
        CheckItemList("avatar", "avatar", "avatarList.json", [@"\""avatarID\""\s*:\s*(\d+)"]);
        CheckItemList("standAvatar", "standAvatar", "standAvatarList.json", [@"\""[Ss]tandAvatarID\""\s*:\s*(\d+)"]);
        CheckVariableLists();
        CheckExtensionLists();
        CheckNamedFlatLists();
        CheckCommandParameters();

        await _write(_issues.Count == 0 ? "OK: no unresolved references" : $"FAILED: {_issues.Count} unresolved reference(s)");
        foreach (var issue in _issues.Take(40))
            await _write("  " + issue);
        if (_issues.Count > 40)
            await _write($"  ... and {_issues.Count - 40} more");
        return _issues.Count == 0;
    }

    // ---------------------------------------------------------------- checks

    private void CheckJsonFiles() {
        var broken = 0;
        foreach (var file in _jsonFiles) {
            try {
                JsonNode.Parse(File.ReadAllText(file));
            } catch (Exception exception) {
                broken++;
                AddIssue("json", $"{Relative(file)}: {exception.Message}");
            }
        }
        _write($"- json files parsed: {_jsonFiles.Length - broken}/{_jsonFiles.Length}");
    }

    private void CheckItemList(string folder, string prefix, string listName, string[] referencePatterns) {
        var listPath = Path.Combine(_jsonRoot, folder, listName);
        var dataDir = Path.Combine(_jsonRoot, folder, "data");
        if (!File.Exists(listPath) || !Directory.Exists(dataDir)) {
            AddIssue("item-list", $"{folder}/{listName}: list or data directory missing");
            return;
        }

        var list = LoadObject(listPath)?["list"] as JsonObject ?? [];
        var fileRegex = new Regex("^" + Regex.Escape(prefix) + @"(\d+)$", RegexOptions.IgnoreCase);
        var documents = new SortedSet<int>();
        foreach (var file in Directory.GetFiles(dataDir, "*.json")) {
            var match = fileRegex.Match(Path.GetFileNameWithoutExtension(file));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var id) && id > 0)
                documents.Add(id);
        }

        var missingSlots = documents.Count(id => string.IsNullOrEmpty(NameOf(list, id)));
        foreach (var id in documents.Where(id => string.IsNullOrEmpty(NameOf(list, id))))
            AddIssue("item-list", $"{folder}/{listName}: document {prefix}{id}.json has no list entry");

        var orphanSlots = 0;
        foreach (var (type, local, name) in Slots(list)) {
            if (string.IsNullOrEmpty(name) || local == 0)
                continue;
            var id = (type - 1) * 1000 + local;
            if (documents.Contains(id))
                continue;
            orphanSlots++;
            AddIssue("item-list", $"{folder}/{listName}: entry {id} has no document");
        }

        var uncovered = 0;
        foreach (var pattern in referencePatterns)
            foreach (var id in Harvest(_jsonFiles, pattern))
                if (string.IsNullOrEmpty(NameOf(list, id))) {
                    uncovered++;
                    AddIssue("item-list", $"{folder}/{listName}: referenced id {id} has no entry");
                }

        _write($"- {folder}/{listName}: {documents.Count} document(s), {missingSlots} without slot, {orphanSlots} slot(s) without document, {uncovered} referenced id(s) uncovered");
    }

    private void CheckVariableLists() {
        var referenced = new SortedSet<int>[VariableListFiles.Length];
        for (var i = 0; i < referenced.Length; i++)
            referenced[i] = [];
        foreach (var file in _jsonFiles)
            foreach (Match match in SetVariableCommand.Matches(File.ReadAllText(file))) {
                var id = int.Parse(match.Groups[2].Value);
                if (id > 0)
                    referenced[int.Parse(match.Groups[1].Value)].Add(id);
            }

        var saved = ReadSaveVariableIds();
        var expected = new SortedSet<int>[VariableListFiles.Length];
        for (var kind = 0; kind < expected.Length; kind++) {
            expected[kind] = [.. referenced[kind]];
            foreach (var id in saved[kind % 3])
                expected[kind].Add(id);
        }

        for (var kind = 0; kind < VariableListFiles.Length; kind++) {
            var relative = VariableListFiles[kind];
            var listPath = Path.Combine(_jsonRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(listPath)) {
                AddIssue("variable", $"{relative}: missing");
                continue;
            }
            var list = LoadObject(listPath)?["list"] as JsonObject ?? [];
            var uncovered = 0;
            foreach (var id in expected[kind])
                if (id > 0 && string.IsNullOrEmpty(NameOf(list, id))) {
                    uncovered++;
                    AddIssue("variable", $"{relative}: id {id} used by the game has no name slot");
                }
            _write($"- {relative}: {expected[kind].Count} id(s) in use, {uncovered} uncovered");
        }
    }

    private void CheckExtensionLists() {
        var scriptPath = Path.Combine(_projectPath, "Game", "GCMain.ts");
        var scriptIds = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        var scriptParameters = new Dictionary<(string Module, int Id), SortedSet<string>>();
        if (File.Exists(scriptPath)) {
            var script = File.ReadAllText(scriptPath);
            // The script is the game: an edit that breaks its syntax must never pass unnoticed.
            try {
                new Acornima.Parser(new Acornima.ParserOptions {
                    EcmaVersion = Acornima.EcmaVersion.Latest,
                    Tolerant = true,
                }).ParseScript(script);
            }
            catch (Acornima.SyntaxErrorException exception) {
                AddIssue("game-script", $"Game/GCMain.ts does not parse: {exception.Message}");
            }
            scriptParameters = ReadScriptParameters(script);
            foreach (var regex in new[] { ScriptFunctionDefinition, ScriptFunctionReference })
                foreach (Match match in regex.Matches(script)) {
                    var module = match.Groups[1].Value;
                    if (!scriptIds.TryGetValue(module, out var set))
                        scriptIds[module] = set = [];
                    set.Add(int.Parse(match.Groups[2].Value));
                }
            // The parameter scan finds the functions of misspelled modules as well.
            foreach (var (module, id) in scriptParameters.Keys) {
                if (!scriptIds.TryGetValue(module, out var set))
                    scriptIds[module] = set = [];
                set.Add(id);
            }
        } else {
            _write("- Game/GCMain.ts missing, extension lists not checked against the script");
        }

        (string Module, string List, string? Directory, string Prefix, int Offset)[] maps = [
            ("CustomCondition", "custom/customCondition.json", "condition/condition", "condition", 0),
            ("CustomGameNumber", "custom/customGameNumberList.json", "gameNumber/num", "num", 0),
            ("CustomGameString", "custom/customGameStringList.json", "gameString/str", "str", 0),
            ("CustomValueFunction", "custom/customValueFunction.json", null, "", -1),
            ("CustomSceneObjectBehavior", "custom/customBehaviorType.json", "behavior/behavior", "behavior", 0)
        ];

        foreach (var (module, relative, directory, prefix, offset) in maps) {
            var listPath = Path.Combine(_jsonRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(listPath)) {
                AddIssue("extension-list", $"{relative}: missing");
                continue;
            }
            var list = LoadObject(listPath)?["list"] as JsonArray ?? [];

            var ids = new SortedSet<int>();
            if (scriptIds.TryGetValue(module, out var raw))
                foreach (var id in raw)
                    if (id + offset > 0)
                        ids.Add(id + offset);

            var problems = 0;
            for (var index = 1; index < list.Count; index++) {
                var named = !string.IsNullOrEmpty(list[index]?.GetValue<string>());
                if (named != ids.Contains(index)) {
                    problems++;
                    AddIssue("extension-list", $"{relative}: slot {index} {(named ? "exists" : "is empty")} but the script says otherwise");
                }
            }
            if (list.Count != (ids.Count == 0 ? 1 : ids.Max + 1)) {
                problems++;
                AddIssue("extension-list", $"{relative}: length {list.Count} does not match the script ids");
            }

            var documents = 0;
            if (directory is not null) {
                var dirPath = Path.Combine(_jsonRoot, "custom", Path.GetDirectoryName(directory)!.Replace('/', Path.DirectorySeparatorChar));
                var onDisk = Directory.Exists(dirPath)
                    ? Directory.GetFiles(dirPath, Path.GetFileName(directory) + "*.json")
                        .Select(file => int.TryParse(Path.GetFileNameWithoutExtension(file)[prefix.Length..], out var id) ? id : 0)
                        .Where(id => id > 0).ToHashSet()
                    : [];
                documents = onDisk.Count;
                foreach (var id in ids.Where(id => !onDisk.Contains(id))) {
                    problems++;
                    AddIssue("extension-list", $"{relative}: entry {id} has no document");
                }
                foreach (var id in onDisk.Where(id => !ids.Contains(id))) {
                    problems++;
                    AddIssue("extension-list", $"{dirPath}: document {id} has no entry");
                }
            }

            // Every parameter the compiled function reads must be a control of the document, and a
            // control whose value the function never reads is stale. This is what makes the entry
            // editable: without the control the block has nothing to show.
            foreach (var id in ids) {
                if (directory is null)
                    break;
                var documentPath = Path.Combine(_jsonRoot, "custom",
                    (directory + id + ".json").Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(documentPath))
                    continue;
                var document = LoadObject(documentPath);
                if (document is null) {
                    problems++;
                    AddIssue("extension-list", $"{directory}{id}.json: not readable");
                    continue;
                }
                var controls = ReadDocumentParameters(document);
                var expected = scriptParameters.GetValueOrDefault((module, id), []);
                foreach (var name in expected.Where(name => !controls.Contains(name))) {
                    problems++;
                    AddIssue("extension-list",
                        $"{directory}{id}.json: parameter '{name}' of {module}.f{id} has no control");
                }
                foreach (var name in controls.Where(name => !expected.Contains(name))) {
                    problems++;
                    AddIssue("extension-list",
                        $"{directory}{id}.json: control '{name}' is not read by {module}.f{id}");
                }
            }

            _write($"- {relative}: {ids.Count} script id(s), {documents} document(s), {problems} problem(s)");
        }
    }


    /// <summary>
    /// Re-derives the parameters of every compiled extension with a plain comment/string aware scan,
    /// so that the expectation does not come from the parser the repair uses.
    /// </summary>
    private static Dictionary<(string Module, int Id), SortedSet<string>> ReadScriptParameters(string script) {
        var result = new Dictionary<(string Module, int Id), SortedSet<string>>();
        var text = StripComments(script);
        foreach (Match match in ScriptExtensionFunction.Matches(text)) {
            var body = ReadBody(text, match.Index + match.Length - 1);
            if (body is null)
                continue;
            var module = FindModule(text, match.Index, body.Value.End, match.Groups[1].Value);
            if (module is null)
                continue;
            var id = int.Parse(match.Groups[1].Value);
            if (!result.TryGetValue((module, id), out var names))
                result[(module, id)] = names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match parameter in Regex.Matches(body.Value.Text, @"\bp\.([A-Za-z_$][\w$]*)"))
                names.Add(parameter.Groups[1].Value);
        }
        return result;
    }

    /// <summary>The module an extension function is registered with, by its assignment.</summary>
    private static string? FindModule(string text, int functionStart, int bodyEnd, string id) {
        var after = text[bodyEnd..Math.Min(text.Length, bodyEnd + 200)];
        var assignment = Regex.Match(after, @"([A-Za-z_$][\w$]*)\s*\.\s*f" + id + @"\s*=");
        if (assignment.Success)
            return assignment.Groups[1].Value;
        var before = text[Math.Max(0, functionStart - 400)..functionStart];
        var iife = Regex.Matches(before, @"\(\s*function\s*\(\s*([A-Za-z_$][\w$]*)\s*\)");
        return iife.Count > 0 ? iife[^1].Groups[1].Value : null;
    }

    /// <summary>Brace matching that ignores braces inside string literals and comments.</summary>
    private static (string Text, int End)? ReadBody(string text, int brace) {
        var depth = 0;
        for (var i = brace; i < text.Length; i++) {
            var c = text[i];
            if (c is '"' or '\'') {
                i++;
                while (i < text.Length && text[i] != c) {
                    if (text[i] == '\\')
                        i++;
                    i++;
                }
                continue;
            }
            if (c == '{')
                depth++;
            else if (c == '}' && --depth == 0)
                return (text[brace..(i + 1)], i + 1);
        }
        return null;
    }

    private static string StripComments(string script) =>
        Regex.Replace(Regex.Replace(script, @"/\*[\s\S]*?\*/", " "), @"//[^\n]*", " ");

    /// <summary>Parameter names the document offers as controls.</summary>
    private static SortedSet<string> ReadDocumentParameters(JsonObject document) {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        if (document["blockList"] is not JsonArray blocks)
            return names;
        foreach (var block in blocks) {
            if (block?["blockAttrs"] is not JsonArray attributes)
                continue;
            foreach (var entry in attributes) {
                var name = (entry?["attr"] as JsonObject)?["varName"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
        }
        return names;
    }

    private void CheckNamedFlatLists() {
        var blanks = 0;
        foreach (var relative in NamedFlatLists.Concat(VariableListFiles)) {
            var path = Path.Combine(_jsonRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) {
                AddIssue("flat-list", $"{relative}: missing");
                continue;
            }
            var root = LoadObject(path);
            var named = 0;
            if (root?["list"] is JsonArray flat) {
                for (var index = 1; index < flat.Count; index++) {
                    named++;
                    if (string.IsNullOrEmpty(flat[index]?.GetValue<string>())) {
                        blanks++;
                        AddIssue("flat-list", $"{relative}: entry {index} has no name");
                    }
                }
            } else if (root?["list"] is JsonObject typed) {
                foreach (var (type, local, name) in Slots(typed)) {
                    if (local == 0)
                        continue;
                    named++;
                    if (string.IsNullOrEmpty(name)) {
                        blanks++;
                        AddIssue("flat-list", $"{relative}: entry {(type - 1) * 1000 + local} has no name");
                    }
                }
            }
            _write($"- {relative}: {named} named entr{(named == 1 ? "y" : "ies")}, {blanks} blank");
        }
    }

    private void CheckCommandParameters() {
        var definitions = new Dictionary<int, HashSet<string>>();
        var commandDir = Path.Combine(_jsonRoot, "custom", "cmd");
        if (Directory.Exists(commandDir))
            foreach (var file in Directory.GetFiles(commandDir, "cmd*.json")) {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file)["cmd".Length..], out var id))
                    continue;
                var names = new HashSet<string>(StringComparer.Ordinal);
                CollectAttributeNames(LoadNode(file), names);
                definitions[id] = names;
            }

        var instances = 0;
        foreach (var file in _jsonFiles) {
            if (new FileInfo(file).Length > 4 * 1024 * 1024)
                continue;
            instances += CheckCommandNode(LoadNode(file), Relative(file), definitions);
        }

        _write($"- command definitions: {definitions.Count}, instances checked: {instances}");
    }

    private int CheckCommandNode(JsonNode? node, string file, Dictionary<int, HashSet<string>> definitions) {
        var instances = 0;
        switch (node) {
            case JsonArray array:
                if (array.Count >= 2 && array[0] is JsonValue value && value.TryGetValue<int>(out var id)
                    && definitions.TryGetValue(id, out var known) && array[1] is JsonObject parameters) {
                    instances++;
                    foreach (var pair in parameters)
                        if (pair.Key != "___cmdID" && !known.Contains(pair.Key) && GeneratedAttributeId.IsMatch(pair.Key))
                            AddIssue("command-param", $"{file}: command {id} uses attribute {pair.Key}, which its definition does not declare");
                }
                foreach (var child in array)
                    instances += CheckCommandNode(child, file, definitions);
                break;
            case JsonObject obj:
                foreach (var pair in obj)
                    instances += CheckCommandNode(pair.Value, file, definitions);
                break;
        }
        return instances;
    }

    // ---------------------------------------------------------------- helpers

    private void AddIssue(string kind, string detail) => _issues.Add($"[{kind}] {detail}");

    private string Relative(string path) => path.StartsWith(_jsonRoot, StringComparison.Ordinal)
        ? path[(_jsonRoot.Length + 1)..]
        : path;

    /// <summary>Name of the list entry for a global item id, following GameListData.</summary>
    private static string? NameOf(JsonObject list, int id) {
        if (id <= 0)
            return null;
        var type = (id - 1) / 1000 + 1;
        var local = (id - 1) % 1000 + 1;
        return list[type.ToString()] is JsonArray array && local < array.Count
            ? array[local]?.GetValue<string>()
            : null;
    }

    private static IEnumerable<(int Type, int Local, string? Name)> Slots(JsonObject list) {
        foreach (var pair in list) {
            if (!int.TryParse(pair.Key, out var type) || pair.Value is not JsonArray array)
                continue;
            for (var local = 0; local < array.Count; local++)
                yield return (type, local, array[local]?.GetValue<string>());
        }
    }

    private static void CollectAttributeNames(JsonNode? node, HashSet<string> names) {
        switch (node) {
            case JsonArray array:
                foreach (var child in array)
                    CollectAttributeNames(child, names);
                break;
            case JsonObject obj:
                foreach (var pair in obj) {
                    if (pair.Key == "attr" && pair.Value is JsonObject attribute) {
                        if (attribute["id"]?.GetValue<string>() is { } id)
                            names.Add(id);
                        if (attribute["varName"]?.GetValue<string>() is { } varName)
                            names.Add(varName);
                    }
                    CollectAttributeNames(pair.Value, names);
                }
                break;
        }
    }

    private static SortedSet<int> Harvest(string[] files, string pattern) {
        var regex = new Regex(pattern, RegexOptions.Compiled);
        var ids = new SortedSet<int>();
        foreach (var file in files)
            foreach (Match match in regex.Matches(File.ReadAllText(file)))
                if (int.TryParse(match.Groups[1].Value, out var value) && value > 0)
                    ids.Add(value);
        return ids;
    }

    private SortedSet<int>[] ReadSaveVariableIds() {
        SortedSet<int>[] result = [[], [], []];
        var saveDir = Path.Combine(_projectPath, "savedata");
        if (!Directory.Exists(saveDir))
            return result;
        string[] arrayNames = ["variables", "switchs", "strings"];
        foreach (var file in Directory.GetFiles(saveDir, "*.gcdata")) {
            JsonNode? variable;
            try {
                variable = JsonNode.Parse(File.ReadAllText(file))?["variable"];
            } catch (Exception) {
                continue;
            }
            if (variable is null)
                continue;
            for (var i = 0; i < arrayNames.Length; i++) {
                if (variable[arrayNames[i]] is not JsonArray array)
                    continue;
                for (var id = 0; id < array.Count; id++)
                    if (array[id] is not null)
                        result[i].Add(id);
            }
        }
        return result;
    }

    private static JsonNode? LoadNode(string path) {
        try {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;
        } catch (Exception) {
            return null;
        }
    }

    private static JsonObject? LoadObject(string path) => LoadNode(path) as JsonObject;
}
