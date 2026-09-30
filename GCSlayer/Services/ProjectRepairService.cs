using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CliFx.Infrastructure;

namespace GCSlayer.Services;

/// <summary>
/// Repairs the list data of a recovered project.
///
/// The GameCreator release pipeline (IDEProjectRelease.onReleaseGameComplete in the core
/// template) deletes every editor-only list file from the released build, so a recovered
/// project only gets the template stub that GCSlayer copies from file_repo. This service
/// rebuilds what the released data still allows to derive:
///
///  - per item lists (animation / avatar / standing avatar) are rebuilt from their data files,
///    which the release keeps and which carry the item ids;
///  - custom/customGameAttribute.json is reassembled from the four attribute files the release
///    splits it into (customWorldData / customWorldSetting / customPlayerData / customPlayerSetting);
///  - variable lists are rebuilt from the ids referenced by "set variable" commands
///    (CommandExecute.precompile_13) and from the ids stored in the project save file;
///  - remaining editor-only lists are padded to the length their references require.
///
/// Names that never made it into the release are replaced with deterministic stubs so that the
/// IDE shows an entry instead of an empty/undefined one.
/// </summary>
public class ProjectRepairService {
    private readonly Func<string, Task> _write;
    private readonly List<string> _report = [];

    /// <summary>Writes progress to a CliFx console.</summary>
    public ProjectRepairService(IConsole console) : this(message => console.Output.WriteLineAsync(message)) { }

    /// <summary>Writes progress through an arbitrary sink, which keeps the service testable.</summary>
    public ProjectRepairService(Func<string, Task> write) => _write = write;

    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>kind -> list file, see CommandExecute.precompile_13 resultSetTypes.</summary>
    private static readonly string[] VariableListFiles = [
        "server/variable/variable.json",
        "server/variable/switch.json",
        "server/variable/string.json",
        "variable/variable.json",
        "variable/switch.json",
        "variable/string.json"
    ];

    /// <summary>[13, resultType, varID, symbol, valueType, ...] is the "set variable" command.</summary>
    private static readonly Regex SetVariableCommand = new(
        @"\[\s*13\s*,\s*([0-6])\s*,\s*(\d+)\s*,", RegexOptions.Compiled);

    /// <summary>
    /// The editor compiles every custom condition/number/string/behavior into the game script as
    /// CustomX["f" + id]; those functions are the only surviving record of the extension ids.
    /// </summary>
    private static readonly Regex ScriptFunctionDefinition = new(
        @"(Custom[A-Za-z]+)\s*\.\s*f(\d+)\s*=", RegexOptions.Compiled);

    private static readonly Regex ScriptFunctionReference = new(
        @"(Custom[A-Za-z]+)\[""f""\s*\+\s*(\d+)", RegexOptions.Compiled);

    private string _projectPath = "";
    private string _jsonRoot = "";
    /// <summary>What every command parameter means, extracted from the game's own editor script.</summary>
    private CommandParameterTypes _parameterTypes = CommandParameterTypes.Empty;

    public async Task RepairAsync(string projectPath) {
        _projectPath = Path.GetFullPath(projectPath);
        _jsonRoot = Path.Combine(_projectPath, "asset", "json");
        if (!Directory.Exists(_jsonRoot))
            throw new DirectoryNotFoundException($"asset/json not found, not a project: {_projectPath}");

        var jsonFiles = Directory.GetFiles(_jsonRoot, "*.json", SearchOption.AllDirectories);
        await _write($"Repair lists of {_projectPath} ({jsonFiles.Length} json files)");

        _parameterTypes = CommandParameterTypes.Build(_projectPath);
        RepairItemLists(jsonFiles);
        ReconstructCustomGameAttribute();
        RepairVariableLists(jsonFiles);
        RepairFlatLists(jsonFiles);
        RepairExtensionLists(jsonFiles);
        StripGeneratedParameterApi();
        ReportParameterTypes();

        foreach (var line in _report)
            await _write(line);
    }

