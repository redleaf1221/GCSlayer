using GCSlayer.Services;
using Xunit;

namespace GCSlayer.Tests;

/// <summary>
/// The extension parameters are recovered by parsing the compiled game script into an AST, so the
/// tests feed real script snippets through the same parser the repair uses.
/// </summary>
public class JavaScriptFunctionScannerTests {
    /// <summary>Wraps a function body the way the editor compiles an extension.</summary>
    private static string Script(string module, int id, string body, bool commentedOut = false) {
        var code = $$"""
            (function ({{module}}) {
                function f{{id}}(trigger, p) {
            {{body}}
                }
                {{module}}.f{{id}} = f{{id}};
            })({{module}} || ({{module}} = {}));
            """;
        return commentedOut ? "/*\n" + code + "\n*/" : code;
    }

    private static JsExtensionFunction Single(string script, string module = "CustomCondition", int id = 7) {
        var functions = JavaScriptFunctionScanner.Scan(script);
        return Assert.Single(functions, function => function.Module == module && function.Id == id);
    }

    private static JsParameter Parameter(JsExtensionFunction function, string name) =>
        Assert.Single(function.Parameters, parameter => parameter.Name == name);

    [Fact]
    public void Scan_IgnoresCommentedOutRevision() {
        var script = Script("CustomCondition", 7, "        return p.oldOnly == 1;", commentedOut: true)
            + Script("CustomCondition", 7, "        return p.opt == 0;");

        var function = Single(script);

        Assert.Equal(["opt"], function.Parameters.Select(parameter => parameter.Name));
    }

    [Fact]
    public void Scan_CollectsEveryParameterNameInSourceOrder() {
        var script = Script("CustomCondition", 7, """
                    var trans = W26_TransMap.getTrans(p.transId);
                    if (trans == null)
                        return false;
                    if (p.opt == 0) {
                        return trans != null;
                    }
                    else if (p.opt == 1) {
                        return trans.isPause();
                    }
                    return false;
            """);

        var function = Single(script);

        Assert.Equal(["transId", "opt"], function.Parameters.Select(parameter => parameter.Name));
        Assert.Equal(JsParameterKind.Enum, Parameter(function, "opt").Kind);
        Assert.Equal([0, 1], Parameter(function, "opt").Options);
    }

    [Fact]
    public void Scan_ReadsVariablesConstantsAndFlagsFromAConditional() {
        var script = Script("CustomGameNumber", 12, """
                    var d = W26_TransMap.getEventParam(trigger, p.useVar ? Game.player.variable.getString(p.name1) : p.name);
                    return d == null ? 0 : d;
            """);

        var function = Single(script, "CustomGameNumber", 12);

        Assert.Equal(JsParameterKind.Boolean, Parameter(function, "useVar").Kind);
        Assert.Equal(JsParameterKind.StringVariable, Parameter(function, "name1").Kind);
        Assert.Equal(JsParameterKind.String, Parameter(function, "name").Kind);
    }

    [Fact]
    public void Scan_ReadsSwitchAndNumberVariables() {
        var script = Script("CustomCondition", 1, """
                    if (p.useVarID) {
                        uiID = Game.player.variable.getVariable(p.uiIDVarID);
                    }
                    else {
                        uiID = p.uiID;
                    }
                    if (Game.player.variable.getSwitch(p.flagID)) {
                        return false;
                    }
                    return true;
            """);

        var function = Single(script, "CustomCondition", 1);

        Assert.Equal(JsParameterKind.NumberVariable, Parameter(function, "uiIDVarID").Kind);
        Assert.Equal(JsParameterKind.Boolean, Parameter(function, "useVarID").Kind);
        Assert.Equal(JsParameterKind.SwitchVariable, Parameter(function, "flagID").Kind);
        Assert.Equal(JsParameterKind.Number, Parameter(function, "uiID").Kind);
    }

    [Fact]
    public void Scan_RecognisesACustomNumberReference() {
        var script = Script("CustomGameNumber", 3, """
                    var value = CustomGameNumber["f" + p.numData2[0]](trigger, p.numData2[1]);
                    return value + p.offset;
            """);

        var function = Single(script, "CustomGameNumber", 3);

        Assert.Equal(JsParameterKind.NumberReference, Parameter(function, "numData2").Kind);
        Assert.Equal(JsParameterKind.Number, Parameter(function, "offset").Kind);
    }

    [Fact]
    public void Scan_RecognisesNestedObjects() {
        var script = Script("CustomCondition", 3, """
                    var uiID = p.uiComp.uiID;
                    var comp = GameUI.get(p.modelData.dataID);
                    return comp[p.uiComp.varName] ? true : false;
            """);

        var function = Single(script, "CustomCondition", 3);

        Assert.Equal(JsParameterKind.Object, Parameter(function, "uiComp").Kind);
        Assert.Equal(JsParameterKind.Object, Parameter(function, "modelData").Kind);
    }

