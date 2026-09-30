using System.Text.Json.Nodes;

namespace GCSlayer.Services;

/// <summary>
/// Recovers the attribute ids of the author's extension documents from the shipped event data.
///
/// A call of a custom condition/number/string is stored as
/// <c>[functionID, { varName: value, … }, [ { attributeID: value, … } ]]</c>:
/// the editor writes the values twice, once keyed by the parameter name (the object the compiled
/// function receives as <c>p</c>) and once keyed by the attribute id of the document, and it *reads*
/// the second one. The documents were deleted on release, so the ids are only left in that record;
/// deriving the ids from the script instead would leave every existing value showing its default.
///
/// Both objects are written by the same loop, so their entries line up: the n-th record key is the
/// id of the n-th parameter. Instances are collected as votes per document, and an id is only
/// accepted when every instance of that document agrees.
///
/// Which document a call belongs to is decided by the command parameter table when the script
/// describes the slot the call sits in: the "if" command stores a custom condition in
/// <c>cmd.params[15]</c> and a custom number in <c>cmd.params[16]</c>, so a call found there is a
/// CustomCondition / CustomGameNumber call even though two documents of different modules may declare
/// the very same parameter names. Calls the table cannot classify, and calls outside a command
/// instance, keep the parameter name match as their only evidence, and an ambiguous vote is dropped.
/// </summary>
public sealed class ExtensionInstanceIndex {
    private readonly Dictionary<(string Module, int Id), Dictionary<string, string>> _ids = new();
    private readonly Dictionary<(string Module, int Id), Dictionary<string, JsParameterKind>> _kinds = new();

    /// <summary>Number of parameters whose original attribute id was recovered.</summary>
    public int RecoveredIds => _ids.Values.Sum(ids => ids.Count);

    /// <summary>The attribute id the author's document used, or null when the data cannot tell.</summary>
    public string? AttributeId(string module, int id, string varName) =>
        _ids.TryGetValue((module, id), out var ids) && ids.TryGetValue(varName, out var found) ? found : null;

    /// <summary>The kind of value the shipped data holds, which is direct evidence.</summary>
    public JsParameterKind? ObservedKind(string module, int id, string varName) =>
        _kinds.TryGetValue((module, id), out var kinds) && kinds.TryGetValue(varName, out var found) ? found : null;

    /// <summary>Collects the instances of every candidate document.</summary>
    /// <param name="documents">The recovery candidates: module, entry id and the parameter names.</param>
    /// <param name="jsonFiles">Every json file of the project.</param>
    /// <param name="parameterTypes">
    /// Command parameter table of the project's editor script; a call in a slot it types as a custom
    /// condition/number/string is attributed to that module.
    /// </param>
    public static ExtensionInstanceIndex Build(
        IReadOnlyList<(string Module, int Id, IReadOnlyList<string> Parameters)> documents,
        IEnumerable<string> jsonFiles,
        CommandParameterTypes? parameterTypes = null) {
        var candidates = documents
            .Select(document => (document.Module, document.Id,
                Set: new HashSet<string>(document.Parameters, StringComparer.Ordinal)))
            .ToArray();
        // Modules the recovery knows a document list for; the table is only allowed to restrict the
        // candidates to one of them, anything else falls back to the parameter name match.
        var modules = candidates.Select(candidate => candidate.Module).ToHashSet(StringComparer.Ordinal);

        var idVotes = new Dictionary<(string Module, int Id, string Name), HashSet<string>>();
        var kindVotes = new Dictionary<(string Module, int Id, string Name), HashSet<JsParameterKind>>();
        foreach (var file in jsonFiles) {
            JsonNode? root;
            try {
                root = JsonNode.Parse(File.ReadAllText(file));
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException) {
                continue;
            }
            CommandPages.Walk(root,
                _ => { },
                (call, commandId, parameterIndex) => {
                    if (Read(call[1]!.AsObject(), call[2]!.AsArray()[0]!.AsObject()) is not { } instance)
                        return;
                    var module = commandId is { } id && parameterIndex is { } index
                        ? parameterTypes?.CustomModule(id, index)
                        : null;
                    Vote(instance, candidates,
                        module is not null && modules.Contains(module) ? module : null,
                        idVotes, kindVotes);
                });
        }

        var index = new ExtensionInstanceIndex();
        foreach (var (key, votes) in idVotes) {
            if (votes.Count != 1)
                continue;
            if (!index._ids.TryGetValue((key.Module, key.Id), out var ids))
                index._ids[(key.Module, key.Id)] = ids = new Dictionary<string, string>(StringComparer.Ordinal);
            ids[key.Name] = votes.First();
        }
        foreach (var (key, votes) in kindVotes) {
            if (votes.Count != 1)
                continue;
            if (!index._kinds.TryGetValue((key.Module, key.Id), out var kinds))
                index._kinds[(key.Module, key.Id)] = kinds = new Dictionary<string, JsParameterKind>(StringComparer.Ordinal);
            kinds[key.Name] = votes.First();
        }
        return index;
    }

    private sealed record Instance(List<string> Names, List<string> Ids, List<JsonNode?> Values);

    /// <summary>Pairs the parameter names with the record ids, which line up positionally.</summary>
    private static Instance? Read(JsonObject parameters, JsonObject record) {
        var names = new List<string>();
        var values = new List<JsonNode?>();
        foreach (var pair in parameters) {
            names.Add(pair.Key);
            values.Add(pair.Value);
        }
        var ids = new List<string>();
        foreach (var pair in record)
            ids.Add(pair.Key);
        if (names.Count == 0 || names.Count != ids.Count)
            return null;
        return new Instance(names, ids, values);
    }

    private static void Vote(Instance instance,
        (string Module, int Id, HashSet<string> Set)[] candidates,
        string? module,
        Dictionary<(string Module, int Id, string Name), HashSet<string>> idVotes,
        Dictionary<(string Module, int Id, string Name), HashSet<JsParameterKind>> kindVotes) {
        var names = new HashSet<string>(instance.Names, StringComparer.Ordinal);
        foreach (var candidate in candidates) {
            // A classified call only votes for documents of the module its slot means; an
            // unclassified one keeps matching on the parameter names alone.
            if (module is not null && candidate.Module != module)
                continue;
            if (!candidate.Set.SetEquals(names))
                continue;
            for (var i = 0; i < instance.Names.Count; i++) {
                var key = (candidate.Module, candidate.Id, instance.Names[i]);
                if (!idVotes.TryGetValue(key, out var votes))
                    idVotes[key] = votes = new HashSet<string>(StringComparer.Ordinal);
                votes.Add(instance.Ids[i]);

                if (KindOf(instance.Values[i]) is not { } kind)
                    continue;
                if (!kindVotes.TryGetValue(key, out var kinds))
                    kindVotes[key] = kinds = new HashSet<JsParameterKind>();
                kinds.Add(kind);
            }
        }
    }

    /// <summary>The kind of a value found in the shipped data.</summary>
    private static JsParameterKind? KindOf(JsonNode? value) => value switch {
        JsonObject => JsParameterKind.Object,
        JsonArray => JsParameterKind.Array,
        JsonValue json => json.TryGetValue(out bool _) ? JsParameterKind.Boolean : null,
        _ => null,
    };
}
