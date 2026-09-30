using System.Text.Json.Nodes;
using GCSlayer.Services;

namespace GCSlayer.Tests;

/// <summary>
/// Locks in the reverse engineered rules about what a released GameCreator game still ships and
/// how the editor lists are rebuilt from it.
/// </summary>
public class ProjectRepairServiceTests {
    private static string? NameAt(JsonArray array, int index) => array[index]?.GetValue<string>();

    [Fact]
    public async Task RebuildsAnimationListFromItsDocuments() {
        using var fixture = new RecoveredProjectFixture();

        await fixture.RepairAsync();

        Assert.Equal("1", NameAt(fixture.TypedListOf("asset/json/animation/animationList.json", "1"), 1));
        Assert.Equal("1001", NameAt(fixture.TypedListOf("asset/json/animation/animationList.json", "2"), 1));
        Assert.Equal(["1", "2"], fixture.ReadObject("asset/json/animation/animationList.json")["type"]!.AsObject()
            .Select(pair => pair.Key).ToArray());
    }

    [Fact]
    public async Task RebuildsAvatarListAndDropsPhantomTemplateEntries() {
        using var fixture = new RecoveredProjectFixture();

        await fixture.RepairAsync();

        var list = fixture.TypedListOf("asset/json/avatar/avatarList.json", "1");
        Assert.Equal("1", NameAt(list, 1));
        Assert.Null(list[2]);
        Assert.Null(list[3]);
        Assert.Null(list[4]);
        // the data still points at avatar 5, so it gets a slot and an empty document
        Assert.Equal("5", NameAt(list, 5));
        Assert.True(File.Exists(fixture.Path_("asset/json/avatar/data/avatar5.json")));
        Assert.Equal(5, fixture.ReadObject("asset/json/avatar/data/avatar5.json")["id"]!.GetValue<int>());
        // the template's avatar type 12 never existed in the game
        Assert.False(fixture.ReadObject("asset/json/avatar/avatarList.json")["type"]!.AsObject().ContainsKey("12"));
    }

    [Fact]
    public async Task PadsStandingAvatarPartListToTheHighestReferencedPart() {
        using var fixture = new RecoveredProjectFixture();

        await fixture.RepairAsync();

        var list = fixture.ListOf("asset/json/standAvatar/standAvatarPartList.json");
        Assert.Equal(4, list.Count);
        Assert.Equal("1", NameAt(list, 1));
        Assert.Equal("3", NameAt(list, 3));
    }

    [Fact]
    public async Task RebuildsCustomGameAttributeFromTheFourReleasedFiles() {
        using var fixture = new RecoveredProjectFixture();

        await fixture.RepairAsync();

        var combined = fixture.ReadObject("asset/json/custom/customGameAttribute.json");
        Assert.Equal(fixture.ReadObject("asset/json/custom/customWorldSetting.json").ToJsonString(),
            combined["worldAttributeSetting"]!.ToJsonString());
        Assert.Equal(fixture.ReadObject("asset/json/custom/customPlayerSetting.json").ToJsonString(),
            combined["playerAttributeSetting"]!.ToJsonString());
        Assert.Equal(fixture.ReadObject("asset/json/custom/customWorldData.json").ToJsonString(),
            combined["worldAttributeConfig"]!.ToJsonString());
        Assert.Equal(fixture.ReadObject("asset/json/custom/customPlayerData.json").ToJsonString(),
            combined["playerAttributeConfig"]!.ToJsonString());
        Assert.Equal("keep-me", combined["k1"]!.GetValue<string>());
    }

    [Fact]
    public async Task VariableListsCoverCommandReferencesAndSaveFileIds() {
        using var fixture = new RecoveredProjectFixture();

        await fixture.RepairAsync();

        // player number variable: referenced as resultType 3, plus the id used by the save file
        var player = fixture.ReadObject("asset/json/variable/variable.json")["list"]!.AsObject();
        Assert.Equal("1", NameAt(player["12"]!.AsArray(), 1));
        Assert.Equal("2", NameAt(player["1"]!.AsArray(), 2));

        // world number variable: resultType 0, the save file id again has to fit as well
        var world = fixture.ReadObject("asset/json/server/variable/variable.json")["list"]!.AsObject();
        Assert.Equal("1", NameAt(world["13"]!.AsArray(), 1));
        Assert.Equal("2", NameAt(world["1"]!.AsArray(), 2));

        // switches (resultType 1 world / 4 player) and strings (2 / 5)
        var worldSwitch = fixture.ReadObject("asset/json/server/variable/switch.json")["list"]!.AsObject();
        Assert.Equal("1", NameAt(worldSwitch["14"]!.AsArray(), 1));
        Assert.Equal("3", NameAt(worldSwitch["1"]!.AsArray(), 3));
        Assert.Equal("1", NameAt(
            fixture.ReadObject("asset/json/variable/switch.json")["list"]!.AsObject()["13"]!.AsArray(), 1));

        var worldString = fixture.ReadObject("asset/json/server/variable/string.json")["list"]!.AsObject();
        Assert.Equal("1", NameAt(worldString["15"]!.AsArray(), 1));
        Assert.Equal("1", NameAt(worldString["1"]!.AsArray(), 1));
        Assert.Equal("1", NameAt(
            fixture.ReadObject("asset/json/variable/string.json")["list"]!.AsObject()["16"]!.AsArray(), 1));
    }

