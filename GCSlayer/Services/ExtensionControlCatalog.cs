using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace GCSlayer.Services;

/// <summary>One editor control: the varType/compType pair the attribute panel switches on.</summary>
/// <param name="VarType">0 number, 1 string, 2 boolean, 3 data structure, 4 custom module, 5 object.</param>
/// <param name="CompType">Control of <c>EditorDataUICompAttribute.createEditorUIComponent</c>.</param>
/// <param name="CompParam">Control parameters, copied onto the control component.</param>
/// <param name="Alias">
/// Label shown next to the control. Always empty: the tool invents no label and the editor falls
/// back to <c>varName</c> (see <c>CustomAttributeSetting.compName</c>).
/// </param>
/// <param name="ArrayMode">True for a list of values instead of a single value.</param>
public sealed record ExtensionControl(int VarType, int CompType, JsonObject CompParam, string Alias,
    bool ArrayMode = false);

/// <summary>
/// Chooses the editor control of a recovered extension parameter and writes the block document.
///
/// The release deletes the extension documents, so which control the author used for a parameter is
/// lost, and nothing here guesses it from what he called the parameter. Two sources of evidence are
/// left, in this order:
///
///  1. the game script itself (see <see cref="JavaScriptFunctionScanner"/>): a value read through
///     Game.player.variable.getString is a string variable, a value compared against 0..n is a
///     dropdown, a value the function reads fields from is a structure, and so on;
///  2. the shipped data (<see cref="ExtensionInstanceIndex.ObservedKind"/>): what the author
///     actually stored for a value the function body does not constrain (a flag it only forwards,
///     an id) is factual evidence of the kind.
///
/// When neither speaks, the parameter falls back to the engine default of its kind, which is a
/// number unless the function body says otherwise.
/// </summary>
public static class ExtensionControlCatalog {
    /// <summary>Members of the object a "select a UI component" parameter holds.</summary>
    private static readonly HashSet<string> UiComponentMembers =
        new(["uiID", "compName", "compID", "varName", "attrs"], StringComparer.Ordinal);

    /// <summary>Generic "pick a custom data attribute" control: all value types, get and set.</summary>
    private static readonly JsonObject CustomDataParam = new() {
        ["numType"] = true,
        ["strType"] = true,
        ["boolType"] = true,
        ["getSetType"] = 1,
    };

    /// <summary>Resolves the control of one parameter.</summary>
    /// <param name="module">Extension module, e.g. CustomCondition.</param>
    /// <param name="id">Entry id of the module's list.</param>
    /// <param name="parameter">The parameter recovered from the compiled function.</param>
    /// <param name="instances">Shipped calls of this entry, which know what the author stored.</param>
    public static ExtensionControl Resolve(string module, int id, JsParameter parameter,
        ExtensionInstanceIndex? instances = null) {
        // The shipped data holds the value the author actually stored, which is evidence for a value
        // the function body does not constrain (a flag it only forwards, an id).
        if (!parameter.Inferred
            && instances?.ObservedKind(module, id, parameter.Name) is { } observed)
            return Default(parameter with { Kind = observed });

        // Everything else is the engine default of the kind the function body proved. No label is
        // invented: the editor falls back to the parameter name.
        return Default(parameter);
    }

    /// <summary>The control implied by the way the function uses the value.</summary>
    private static ExtensionControl Default(JsParameter parameter) => parameter.Kind switch {
        JsParameterKind.Number => new ExtensionControl(0, 0, [], string.Empty),
        JsParameterKind.NumberVariable => new ExtensionControl(0, 6, [], string.Empty),
        JsParameterKind.String => new ExtensionControl(1, 9, [], string.Empty),
        JsParameterKind.StringVariable => new ExtensionControl(0, 8, [], string.Empty),
        JsParameterKind.SwitchVariable => new ExtensionControl(0, 7, [], string.Empty),
        JsParameterKind.Boolean => new ExtensionControl(2, 15, [], string.Empty),
        JsParameterKind.Enum => new ExtensionControl(0, 2, new JsonObject {
            // The labels are the numbers the function compares against, so the stored value stays
            // readable: the author's own wording for these options is gone.
            ["items"] = string.Join(",", parameter.Options),
            ["showIndex"] = false,
        }, string.Empty),
        JsParameterKind.Object when parameter.Members.Count > 0
            && parameter.Members.All(UiComponentMembers.Contains) => new ExtensionControl(5, 46, [], string.Empty),
        JsParameterKind.Object => new ExtensionControl(5, 55, (JsonObject)CustomDataParam.DeepClone(), string.Empty),
        JsParameterKind.Array => new ExtensionControl(0, 0, [], string.Empty, ArrayMode: true),
        JsParameterKind.NumberReference => new ExtensionControl(0, 36, [], string.Empty),
        JsParameterKind.ConditionReference => new ExtensionControl(0, 50, [], string.Empty),
        JsParameterKind.StringReference => new ExtensionControl(0, 51, [], string.Empty),
        _ => new ExtensionControl(0, 0, [], string.Empty),
    };

