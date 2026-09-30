using System.Text.Json.Nodes;
using GCSlayer.Services;
using Xunit;

namespace GCSlayer.Tests;

/// <summary>
/// The control of a recovered parameter comes from the compiled function (the scanner) or, for a
/// value the function does not constrain, from the kind the shipped data observed. Nothing here may
/// depend on what the author called the parameter: these tests pin down the rule end to end, through
/// the same repair the tool runs.
/// </summary>
public class ExtensionControlCatalogTests {
    private static JsParameter Parameter(string name, JsParameterKind kind, bool inferred = true,
        IReadOnlyList<int>? options = null, IReadOnlyList<string>? members = null) =>
        new(name, kind, options ?? [], 0, inferred, members ?? []);

    [Theory]
    [InlineData(JsParameterKind.Number, 0, 0)]
    [InlineData(JsParameterKind.NumberVariable, 0, 6)]
    [InlineData(JsParameterKind.String, 1, 9)]
    [InlineData(JsParameterKind.StringVariable, 0, 8)]
    [InlineData(JsParameterKind.SwitchVariable, 0, 7)]
    [InlineData(JsParameterKind.Boolean, 2, 15)]
    [InlineData(JsParameterKind.NumberReference, 0, 36)]
    [InlineData(JsParameterKind.ConditionReference, 0, 50)]
    [InlineData(JsParameterKind.StringReference, 0, 51)]
    public void Resolve_MapsEveryKindOntoAValidControl(JsParameterKind kind, int varType, int compType) {
        var control = ExtensionControlCatalog.Resolve("CustomCondition", 7, Parameter("p", kind));

        Assert.Equal(varType, control.VarType);
        Assert.Equal(compType, control.CompType);
    }

    [Fact]
    public void Resolve_KeepsTheOptionNumbersInsteadOfAnyWording() {
        // The function compares against 0 and 1, so the values themselves are the labels: no author
        // table is left to borrow wording from.
        var control = ExtensionControlCatalog.Resolve("CustomCondition", 7,
            Parameter("opt", JsParameterKind.Enum, options: [0, 1]));

        Assert.Equal(0, control.VarType);
        Assert.Equal(2, control.CompType);
        Assert.Equal("0,1", control.CompParam["items"]!.GetValue<string>());
        Assert.Equal(string.Empty, control.Alias);
    }

    [Fact]
    public void Resolve_IgnoresAnAuthorChosenNameThatSuggestsAnotherKind() {
        // No function body evidence and no shipped instance: the kind default is a number, whatever
        // the author called the parameter.
        var control = ExtensionControlCatalog.Resolve("CustomCondition", 7,
            Parameter("isThing", JsParameterKind.Number, inferred: false));

        Assert.Equal(0, control.VarType);
        Assert.Equal(0, control.CompType);
        Assert.Equal(string.Empty, control.Alias);
    }

    [Fact]
    public void Resolve_ReadsAListAsAPlainListWithoutAListMeaningInTheData() {
        var control = ExtensionControlCatalog.Resolve("CustomCondition", 7,
            Parameter("trophy", JsParameterKind.Array));

        Assert.Equal(0, control.VarType);
        Assert.Equal(0, control.CompType);
        Assert.True(control.ArrayMode);
        Assert.Equal(string.Empty, control.Alias);
    }

    [Fact]
    public void Resolve_PicksTheUiComponentPickerForAUiComponentObject() {
        var control = ExtensionControlCatalog.Resolve("CustomCondition", 7,
            Parameter("uiComp", JsParameterKind.Object, members: ["uiID", "compID"]));

        Assert.Equal(5, control.VarType);
        Assert.Equal(46, control.CompType);
    }

    [Fact]
    public void Resolve_PicksAGenericCustomDataPickerForOtherObjects() {
        var control = ExtensionControlCatalog.Resolve("CustomCondition", 7,
            Parameter("modelData", JsParameterKind.Object, members: ["moduleID", "dataID"]));

        Assert.Equal(5, control.VarType);
        Assert.Equal(55, control.CompType);
        Assert.True(control.CompParam["numType"]!.GetValue<bool>());
    }

    [Fact]
    public void BuildDocument_WritesTheFieldsTheEditorReadsWithoutGuessing() {
        var parameters = new[] {
            Parameter("flag", JsParameterKind.Boolean),
            Parameter("text", JsParameterKind.String),
            Parameter("list", JsParameterKind.Array),
        };

        var document = ExtensionControlCatalog.BuildDocument("CustomCondition", 7, parameters);
        var attributes = document["blockList"]!.AsArray()[0]!.AsObject()["blockAttrs"]!.AsArray();

        Assert.Equal(3, attributes.Count);
        for (var i = 0; i < 2; i++) {
            var attr = attributes[i]!.AsObject()["attr"]!.AsObject();
            // compType must be a key of the editor's component mapping and compParam an object.
            Assert.True(attr["compData"]!["compType"]!.GetValue<int>() >= 0);
            Assert.NotNull(attr["compData"]!["compParam"]!.AsObject());
            // A boolean default value is lower cased by the editor, so it must be a string.
            Assert.IsType<string>(attr["defaultValue"]!.GetValue<string>());
            Assert.False(string.IsNullOrEmpty(attr["varName"]!.GetValue<string>()));
            Assert.False(string.IsNullOrEmpty(attr["id"]!.GetValue<string>()));
            // No label is invented, so the panel falls back to the parameter name.
            Assert.Equal(string.Empty, attr["alias"]!.GetValue<string>());
            Assert.False(attr["arrayMode"]!.GetValue<bool>());
        }
        Assert.Equal("false", attributes[0]!.AsObject()["attr"]!["defaultValue"]!.GetValue<string>());
        Assert.True(attributes[2]!.AsObject()["attr"]!["arrayMode"]!.GetValue<bool>());
    }

    [Fact]
    public void BuildDocument_KeepsTheLayoutAndBlockFieldsRealDocumentsCarry() {
        var document = ExtensionControlCatalog.BuildDocument("CustomGameNumber", 3, [Parameter("x", JsParameterKind.Number)]);

        Assert.Equal(3, document["id"]!.GetValue<int>());
        Assert.False(document["isCustomPhraseDesc"]!.GetValue<bool>());
        var block = document["blockList"]!.AsArray()[0]!.AsObject();
        Assert.Equal("未命名块", block["name"]!.GetValue<string>());
        Assert.True(block["autoOrder"]!.GetValue<bool>());
        Assert.Empty(block["blockCondition"]!.AsArray());
        var entry = block["blockAttrs"]!.AsArray()[0]!.AsObject();
        Assert.Empty(entry["attrConditions"]!.AsArray());
        Assert.True(entry["width"]!.GetValue<int>() > 0);
        Assert.Equal(32, entry["height"]!.GetValue<int>());
    }

    [Fact]
    public void StableId_IsDeterministicAndUniquePerParameter() {
        var first = ExtensionControlCatalog.StableId("CustomCondition", 7, "opt");
        var again = ExtensionControlCatalog.StableId("CustomCondition", 7, "opt");
        var other = ExtensionControlCatalog.StableId("CustomCondition", 7, "useVar");

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        // The editor keys its per attribute records with this shape.
        Assert.Matches(@"^\d{6,}_0\.\d+$", first);
    }
}