    [Fact]
    public void Scan_IgnoresInnerFunctionsThatShadowTheParameterObject() {
        var script = Script("CustomCondition", 4, """
                    var mapped = list.map(function (p) {
                        return p.shadowed;
                    });
                    return p.visible ? true : false;
            """);

        var function = Single(script, "CustomCondition", 4);

        Assert.Equal(["visible"], function.Parameters.Select(parameter => parameter.Name));
    }

    [Fact]
    public void Scan_ResolvesTheModuleFromTheAssignmentWhenThereIsNoIife() {
        var script = """
            var CustomGameString;
            function f9(trigger, p) {
                return String(p.text);
            }
            CustomGameString.f9 = f9;
            """;

        var function = Single(script, "CustomGameString", 9);

        // The kind comes from the String() coercion around the read, not from the name "text".
        Assert.Equal(JsParameterKind.String, Parameter(function, "text").Kind);
    }

    [Fact]
    public void Scan_NeverDerivesAKindFromTheSpellingOfTheName() {
        var script = Script("CustomCondition", 8, """
                    if (p.isThing || p.hasThing || p.useThing || p.allowThing || p.canThing || p.thingEnable) {
                        return true;
                    }
                    return p.thingName + p.thingStr + p.thingText + p.thingPath + p.thingUrl + p.thingTitle;
            """);

        var function = Single(script, "CustomCondition", 8);

        // Every one of those names suggests a boolean or a string; only the use decides. The six the
        // "if" tests are booleans, and the six added into a number are numbers because of the
        // addition, not because "Str"/"Text" sounds like a string.
        foreach (var name in new[] { "isThing", "hasThing", "useThing", "allowThing", "canThing", "thingEnable" }) {
            Assert.Equal(JsParameterKind.Boolean, Parameter(function, name).Kind);
            Assert.True(Parameter(function, name).Inferred);
        }
        foreach (var name in new[] { "thingName", "thingStr", "thingText", "thingPath", "thingUrl", "thingTitle" })
            Assert.Equal(JsParameterKind.Number, Parameter(function, name).Kind);
    }

    [Fact]
    public void Scan_LeavesAForwardedFlagUnconstrainedWhenOnlyTheNameSuggestsABoolean() {
        // The function hands p.isThing on to another object and never tests, compares or coerces it,
        // so nothing in the body says it is a flag: the kind stays the default and Inferred is false.
        var script = Script("CustomCondition", 10, "        return helper.forward(p.isThing);");

        var parameter = Parameter(Single(script, "CustomCondition", 10), "isThing");

        Assert.Equal(JsParameterKind.Number, parameter.Kind);
        Assert.False(parameter.Inferred);
    }

    [Fact]
    public void Scan_IgnoresCodeThatOnlyLooksLikeAnExtension() {
        var script = """
            (function (CustomCondition) {
                function helper(trigger, p) {
                    return p.notAnExtension;
                }
                CustomCondition.helper = helper;
            })(CustomCondition || (CustomCondition = {}));
            """;

        Assert.Empty(JavaScriptFunctionScanner.Scan(script));
    }

    /// <summary>
    /// Diagnostic: GCSLAYER_DUMP_PARAMS=&lt;file&gt; GCSLAYER_SCRIPT=&lt;game script&gt; dumps what the
    /// scanner recovers from a real project, which is how the classification rules were tuned.
    /// </summary>
    [Fact]
    public void Scan_DumpsARealScriptWhenAsked() {
        var target = Environment.GetEnvironmentVariable("GCSLAYER_DUMP_PARAMS");
        var source = Environment.GetEnvironmentVariable("GCSLAYER_SCRIPT");
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(source) || !File.Exists(source))
            return;

        var functions = JavaScriptFunctionScanner.Scan(File.ReadAllText(source));
        var lines = functions
            .OrderBy(function => function.Module, StringComparer.Ordinal)
            .ThenBy(function => function.Id)
            .Select(function => $"{function.Module}.f{function.Id}: "
                + string.Join(", ", function.Parameters.Select(parameter => {
                    var flag = parameter.Inferred ? string.Empty : "?";
                    return parameter.Kind switch {
                        JsParameterKind.Enum => $"{parameter.Name}=enum({string.Join("|", parameter.Options)}){flag}",
                        JsParameterKind.Array => $"{parameter.Name}=array[{parameter.ArrayLength}]{flag}",
                        _ => $"{parameter.Name}={parameter.Kind}{flag}",
                    };
                })));
        File.WriteAllLines(target, lines);
    }
}