    [Fact]
    public async Task ExtensionListsFollowTheCompiledScriptAndGetDocuments() {
        using var fixture = new RecoveredProjectFixture();

        await fixture.RepairAsync();

        var condition = fixture.ListOf("asset/json/custom/customCondition.json");
        Assert.Equal(8, condition.Count);
        Assert.Equal("7", NameAt(condition, 7));
        Assert.Null(condition[1]);
        Assert.Equal(7, fixture.ReadObject("asset/json/custom/condition/condition7.json")["id"]!.GetValue<int>());

        var number = fixture.ListOf("asset/json/custom/customGameNumberList.json");
        Assert.Equal(13, number.Count);
        Assert.Equal("12", NameAt(number, 12));
        Assert.True(File.Exists(fixture.Path_("asset/json/custom/gameNumber/num12.json")));

        var text = fixture.ListOf("asset/json/custom/customGameStringList.json");
        Assert.Equal(4, text.Count);
        Assert.Equal("3", NameAt(text, 3));
        Assert.True(File.Exists(fixture.Path_("asset/json/custom/gameString/str3.json")));

        // the game defines no custom value function and no custom behaviour at all
        Assert.Single(fixture.ListOf("asset/json/custom/customValueFunction.json"));
        Assert.Single(fixture.ListOf("asset/json/custom/customBehaviorType.json"));
        Assert.False(Directory.Exists(fixture.Path_("asset/json/custom/behavior")));
    }

    [Fact]
    public async Task StubDocumentsMatchTheShapeARealProjectUses() {
        using var fixture = new RecoveredProjectFixture();

        await fixture.RepairAsync();

        var stub = fixture.ReadObject("asset/json/custom/condition/condition7.json");
        Assert.Equal(["compAutoOrderLimitHeight", "id", "blockList", "isCustomPhraseDesc"],
            stub.Select(pair => pair.Key).ToArray());
        var block = stub["blockList"]!.AsArray()[0]!.AsObject();
        Assert.Equal("未命名块", block["name"]!.GetValue<string>());
        Assert.Empty(block["blockCondition"]!.AsArray());
    }

    [Fact]
    public async Task ExtensionDocumentsGetOneControlPerCompiledParameter() {
        using var fixture = new RecoveredProjectFixture();

        await fixture.RepairAsync();

        // CustomCondition.f7 reads opt, useVarID and transId.
        var controls = fixture.Controls("asset/json/custom/condition/condition7.json");
        Assert.Equal(["opt", "useVarID", "transId"], controls.Select(control => control.Name));
        // opt is compared against 0 and 1, and the labels are those values.
        Assert.Equal((0, 2, "0,1"), controls[0].Control);
        // No author table is consulted any more, so the panel label stays the parameter name.
        var opt = fixture.ReadObject("asset/json/custom/condition/condition7.json")["blockList"]!
            .AsArray()[0]!.AsObject()["blockAttrs"]!.AsArray()[0]!.AsObject()["attr"]!.AsObject();
        Assert.Equal("opt", opt["varName"]!.GetValue<string>());
        Assert.Equal(string.Empty, opt["alias"]!.GetValue<string>());
        // useVarID is only forwarded, nothing in the body says it is a flag, and no shipped instance
        // shows one either, so it gets the kind default: a plain number.
        Assert.Equal((0, 0, string.Empty), controls[1].Control);
        // transId is forwarded the same way and keeps the plain number control.
        Assert.Equal((0, 0, string.Empty), controls[2].Control);

        // CustomGameNumber.f12: a flag, the string variable and the string constant it selects.
        var number = fixture.Controls("asset/json/custom/gameNumber/num12.json");
        Assert.Equal(["useVar", "name1", "name"], number.Select(control => control.Name));
        Assert.Equal((2, 15, string.Empty), number[0].Control);
        Assert.Equal((0, 8, string.Empty), number[1].Control);
        Assert.Equal((1, 9, string.Empty), number[2].Control);

        // CustomGameString.f3 coerces its parameter with String(), which is what makes it a string.
        Assert.Equal((1, 9, string.Empty), fixture.Controls("asset/json/custom/gameString/str3.json")[0].Control);
    }