    /// <summary>
    /// One factual line about the command parameter table. Without the editor script every other
    /// source still works, so this only reports what the script was able to type.
    /// </summary>
    private void ReportParameterTypes() {
        if (_parameterTypes.Available)
            _report.Add($"- command parameters: {_parameterTypes.Commands} command(s) yield "
                + $"{_parameterTypes.TypedParameters} typed parameter slot(s) "
                + $"({_parameterTypes.VariableTypings} variable reference(s), "
                + $"{_parameterTypes.CustomTypings} custom condition/number/string reference(s)) "
                + $"from {_parameterTypes.ScriptPath}");
        else
            _report.Add($"- warn: no command parameter table ({_parameterTypes.ScriptPath}: "
                + $"{_parameterTypes.Error}); variables stay limited to the set-variable command and the save file");
    }

    /// <summary>
    /// Drops the compiled copies of Game/system/*Params.ts from the game script: the IDE regenerates
    /// that file from the extension documents, so a bundle built from the documents the release lost
    /// only carries stale classes.
    /// </summary>
    private void StripGeneratedParameterApi() {
        var scriptPath = Path.Combine(_projectPath, "Game", "GCMain.ts");
        if (!File.Exists(scriptPath))
            return;

        var families = GeneratedParameterApiStripper.ReadParameterApiPrefixes(_projectPath);
        if (families.Count == 0) {
            _report.Add("- skip generated parameter API: the editor declares no parameter API file");
            return;
        }

        var script = File.ReadAllText(scriptPath);
        var (stripped, removed, skipped) = GeneratedParameterApiStripper.Strip(script, families);
        if (removed == 0) {
            _report.Add($"- generated parameter API: nothing to remove "
                + $"({families.Count} family(ies), all referenced or absent)");
            return;
        }
        File.WriteAllText(scriptPath, stripped);
        _report.Add($"- Game/GCMain.ts: removed {removed} compiled parameter API class(es) "
            + $"of {families.Count} editor generated family(ies)"
            + (skipped.Count == 0 ? "" : $", kept {skipped.Count} that are still referenced"));
    }

    // ---------------------------------------------------------------- item lists

