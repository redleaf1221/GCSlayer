using System.Text.Json.Nodes;

namespace GCSlayer.Services;

/// <summary>One parameter of one command instance of the project data.</summary>
/// <param name="CommandId">The command id, i.e. the first element of the instance array.</param>
/// <param name="Index">The parameter index, i.e. the JSON index inside the instance minus one.</param>
/// <param name="Value">The parameter value.</param>
/// <param name="Instance">The whole command instance, which the guards of the table refer to.</param>
public readonly record struct CommandParameter(int CommandId, int Index, JsonNode? Value, JsonArray Instance);

/// <summary>
/// Finds the command instances of the project data, which is what links a value in the data to the
/// parameter description <see cref="CommandParameterTypes"/> extracted from the game script.
///
/// A command instance is an array whose first element is the command id and whose remaining elements
/// are the parameters, so the parameter at JSON index <c>i</c> of the instance is <c>params[i - 1]</c>.
/// That was verified against the engine itself, not assumed:
///
///  - <c>CommandPage.prototype.parse</c> builds every command with <c>new Command(cmdType,
///    cmdData.slice(1), …)</c>, so the value at JSON index 1 is <c>cmd.params[0]</c>;
///  - the same function pops the trailing per instance record of a builtin command
///    (<c>Command.getCMDIDByParam</c> reads the last element when it is an object carrying
///    <c>___cmdID</c>), so the last element is not a parameter;
///  - the "set variable" instance <c>[13, resultType, varID, symbol, valueType, …, ___cmdID]</c> is
///    read by <c>CommandExecute.precompile_13</c> as <c>cmd.params[0]</c> = result type,
///    <c>cmd.params[1]</c> = variable id, <c>cmd.params[2]</c> = symbol, <c>cmd.params[3]</c> = value
///    type — exactly the JSON indices 1 to 4;
///  - a custom command instance (id ≥ <c>CommandPage.CUSTOM_COMMAND_START_ID</c> = 10000) is
///    <c>[10002, { attributeID: value, … }, ___cmdID]</c>, and the parser turns <c>cmd.params[0]</c>
///    into the values keyed by the document's attribute ids.
///
/// A page is an array whose elements are all command instances. The engine writes two shapes: a
/// command page directly (<c>{"commands": [ [cmdID, …], … ]}</c>, e.g. ws*.json) and a list of pages
/// per UI component (<c>{"commands": [ [ [cmdID, …] ], … ]}</c>, e.g. sui*.json), plus pages nested in
/// a parameter such as the fragment commands of command 15. Both are found structurally, without
/// depending on a file name; the property name only decides whether an array of short numeric arrays
/// may be a page at all, so that data arrays such as "curveData" are never mistaken for commands.
/// </summary>
public static class CommandPages {
    /// <summary>CommandPage.CUSTOM_COMMAND_START_ID: ids from here on are custom commands.</summary>
    public const int CustomCommandStartId = 10000;