    [Fact]
    public async Task ExtensionDocumentsNeverOverwriteAParameterAnAuthorAlreadyAuthored() {
        using var fixture = new RecoveredProjectFixture();
        var path = fixture.Path_("asset/json/custom/condition/condition7.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string authored = """
            {"compAutoOrderLimitHeight":0,"id":7,"blockList":[{"name":"我的块","blockAttrs":[
            {"attr":{"id":"1_0.5","varName":"opt","varType":0,"compData":{"compType":2,"compParam":{"items":"甲,乙"}},
            "defaultValue":"","alias":"我","arrayMode":false,"accessMode":1,"syncMode":0}}],
            "blockCondition":[],"blockHeight":90,"autoOrder":true,"hideBlockTitle":false}],"isCustomPhraseDesc":false}
            """;
        File.WriteAllText(path, authored);

        await fixture.RepairAsync();

        Assert.Equal(authored, File.ReadAllText(path));
    }

    [Fact]
    public async Task RepairIsIdempotent() {
        using var fixture = new RecoveredProjectFixture();

        await fixture.RepairAsync();
        var first = fixture.HashAll();

        await fixture.RepairAsync();

        Assert.Equal(first, fixture.HashAll());
    }

    [Fact]
    public async Task ReportsWhatItChanged() {
        using var fixture = new RecoveredProjectFixture();
        var report = new List<string>();

        await fixture.RepairAsync(report);

        Assert.Contains(report, line => line.Contains("customGameAttribute.json"));
        Assert.Contains(report, line => line.Contains("empty document(s) created"));
        Assert.Contains(report, line => line.Contains("customCondition.json"));
    }

    [Fact]
    public async Task VariableListsAlsoHarvestTheParametersOfOtherCommands() {
        using var fixture = new RecoveredProjectFixture();

        await fixture.RepairAsync();

        // The "if" command of the fixture reads cmd.params[2][1] as a world number variable while
        // cmd.params[1] == 1, so id 19001 needs a slot and its unused player alternative (19501) not.
        var world = fixture.ReadObject("asset/json/server/variable/variable.json")["list"]!.AsObject();
        Assert.True(world.ContainsKey("20"));
        Assert.Equal("1", NameAt(world["20"]!.AsArray(), 1));

        var player = fixture.ReadObject("asset/json/variable/variable.json")["list"]!.AsObject();
        Assert.False(player.ContainsKey("20"));
    }

    [Fact]
    public async Task VariableListsKeepTheNamesAndEntriesTheProjectAlreadyHad() {
        using var fixture = new RecoveredProjectFixture();
        // a list with an author name and with a type group nothing references
        fixture.Write("asset/json/server/variable/variable.json",
            """{"list":{"1":[null,"作者的名字"],"3":[null,"另一组"]},"type":{"1":"1","3":"3"}}""");

        await fixture.RepairAsync();

        var root = fixture.ReadObject("asset/json/server/variable/variable.json");
        var list = root["list"]!.AsObject();
        Assert.Equal("作者的名字", NameAt(list["1"]!.AsArray(), 1));
        // the type group the template carried is neither dropped nor renamed
        Assert.Equal("另一组", NameAt(list["3"]!.AsArray(), 1));
        Assert.Equal("3", root["type"]!.AsObject()["3"]!.GetValue<string>());
        // and the ids of the shipped data were added next to them
        Assert.Equal("1", NameAt(list["13"]!.AsArray(), 1));
        Assert.Equal("1", NameAt(list["20"]!.AsArray(), 1));
    }

    [Fact]
    public async Task VariableListsAreOnlyExtendedNeverShortened() {
        using var fixture = new RecoveredProjectFixture();
        fixture.Write("asset/json/variable/string.json",
            """{"list":{"1":[null,"keep","keep2","keep3"]},"type":{"1":"1"}}""");

        await fixture.RepairAsync();

        var list = fixture.ReadObject("asset/json/variable/string.json")["list"]!.AsObject();
        Assert.Equal("keep", NameAt(list["1"]!.AsArray(), 1));
        Assert.Equal("keep3", NameAt(list["1"]!.AsArray(), 3));
        // the id of the shipped data (15001) lives in type 16 and is appended, not written over
        Assert.Equal("1", NameAt(list["16"]!.AsArray(), 1));
    }

    [Fact]
    public async Task ThrowsWhenTheFolderIsNotAProject() {
        using var fixture = new RecoveredProjectFixture();
        var empty = Path.Combine(fixture.Root, "not-a-project");
        Directory.CreateDirectory(empty);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            new ProjectRepairService(_ => Task.CompletedTask).RepairAsync(empty));
    }

    [Fact]
    public async Task KeepsGoingWhenTheGameScriptWasNotRecovered() {
        using var fixture = new RecoveredProjectFixture();
        File.Delete(fixture.Path_("Game/GCMain.ts"));
        var report = new List<string>();

        await fixture.RepairAsync(report);

        // without the script the extension lists stay untouched instead of being emptied
        Assert.True(fixture.ListOf("asset/json/custom/customCondition.json").Count > 1);
        Assert.Contains(report, line => line.Contains("GCMain.ts not found"));
    }
}