    private void RepairItemLists(string[] jsonFiles) {
        RepairItemList(jsonFiles, "animation", "ani", "animationList.json",
            [@"\""[Aa]nimation(?:ID|Id|id)\""\s*:\s*(\d+)"]);
        RepairItemList(jsonFiles, "avatar", "avatar", "avatarList.json",
            [@"\""avatarID\""\s*:\s*(\d+)"]);
        RepairItemList(jsonFiles, "standAvatar", "standAvatar", "standAvatarList.json",
            [@"\""[Ss]tandAvatarID\""\s*:\s*(\d+)"]);
    }

    /// <summary>
    /// Rebuilds a type indexed list ({"list":{"type":[null,name...]}}) from the data files that
    /// back it plus the ids the shipped data still points at. Item ids follow
    /// GameListData.getID/getType/getLocalID, i.e. an item file "&lt;prefix&gt;&lt;id&gt;.json"
    /// belongs to type (id-1)/1000+1 at index (id-1)%1000+1. Slots without an item stay null, so
    /// the engine skips them instead of failing to load a document that never existed. Names are
    /// lost in the release, so the item id becomes the stub name.
    /// </summary>
    private void RepairItemList(string[] jsonFiles, string folder, string prefix, string listName,
        string[] referencePatterns) {
        var listPath = Path.Combine(_jsonRoot, folder, listName);
        var dataDir = Path.Combine(_jsonRoot, folder, "data");
        if (!Directory.Exists(dataDir)) {
            _report.Add($"- skip {folder}/{listName}: no data directory");
            return;
        }

        var fileRegex = new Regex("^" + Regex.Escape(prefix) + @"(\d+)$", RegexOptions.IgnoreCase);
        var ids = new SortedSet<int>();
        foreach (var file in Directory.GetFiles(dataDir, "*.json")) {
            var match = fileRegex.Match(Path.GetFileNameWithoutExtension(file));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var id) && id > 0)
                ids.Add(id);
        }

        // referenced ids without a document: recreate an empty document so the IDE can load them
        var created = 0;
        foreach (var pattern in referencePatterns) {
            foreach (var id in HarvestIds(jsonFiles, pattern)) {
                if (id <= 0 || ids.Contains(id))
                    continue;
                var stub = BuildItemStub(folder, id);
                if (stub is null)
                    continue;
                Save(Path.Combine(dataDir, prefix + id + ".json"), stub);
                ids.Add(id);
                created++;
            }
        }

        var root = LoadObject(listPath) ?? new JsonObject();
        var listObj = new JsonObject();
        var typeObj = new JsonObject();
        foreach (var group in ids.GroupBy(id => (id - 1) / 1000 + 1).OrderBy(g => g.Key)) {
            var type = group.Key;
            var maxLocal = group.Max(id => (id - 1) % 1000 + 1);
            var set = group.ToHashSet();
            var array = new JsonArray { null };
            for (var local = 1; local <= maxLocal; local++) {
                var id = (type - 1) * 1000 + local;
                array.Add(set.Contains(id) ? JsonValue.Create(id.ToString()) : null);
            }
            listObj[type.ToString()] = array;
            typeObj[type.ToString()] = type.ToString();
        }

        root["list"] = listObj;
        root["type"] = typeObj;
        Save(listPath, root);
        _report.Add(created == 0
            ? $"- {folder}/{listName}: {ids.Count} item(s) -> {listObj.Count} type(s)"
            : $"- {folder}/{listName}: {ids.Count} item(s) -> {listObj.Count} type(s), {created} empty document(s) created for referenced ids");
    }

    /// <summary>Empty document for an item the game still references, copied from a real one.</summary>
    private JsonObject? BuildItemStub(string folder, int id) {
        if (folder == "animation") {
            return new JsonObject {
                ["totalFrame"] = 0,
                ["imageSources"] = new JsonArray { null },
                ["layers"] = new JsonArray(),
                ["isParticle"] = false,
                ["id"] = id
            };
        }

        var dataDir = Path.Combine(_jsonRoot, folder, "data");
        var smallest = Directory.GetFiles(dataDir, "*.json")
            .OrderBy(file => new FileInfo(file).Length)
            .FirstOrDefault();
        if (smallest is null || LoadObject(smallest) is not JsonObject template)
            return null;
        var stub = (JsonObject)template.DeepClone();
        stub["id"] = id;
        return stub;
    }

    // ---------------------------------------------------------------- custom attributes

    /// <summary>
    /// The editor keeps every custom attribute in custom/customGameAttribute.json while the
    /// release writes the same data as four separate files. Reassemble the editor file so the
    /// attribute names (and blocks) the author gave are back.
    /// </summary>
    private void ReconstructCustomGameAttribute() {
        var target = Path.Combine(_jsonRoot, "custom", "customGameAttribute.json");
        (string Key, string FileName)[] parts = [
            ("worldAttributeSetting", "customWorldSetting.json"),
            ("playerAttributeSetting", "customPlayerSetting.json"),
            ("worldAttributeConfig", "customWorldData.json"),
            ("playerAttributeConfig", "customPlayerData.json")
        ];

        var result = new JsonObject();
        foreach (var (key, fileName) in parts) {
            var node = LoadNode(Path.Combine(_jsonRoot, "custom", fileName));
            if (node is null) {
                _report.Add($"- skip custom/customGameAttribute.json: {fileName} missing");
                return;
            }
            result[key] = node.DeepClone();
        }

        if (LoadNode(target) is JsonObject old && old["k1"] is not null)
            result["k1"] = old["k1"]!.DeepClone();

        Save(target, result);
        _report.Add("- custom/customGameAttribute.json: rebuilt from the 4 released attribute files");
    }

    // ---------------------------------------------------------------- variables

    /// <summary>
    /// Collects the variable ids the shipped data still references and extends the six lists with the
    /// slots those ids need.
    ///
    /// Three sources, in the order the data can be trusted:
    ///
    ///  1. the "set variable" command <c>[13, resultType, varID, …]</c>, whose kind is the result type
    ///     the instance itself stores;
    ///  2. the save file, which keeps one slot per id and does not separate world from player;
    ///  3. every other command parameter the editor script types as a variable reference (see
    ///     <see cref="CommandParameterTypes"/>): the "if" command keeps a world and a player id in the
    ///     same slot, the wait/camera/behavior commands read a player id, and so on. Those references
    ///     are explicitly world or player, so they are added to the list the script names and not to
    ///     its counterpart; only the save file, which cannot tell the two apart, is mirrored.
    ///
    /// The lists are only ever extended: an existing entry keeps its name and its position, and no
    /// slot is dropped, so a project that already had names (or entries the release kept) survives.
    /// </summary>
    private void RepairVariableLists(string[] jsonFiles) {
        // one set per list file, and one per source so the report can name them
        var fromSetVariable = NewSets();
        var fromSaveFile = NewSets();
        var fromOtherCommands = NewSets();

        foreach (var file in jsonFiles) {
            var text = File.ReadAllText(file);
            foreach (Match match in SetVariableCommand.Matches(text)) {
                var kind = int.Parse(match.Groups[1].Value);
                var id = int.Parse(match.Groups[2].Value);
                if (id > 0)
                    fromSetVariable[kind].Add(id);
            }
        }

        // Save files keep one slot per variable id and do not separate world from player
        // variables, so their ids are added to both sides.
        var saved = ReadSaveVariableIds();
        for (var kind = 0; kind < 3; kind++)
            foreach (var id in saved[kind]) {
                fromSaveFile[kind].Add(id);
                fromSaveFile[kind + 3].Add(id);
            }

        HarvestCommandParameterVariables(jsonFiles, fromOtherCommands);

        for (var kind = 0; kind < VariableListFiles.Length; kind++) {
            var ids = new SortedSet<int>(fromSetVariable[kind]);
            ids.UnionWith(fromSaveFile[kind]);
            ids.UnionWith(fromOtherCommands[kind]);
            var relative = VariableListFiles[kind];
            var path = Path.Combine(_jsonRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var (before, after) = ExtendVariableList(path, ids);
            _report.Add($"- {relative}: {ids.Count} id(s), length {before} -> {after}");
        }

        _report.Add($"- variables: {Total(fromSetVariable)} id(s) from the set-variable command, "
            + $"{Total(fromSaveFile)} from the save file, {Total(fromOtherCommands)} from other command parameters");
    }

    private static SortedSet<int>[] NewSets() {
        var sets = new SortedSet<int>[VariableListFiles.Length];
        for (var i = 0; i < sets.Length; i++)
            sets[i] = [];
        return sets;
    }

    private static int Total(SortedSet<int>[] sets) => sets.Sum(set => set.Count);

    /// <summary>
    /// Adds the ids of every command parameter the script types as a variable reference. A parameter
    /// can hold a union (the "if" command stores a constant, a world id and a player id in one array),
    /// so each typing carries the path inside the parameter value it applies to, and a guard for the
    /// slots the engine only reads as an id while a mode flag says so (the wait command stores a
    /// duration in the same slot it reads as a variable id).
    /// </summary>
    private void HarvestCommandParameterVariables(string[] jsonFiles, SortedSet<int>[] ids) {
        if (!_parameterTypes.Available)
            return;
        foreach (var file in jsonFiles) {
            JsonNode? root;
            try {
                root = JsonNode.Parse(File.ReadAllText(file));
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException) {
                continue;
            }
            CommandPages.Walk(root, parameter => {
                foreach (var slot in _parameterTypes.VariableSlots(parameter.CommandId, parameter.Index)) {
                    var kind = (int)slot.Kind;
                    // The scene object switch has no editor list of its own.
                    if (kind >= ids.Length || kind == (int)VariableKind.SceneObjectSwitch)
                        continue;
                    if (!GuardHolds(parameter.Instance, slot.Guard))
                        continue;
                    foreach (var id in ReadIds(parameter.Value, slot.Path))
                        ids[kind].Add(id);
                }
            });
        }
    }

    /// <summary>Whether the shipped instance satisfies the condition the script puts on a slot.</summary>
    private static bool GuardHolds(JsonArray instance, VariableGuard? guard) {
        if (guard is null)
            return true;
        if (guard.Path.Count == 0 || guard.Path[0].Index is not { } index || index < 0)
            return false;
        // A parameter the instance does not carry is undefined in the engine, which is falsy.
        var node = index < CommandPages.ParameterCount(instance) ? instance[index + 1] : null;
        for (var step = 1; step < guard.Path.Count; step++)
            node = Read(node, guard.Path[step]);
        return guard.Operator switch {
            VariableGuardOperator.Truthy => IsTruthy(node),
            VariableGuardOperator.Falsy => !IsTruthy(node),
            VariableGuardOperator.Equal => Number(node) == guard.Value,
            VariableGuardOperator.NotEqual => Number(node) != guard.Value,
            VariableGuardOperator.LessThan => Number(node) is { } less && less < guard.Value,
            VariableGuardOperator.LessThanOrEqual => Number(node) is { } lessEqual && lessEqual <= guard.Value,
            VariableGuardOperator.GreaterThan => Number(node) is { } greater && greater > guard.Value,
            _ => Number(node) is { } greaterEqual && greaterEqual >= guard.Value,
        };
    }

    private static long? Number(JsonNode? node) => node switch {
        JsonValue value when value.TryGetValue(out int number) => number,
        JsonValue value when value.TryGetValue(out bool boolean) => boolean ? 1 : 0,
        _ => null,
    };

    private static bool IsTruthy(JsonNode? node) => node switch {
        null => false,
        JsonObject => true,
        JsonArray array => array.Count > 0,
        JsonValue value when value.TryGetValue(out bool boolean) => boolean,
        JsonValue value when value.TryGetValue(out int number) => number != 0,
        JsonValue value when value.TryGetValue(out double number) => number != 0,
        JsonValue value when value.TryGetValue(out string? text) => text is { Length: > 0 },
        _ => true,
    };

    private static JsonNode? Read(JsonNode? node, ParameterStep step) => node switch {
        JsonArray array when step.Index is { } index && index >= 0 && index < array.Count => array[index],
        JsonObject json when step.Member is { } member && json.ContainsKey(member) => json[member],
        _ => null,
    };

    /// <summary>
    /// The ids a parameter value holds at a path, skipping everything that cannot be a slot index
    /// (the editor fills unused alternatives with constants, 0 or null).
    /// </summary>
    private static IEnumerable<int> ReadIds(JsonNode? value, IReadOnlyList<ParameterStep> path) {
        var node = value;
        foreach (var step in path) {
            node = Read(node, step);
            if (node is null)
                yield break;
        }
        if (node is JsonValue scalar && scalar.TryGetValue(out int id) && id > 0)
            yield return id;
    }

    /// <summary>
    /// Extends one typed variable list with a slot for every id, keeping every entry the file already
    /// had: the names the author or the repository provided, and the type groups and type values the
    /// project already uses. A slot without a name (the template stub, or a gap below the highest
    /// referenced id) is named after its index inside the type, which is the convention the lists of a
    /// recovered project follow; nothing is ever removed.
    /// </summary>
    /// <returns>The list length in slots before and after.</returns>
    private (int Before, int After) ExtendVariableList(string path, SortedSet<int> ids) {
        var root = LoadObject(path) ?? new JsonObject();
        var list = root["list"] as JsonObject ?? [];
        var types = root["type"] as JsonObject ?? [];
        var before = list.Sum(pair => (pair.Value as JsonArray)?.Count ?? 0);

        // The highest referenced id per type decides how far its list has to reach.
        var needed = new SortedDictionary<int, int>();
        foreach (var id in ids.Where(id => id > 0)) {
            var type = (id - 1) / 1000 + 1;
            var local = (id - 1) % 1000 + 1;
            needed[type] = Math.Max(needed.TryGetValue(type, out var highest) ? highest : 0, local);
        }
        foreach (var (type, local) in needed) {
            var key = type.ToString();
            if (list[key] is not JsonArray array) {
                array = new JsonArray { null };
                list[key] = array;
            }
            while (array.Count <= local)
                array.Add(null);
        }

        foreach (var pair in list) {
            if (pair.Value is not JsonArray array)
                continue;
            for (var index = 1; index < array.Count; index++)
                if (string.IsNullOrEmpty(array[index]?.GetValue<string>()))
                    array[index] = JsonValue.Create(index.ToString());
            if (!types.ContainsKey(pair.Key))
                types[pair.Key] = pair.Key;
        }

        root["list"] = list;
        root["type"] = types;
        Save(path, root);
        var after = list.Sum(pair => (pair.Value as JsonArray)?.Count ?? 0);
        return (before, after);
    }

    /// <summary>Reads the variable/switch/string id slots of every plain text save file.</summary>
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
            } catch (JsonException) {
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

    // ---------------------------------------------------------------- flat lists

    private void RepairFlatLists(string[] jsonFiles) {
        var all = jsonFiles;
        var avatarData = jsonFiles.Where(UnderFolder("avatar/data")).ToArray();
        var standAvatarData = jsonFiles.Where(UnderFolder("standAvatar/data")).ToArray();

        RepairFlatList("avatar/avatarActList.json", HarvestMax(all, @"""avatarAct""\s*:\s*(\d+)"));
        RepairFlatList("animation/animationSignalList.json", HarvestMax(all, @"""signalId""\s*:\s*(\d+)"));
        RepairFlatList("avatar/avatarPartList.json", HarvestMax(avatarData, @"""partID""\s*:\s*(\d+)"));
        RepairFlatList("standAvatar/standAvatarPartList.json", HarvestMax(standAvatarData, @"""partID""\s*:\s*(\d+)"));

        // Custom conditions/numbers/strings/behaviours are handled by RepairExtensionLists below.
        RepairFlatList("standAvatar/expressionList.json", 0);

        Func<string, bool> UnderFolder(string folder) => path => {
            var normalized = path.Replace('\\', '/');
            return normalized.Contains("/" + folder + "/", StringComparison.OrdinalIgnoreCase);
        };
    }

    // ---------------------------------------------------------------- editor extensions

    /// <summary>
    /// Rebuilds the editor extension lists (custom conditions, game numbers, game strings, custom
    /// value functions and behaviours). Their editor files are deleted on release together with the
    /// folders that hold the per item documents, so the ids are taken from the recovered game script
    /// and a block document is created for every entry: without it the IDE drops the entry while
    /// loading, and without the parameters of its compiled function the entry has a block that
    /// cannot be edited.
    ///
    /// The parameters come from parsing the game script (see <see cref="JavaScriptFunctionScanner"/>),
    /// and their controls from the structure of that script plus the kind the shipped data observed
    /// (see <see cref="ExtensionControlCatalog"/>); a document the author wrote is never touched.
    /// </summary>
    private void RepairExtensionLists(string[] jsonFiles) {
        var scriptPath = Path.Combine(_projectPath, "Game", "GCMain.ts");
        if (!File.Exists(scriptPath)) {
            _report.Add("- skip editor extensions: Game/GCMain.ts not found");
            return;
        }

        var script = File.ReadAllText(scriptPath);
        var functions = new Dictionary<(string Module, int Id), IReadOnlyList<JsParameter>>();
        var ids = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        try {
            foreach (var function in JavaScriptFunctionScanner.Scan(script)) {
                functions[(function.Module, function.Id)] = function.Parameters;
                if (!ids.TryGetValue(function.Module, out var set))
                    ids[function.Module] = set = [];
                set.Add(function.Id);
            }
        }
        catch (Exception exception) when (exception is Acornima.SyntaxErrorException or InvalidOperationException) {
            // The script is the only source of the extension ids, so fall back to the plain scan of
            // the assignment statements rather than dropping the lists.
            _report.Add($"- warn: could not parse Game/GCMain.ts ({exception.Message}); parameters stay empty");
            CollectScriptIds(script, ScriptFunctionDefinition, ids);
            CollectScriptIds(script, ScriptFunctionReference, ids);
        }

        _report.Add($"- extension parameters: {functions.Count} compiled function(s) read, "
            + $"{functions.Sum(pair => pair.Value.Count)} parameter(s): control from the function "
            + "body, else from the shipped data, else the kind default");

        // The shipped calls of these entries still hold the attribute ids of the documents the release
        // deleted; without them the editor would show the default value of every parameter. The command
        // parameter table tells such a call which module it belongs to, which is what separates two
        // documents that happen to share their parameter names.
        var instances = ExtensionInstanceIndex.Build(
            functions.Select(pair => (pair.Key.Module, pair.Key.Id,
                (IReadOnlyList<string>)pair.Value.Select(parameter => parameter.Name).ToArray())).ToArray(),
            jsonFiles, _parameterTypes);
        _report.Add($"- extension attribute ids: {instances.RecoveredIds} recovered from the "
            + "shipped event data (the rest are derived)");

        // module name -> editor list, per item file prefix, list id offset
        RepairExtensionList("CustomCondition", "custom/customCondition.json", "condition/condition", 0, ids, functions, instances);
        RepairExtensionList("CustomGameNumber", "custom/customGameNumberList.json", "gameNumber/num", 0, ids, functions, instances);
        RepairExtensionList("CustomGameString", "custom/customGameStringList.json", "gameString/str", 0, ids, functions, instances);
        RepairExtensionList("CustomValueFunction", "custom/customValueFunction.json", null, -1, ids, functions, instances);
        RepairExtensionList("CustomSceneObjectBehavior", "custom/customBehaviorType.json", "behavior/behavior", 0, ids, functions, instances);
    }

    private static void CollectScriptIds(string script, Regex regex, Dictionary<string, SortedSet<int>> ids) {
        foreach (Match match in regex.Matches(script)) {
            var module = match.Groups[1].Value;
            var id = int.Parse(match.Groups[2].Value);
            if (!ids.TryGetValue(module, out var set))
                ids[module] = set = [];
            set.Add(id);
        }
    }

    private void RepairExtensionList(string module, string listRelativePath, string? dataFilePrefix,
        int listIdOffset, Dictionary<string, SortedSet<int>> scriptIds,
        Dictionary<(string Module, int Id), IReadOnlyList<JsParameter>> functions,
        ExtensionInstanceIndex instances) {
        var ids = new SortedSet<int>();
        if (scriptIds.TryGetValue(module, out var raw))
            foreach (var id in raw) {
                var listId = id + listIdOffset;
                if (listId > 0)
                    ids.Add(listId);
            }

        var listPath = Path.Combine(_jsonRoot, listRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var root = LoadObject(listPath) ?? new JsonObject();
        var previous = (root["list"] as JsonArray)?.Count ?? 0;
        var highest = ids.Count == 0 ? 0 : ids.Max;
        var array = new JsonArray { null };
        for (var id = 1; id <= highest; id++)
            array.Add(ids.Contains(id) ? JsonValue.Create(id.ToString()) : null);
        root["list"] = array;
        Save(listPath, root);

        var written = 0;
        var parameters = 0;
        if (dataFilePrefix is not null) {
            foreach (var id in ids) {
                var dataPath = Path.Combine(_jsonRoot, "custom",
                    (dataFilePrefix + id + ".json").Replace('/', Path.DirectorySeparatorChar));
                // A document the author wrote is never touched; a document this tool generated is
                // rewritten, so that improving the recovery updates what it produced before.
                if (LoadObject(dataPath) is { } existing && IsAuthoredDocument(module, id, existing, instances))
                    continue;
                var functionId = id - listIdOffset;
                var recovered = functions.GetValueOrDefault((module, functionId)) ?? [];
                Save(dataPath, ExtensionControlCatalog.BuildDocument(module, id, recovered, instances));
                written++;
                parameters += recovered.Count;
            }
        }

        _report.Add($"- {listRelativePath}: {ids.Count} id(s), length {previous} -> {array.Count}, "
            + $"{written} document(s) written with {parameters} parameter control(s) (from {module})");
    }

    /// <summary>
    /// True when a document holds anything this tool did not generate: a control without a name, or
    /// with an attribute id other than one this tool would write (the id it derives, or the id the
    /// shipped data says the author used).
    /// </summary>
    private static bool IsAuthoredDocument(string module, int id, JsonObject document,
        ExtensionInstanceIndex instances) {
        if (document["blockList"] is not JsonArray blocks)
            return false;
        foreach (var block in blocks) {
            if (block?["blockAttrs"] is not JsonArray attributes)
                continue;
            foreach (var entry in attributes) {
                if (entry?["attr"] is not JsonObject attr)
                    return true;
                var name = attr["varName"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name))
                    return true;
                var id_ = attr["id"]?.GetValue<string>();
                if (id_ != ExtensionControlCatalog.StableId(module, id, name)
                    && id_ != instances.AttributeId(module, id, name))
                    return true;
            }
        }
        return false;
    }


    private void RepairFlatList(string relativePath, int highestReferencedId) {
        var path = Path.Combine(_jsonRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var root = LoadObject(path) ?? new JsonObject();
        var existing = (root["list"] as JsonArray)?.Count ?? 0;
        var target = Math.Max(existing, highestReferencedId + 1);
        if (target < 1)
            target = 1;
        root["list"] = BuildNameArray(target - 1, index => index.ToString());
        Save(path, root);
        _report.Add($"- {relativePath}: list length {existing} -> {target}");
    }

    private static int HarvestMax(string[] files, string pattern) {
        var regex = new Regex(pattern, RegexOptions.Compiled);
        var max = 0;
        foreach (var file in files) {
            var text = File.ReadAllText(file);
            foreach (Match match in regex.Matches(text)) {
                if (int.TryParse(match.Groups[1].Value, out var value) && value > max)
                    max = value;
            }
        }
        return max;
    }

    private static SortedSet<int> HarvestIds(string[] files, string pattern) {
        var regex = new Regex(pattern, RegexOptions.Compiled);
        var ids = new SortedSet<int>();
        foreach (var file in files) {
            var text = File.ReadAllText(file);
            foreach (Match match in regex.Matches(text)) {
                if (int.TryParse(match.Groups[1].Value, out var value) && value > 0)
                    ids.Add(value);
            }
        }
        return ids;
    }

    // ---------------------------------------------------------------- helpers

    private static JsonArray BuildNameArray(int maxLocalIndex, Func<int, string> nameFor) {
        var array = new JsonArray { null };
        for (var index = 1; index <= maxLocalIndex; index++)
            array.Add(nameFor(index));
        return array;
    }

    private static JsonNode? LoadNode(string path) {
        if (!File.Exists(path))
            return null;
        try {
            return JsonNode.Parse(File.ReadAllText(path));
        } catch (JsonException) {
            return null;
        }
    }

    private static JsonObject? LoadObject(string path) => LoadNode(path) as JsonObject;

    private static void Save(string path, JsonNode node) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(WriteOptions));
    }
}