    /// <summary>Property names whose value holds pages or command instances.</summary>
    private static bool DeclaresPages(string name) =>
        name.Contains("ommand", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the node is a command instance and yields its command id.</summary>
    public static bool TryReadCommandId(JsonNode? node, out int commandId) {
        commandId = 0;
        return node is JsonArray { Count: >= 2 } instance
            && instance[0] is JsonValue id
            && id.TryGetValue(out commandId);
    }

    /// <summary>
    /// True when the last element is the per instance record the engine pops
    /// (<c>Command.getCMDIDByParam</c>): an object carrying <c>___cmdID</c>.
    /// </summary>
    public static bool HasInstanceInfo(JsonArray instance) =>
        instance.Count > 0 && instance[^1] is JsonObject record && record.ContainsKey("___cmdID");

    /// <summary>Number of <c>cmd.params</c> entries the engine forms for a command instance.</summary>
    public static int ParameterCount(JsonArray instance) {
        if (instance.Count == 0)
            return 0;
        var custom = instance[0] is JsonValue value && value.TryGetValue(out int commandId)
            && commandId >= CustomCommandStartId;
        var count = !custom && HasInstanceInfo(instance) ? instance.Count - 1 : instance.Count;
        return Math.Max(0, count - 1);
    }

    /// <summary>
    /// True when the array holds command instances. <paramref name="declared"/> is set when the array
    /// is the value of a "…command…" property (or an element of such an array), where the editor writes
    /// pages even when an instance carries no object.
    /// </summary>
    public static bool IsPage(JsonArray array, bool declared) {
        if (array.Count == 0)
            return false;
        var carriesObject = false;
        foreach (var element in array) {
            if (element is not JsonArray instance || instance.Count < 2)
                return false;
            if (instance[0] is not JsonValue first || !first.TryGetValue(out int _))
                return false;
            if (!carriesObject) {
                for (var i = 1; i < instance.Count; i++)
                    if (instance[i] is JsonObject) {
                        carriesObject = true;
                        break;
                    }
            }
        }
        // Every command instance the editor writes carries the object of its per instance id, which
        // keeps plain numeric arrays (curve points, coordinates) out of the walk.
        return carriesObject || declared;
    }

    /// <summary>
    /// Walks the document: <paramref name="onParameter"/> receives every parameter of every command
    /// instance; <paramref name="onCall"/> receives every custom value call
    /// <c>[functionID, { parameter values }, [ { attribute ids } ]]</c> with the command id and the
    /// parameter index of the slot it sits in, or nulls when it sits outside a command instance (the
    /// index then falls back to matching the parameter names alone).
    /// </summary>
    public static void Walk(JsonNode? root, Action<CommandParameter> onParameter,
        Action<JsonArray, int?, int?>? onCall = null) {
        Visit(root, null, null, false, onParameter, onCall);
    }

    private static void Visit(JsonNode? node, int? commandId, int? parameterIndex, bool declared,
        Action<CommandParameter> onParameter, Action<JsonArray, int?, int?>? onCall) {
        switch (node) {
            case JsonObject json:
                foreach (var pair in json)
                    Visit(pair.Value, commandId, parameterIndex, DeclaresPages(pair.Key), onParameter, onCall);
                break;

            // "commands" of a UI component holds one page per element; the elements are the pages.
            case JsonArray holders when declared && !IsPage(holders, true):
                foreach (var child in holders)
                    Visit(child, commandId, parameterIndex, true, onParameter, onCall);
                break;

            case JsonArray page when IsPage(page, declared):
                foreach (var element in page) {
                    if (element is not JsonArray { Count: >= 2 } instance)
                        continue;
                    var id = instance[0]!.GetValue<int>();
                    // The engine pops the trailing per instance record of a builtin command before it
                    // forms cmd.params, so that element is not a parameter.
                    var count = ParameterCount(instance);
                    for (var index = 0; index < count; index++) {
                        onParameter(new CommandParameter(id, index, instance[index + 1], instance));
                        Visit(instance[index + 1], id, index, false, onParameter, onCall);
                    }
                }
                break;

            // A custom value call: the two records line up positionally, and a nested call belongs to
            // the parameters of the extension document, not to the command slot that holds this one.
            case JsonArray { Count: 3 } call
                when call[0] is JsonValue functionId && functionId.TryGetValue(out int _)
                    && call[1] is JsonObject
                    && call[2] is JsonArray { Count: > 0 } records
                    && records[0] is JsonObject:
                onCall?.Invoke(call, commandId, parameterIndex);
                Visit(call[1], null, null, false, onParameter, onCall);
                Visit(call[2], null, null, false, onParameter, onCall);
                break;

            case JsonArray array:
                foreach (var child in array)
                    Visit(child, commandId, parameterIndex, false, onParameter, onCall);
                break;
        }
    }
}
