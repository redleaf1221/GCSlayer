using GCSlayer.Services;

namespace GCSlayer.Tests;

/// <summary>
/// A recovered project in which two documents of different modules declare the very same parameter
/// names, which is what the shipped event data alone cannot tell apart: only the command parameter
/// table knows whether the call sits in a slot that means a custom condition or a custom string.
/// </summary>
internal sealed class SharedParameterFixture : IDisposable {
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "gcs-shared-" + Guid.NewGuid().ToString("N"));

    public SharedParameterFixture() {
        Directory.CreateDirectory(Root);

        // command 0 reads cmd.params[15] as a custom condition and cmd.params[23] as a custom string,
        // and the three other accessors exist so the table has to pick the right slot.
        Write("GameCreator/script.js", """
            var CommandExecute;
            (function (CommandExecute) {
                function command_0(commandPage, cmd, trigger, triggerPlayer) {
                    var params = cmd.params;
                    if (params[0] > 3) {
                        var customFunc = CustomCondition["f" + (params[0] - 3)];
                        customFunc(trigger, params[15][1]);
                    }
                    var text = getString2(params[21], params[11], params[23]);
                    function getString2(useGameStr, param, customParam) {
                        if (!useGameStr) {
                            return param;
                        }
                        return CustomGameString["f" + customParam[0]](trigger, customParam[1]);
                    }
                }
                CommandExecute.command_0 = command_0;
            })(CommandExecute || (CommandExecute = {}));
            """);

        // the two compiled entries the release kept: same parameter names, different modules
        Write("Game/GCMain.ts", """
            var CustomCondition;
            (function (CustomCondition) {
                function f7(trigger, p) {
                    return p.opt == 1;
                }
                CustomCondition.f7 = f7;
            })(CustomCondition || (CustomCondition = {}));
            var CustomGameString;
            (function (CustomGameString) {
                function f11(trigger, p) {
                    return p.opt;
                }
                CustomGameString.f11 = f11;
            })(CustomGameString || (CustomGameString = {}));
            """);

        // a page with one call in the custom condition slot, one in the custom string slot, and one in
        // a slot the script does not describe at all
        Write("asset/json/server/command/data/ws1.json", """
            {"commands":[
            [0,9,0,[0,1,1,0],0,0,[0,0,0,0],0,[0,0,0],0,0,[0,0],"","",false,null,
            [10,{"opt":1},[{"111_0.1":1}]],null,null,null,null,null,null,null,0,null,{"___cmdID":"a"}],
            [0,0,0,[0,1,1,0],0,0,[0,0,0,0],0,[0,0,0],2,0,[0,0],"","",false,null,
            null,null,null,null,null,null,null,0,[11,{"opt":1},[{"222_0.2":1}]],{"___cmdID":"b"}],
            [0,0,0,[0,1,1,0],0,0,[0,0,0,0],0,[0,0,0],0,0,[0,0],"","",false,
            [12,{"sys":1},[{"333_0.3":1}]],null,null,null,null,null,null,null,0,null,{"___cmdID":"c"}]]}
            """);

        Write("asset/json/custom/customCondition.json", """{"list":[null,""]}""");
        Write("asset/json/custom/customGameStringList.json", """{"list":[null]}""");
    }

    public string Path_(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Write(string relative, string content) {
        var path = Path_(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public string[] JsonFiles() =>
        Directory.GetFiles(Path_( "asset/json"), "*.json", SearchOption.AllDirectories);

    public (string Module, int Id, IReadOnlyList<string> Parameters)[] Documents() => [
        ("CustomCondition", 7, ["opt"]),
        ("CustomGameString", 11, ["opt"]),
    ];

    public string? AttributeId(string relative, string varName) {
        var text = File.ReadAllText(Path_(relative));
        var document = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject();
        return document["blockList"]!.AsArray()[0]!.AsObject()["blockAttrs"]!.AsArray()
            .Select(entry => entry!.AsObject()["attr"]!.AsObject())
            .FirstOrDefault(attr => attr["varName"]!.GetValue<string>() == varName)?["id"]!.GetValue<string>();
    }

    public void Dispose() {
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
        GC.SuppressFinalize(this);
    }
}

public class ExtensionInstanceAttributionTests {
    [Fact]
    public void AttributesACallToTheModuleItsCommandSlotMeans() {
        using var fixture = new SharedParameterFixture();
        var types = CommandParameterTypes.Build(fixture.Root);

        var index = ExtensionInstanceIndex.Build(fixture.Documents(), fixture.JsonFiles(), types);

        Assert.Equal("111_0.1", index.AttributeId("CustomCondition", 7, "opt"));
        Assert.Equal("222_0.2", index.AttributeId("CustomGameString", 11, "opt"));
    }

    [Fact]
    public void DropsTheVoteOfACallThatTwoDocumentsFitWithoutTheTable() {
        using var fixture = new SharedParameterFixture();

        var index = ExtensionInstanceIndex.Build(fixture.Documents(), fixture.JsonFiles());

        // both candidates match the parameter names, so the shipped data cannot tell them apart
        Assert.Null(index.AttributeId("CustomCondition", 7, "opt"));
        Assert.Null(index.AttributeId("CustomGameString", 11, "opt"));
        Assert.Equal(0, index.RecoveredIds);
    }

    [Fact]
    public void AttributesACallInASlotTheScriptDoesNotDescribeByItsParameterNames() {
        using var fixture = new SharedParameterFixture();
        // the third call sits in a slot the script says nothing about, so the parameter names of a
        // single candidate document are still the evidence that attributes it
        var documents = new (string, int, IReadOnlyList<string>)[] { ("CustomValueFunction", 3, ["sys"]) };

        var index = ExtensionInstanceIndex.Build(documents, fixture.JsonFiles(),
            CommandParameterTypes.Build(fixture.Root));

        Assert.Equal("333_0.3", index.AttributeId("CustomValueFunction", 3, "sys"));
    }

    [Fact]
    public async Task RepairWritesTheRecoveredAttributeIdsIntoTheTwoDocuments() {
        using var fixture = new SharedParameterFixture();

        await new ProjectRepairService(_ => Task.CompletedTask).RepairAsync(fixture.Root);

        Assert.Equal("111_0.1", fixture.AttributeId("asset/json/custom/condition/condition7.json", "opt"));
        Assert.Equal("222_0.2", fixture.AttributeId("asset/json/custom/gameString/str11.json", "opt"));
    }
}
