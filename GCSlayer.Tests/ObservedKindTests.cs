using GCSlayer.Services;
using Xunit;

namespace GCSlayer.Tests;

/// <summary>
/// A recovered condition whose compiled function never constrains one of its parameters, while the
/// shipped call of that entry stores a boolean there. The control must follow that data evidence,
/// and must never follow what the parameter is called.
/// </summary>
internal sealed class ObservedKindFixture : IDisposable {
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "gcs-observed-" + Guid.NewGuid().ToString("N"));

    public ObservedKindFixture() {
        Directory.CreateDirectory(Root);
        Write("asset/json/custom/customCondition.json", """{"list":[null,""]}""");
        // f7 reads p.ran and hands p.isThing to a helper that does not exist in the script, so the
        // body constrains ran and says nothing about isThing.
        Write("Game/GCMain.ts", """
            var CustomCondition;
            (function (CustomCondition) {
                function f7(trigger, p) {
                    W26_Helper.forward(p.isThing);
                    return p.ran == 1;
                }
                CustomCondition.f7 = f7;
            })(CustomCondition || (CustomCondition = {}));
            """);
        // A shipped page: commands -> page -> command instance; the instance's value slot holds one
        // call of entry 7, `[7, { ran, isThing }, [ { attributeID: value } ]]`, and the record stores
        // a boolean for isThing.
        Write("asset/json/server/command/data/ws1.json", """
            {"commands":[[0,[7,{"ran":1,"isThing":true},[{"1_0.1":1,"1_0.2":true}]],null,"x",
            {"___cmdID":"a"}]]}
            """);
    }

    public void Dispose() {
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
        GC.SuppressFinalize(this);
    }

    public void Write(string relative, string content) {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public async Task RepairAsync() =>
        await new ProjectRepairService(_ => Task.CompletedTask).RepairAsync(Root);

    /// <summary>The controls of the rebuilt document, as (varName, (varType, compType), alias).</summary>
    public (string Name, int VarType, int CompType, string Alias)[] Controls() {
        var document = System.Text.Json.Nodes.JsonNode
            .Parse(File.ReadAllText(Path.Combine(Root, "asset/json/custom/condition/condition7.json")))!.AsObject();
        return document["blockList"]!.AsArray()[0]!.AsObject()["blockAttrs"]!.AsArray()
            .Select(entry => entry!.AsObject()["attr"]!.AsObject())
            .Select(attr => (
                attr["varName"]!.GetValue<string>(),
                attr["varType"]!.GetValue<int>(),
                attr["compData"]!["compType"]!.GetValue<int>(),
                attr["alias"]!.GetValue<string>()))
            .ToArray();
    }
}

public class ObservedKindTests {
    [Fact]
    public void IndexSeesTheBooleanInTheShippedCall() {
        using var fixture = new ObservedKindFixture();
        var documents = new (string, int, IReadOnlyList<string>)[] {
            ("CustomCondition", 7, ["ran", "isThing"]),
        };

        var index = ExtensionInstanceIndex.Build(documents,
            Directory.GetFiles(Path.Combine(fixture.Root, "asset/json"), "*.json", SearchOption.AllDirectories));

        Assert.Equal(JsParameterKind.Boolean, index.ObservedKind("CustomCondition", 7, "isThing"));
        Assert.Equal("1_0.2", index.AttributeId("CustomCondition", 7, "isThing"));
    }

    [Fact]
    public async Task ControlFollowsTheShippedDataWhenTheFunctionDoesNotConstrainTheValue() {
        using var fixture = new ObservedKindFixture();

        await fixture.RepairAsync();

        var controls = fixture.Controls();
        Assert.Equal(["isThing", "ran"], controls.Select(control => control.Name));
        // Nothing in the body constrains isThing, and the shipped call stores a boolean for it: the
        // control follows that evidence, not the "is" prefix.
        Assert.Equal((2, 15), (controls[0].VarType, controls[0].CompType));
        Assert.Equal(string.Empty, controls[0].Alias);
        // ran is compared against 1, i.e. the body constrains it to a number.
        Assert.Equal((0, 0), (controls[1].VarType, controls[1].CompType));
    }
}