    // ---------------------------------------------------------------- documents

    /// <summary>
    /// Builds the extension document of one entry: a single block holding one control per parameter
    /// the compiled function reads. Layout fields (x/y/width/height) only matter to the block
    /// designer and are laid out like real documents do.
    /// </summary>
    public static JsonObject BuildDocument(string module, int id, IReadOnlyList<JsParameter> parameters,
        ExtensionInstanceIndex? instances = null) {
        var attributes = new JsonArray();
        for (var i = 0; i < parameters.Count; i++) {
            var parameter = parameters[i];
            var control = Resolve(module, id, parameter, instances);
            attributes.Add(new JsonObject {
                ["attr"] = BuildAttribute(module, id, parameter.Name, control,
                    instances?.AttributeId(module, id, parameter.Name)),
                ["attrConditions"] = new JsonArray(),
                ["x"] = 15,
                ["y"] = 10 + (i * 40),
                ["width"] = 255,
                ["height"] = 32,
            });
        }

        return new JsonObject {
            ["compAutoOrderLimitHeight"] = 0,
            ["id"] = id,
            ["blockList"] = new JsonArray {
                new JsonObject {
                    ["name"] = "未命名块",
                    ["blockAttrs"] = attributes,
                    ["blockCondition"] = new JsonArray(),
                    ["blockHeight"] = parameters.Count == 0 ? 35 : 300,
                    ["autoOrder"] = true,
                    ["hideBlockTitle"] = false,
                },
            },
            ["isCustomPhraseDesc"] = false,
        };
    }

    /// <summary>
    /// One blockAttrs entry, with the field set real documents carry. The attribute id is the one the
    /// shipped data recorded when the author's document is known, so that the editor finds the values
    /// it stored; otherwise it is derived from the module, the entry and the name.
    /// </summary>
    public static JsonObject BuildAttribute(string module, int id, string varName, ExtensionControl control,
        string? attributeId = null) =>
        new() {
            ["id"] = attributeId ?? StableId(module, id, varName),
            ["varName"] = varName,
            ["varType"] = control.VarType,
            ["compData"] = new JsonObject {
                ["compType"] = control.CompType,
                ["compParam"] = control.CompParam.DeepClone(),
            },
            // The editor lower cases the default value of a boolean, so it must be a string.
            ["defaultValue"] = control.VarType == 2 ? "false" : "",
            ["hideMode"] = false,
            ["useCommand"] = false,
            ["onlyPointTo"] = false,
            ["moduleID"] = 1,
            ["dataStructureID"] = 1,
            ["arrayMode"] = control.ArrayMode,
            ["arrayLength"] = 100,
            ["arrayAllowDelete"] = false,
            ["arrayAllowSwap"] = false,
            ["arrayAllowUpdate"] = false,
            ["accessMode"] = 1,
            ["syncMode"] = 0,
            ["attrTips"] = "",
            ["alias"] = control.Alias,
            ["onlyConfig"] = false,
            ["preview"] = false,
            ["autoTitleWidth"] = true,
            ["titleWidth"] = 100,
            ["eyeShow"] = true,
            ["lockBlock"] = false,
            ["enumEnable_get"] = true,
            ["enumEnable_set"] = true,
            ["verticalTitleMode"] = false,
        };

    /// <summary>
    /// Attribute id: the editor keys the per attribute value record of an instance by it, so it must
    /// be unique, and it is derived from the module, the entry and the name so that repairing twice
    /// produces the same document.
    /// </summary>
    public static string StableId(string module, int id, string varName) {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{module}.f{id}.{varName}"));
        var high = BitConverter.ToUInt64(hash, 0) % 1_000_000_000_000UL + 100_000_000_000UL;
        var low = BitConverter.ToUInt64(hash, 8) % 10_000_000_000_000_000UL;
        return $"{high}_0.{low:D16}";
    }
}
