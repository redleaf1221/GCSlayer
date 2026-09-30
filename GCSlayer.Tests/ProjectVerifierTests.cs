using System.Text.Json.Nodes;

namespace GCSlayer.Tests;

/// <summary>
/// The verifier must not be a rubber stamp: it fails before the repair and still fails when a
/// single document, slot or attribute definition goes missing afterwards.
/// </summary>
public class ProjectVerifierTests {
    [Fact]
    public async Task FailsBeforeTheRepairBecauseTheTemplateStubsDoNotCoverTheData() {
        using var fixture = new RecoveredProjectFixture();

        var clean = await fixture.VerifyAsync();

        Assert.False(clean);
    }

    [Fact]
    public async Task PassesOnARepairedProject() {
        using var fixture = new RecoveredProjectFixture();
        await fixture.RepairAsync();

        var report = new List<string>();
        var clean = await fixture.VerifyAsync(report);

        Assert.True(clean);
        Assert.Contains(report, line => line.StartsWith("OK:"));
    }

    [Fact]
    public async Task ReportsAListEntryWithoutItsDocument() {
        using var fixture = new RecoveredProjectFixture();
        await fixture.RepairAsync();
        File.Delete(fixture.Path_("asset/json/avatar/data/avatar5.json"));

        var report = new List<string>();
        var clean = await fixture.VerifyAsync(report);

        Assert.False(clean);
        Assert.Contains(report, line => line.Contains("avatarList.json") && line.Contains("5"));
    }

    [Fact]
    public async Task ReportsAReferencedIdWithoutItsEntry() {
        using var fixture = new RecoveredProjectFixture();
        await fixture.RepairAsync();
        fixture.Write("asset/json/ui/data/ui1.json", """{"name":"窗口","avatarID":9}""");

        var report = new List<string>();
        var clean = await fixture.VerifyAsync(report);

        Assert.False(clean);
        Assert.Contains(report, line => line.Contains("referenced id 9"));
    }

    [Fact]
    public async Task ReportsAVariableIdWithoutItsNameSlot() {
        using var fixture = new RecoveredProjectFixture();
        await fixture.RepairAsync();
        fixture.Write("asset/json/variable/variable.json", """{"list":{},"type":{}}""");

        var report = new List<string>();
        var clean = await fixture.VerifyAsync(report);

        Assert.False(clean);
        Assert.Contains(report, line => line.Contains("variable/variable.json") && line.Contains("11001"));
    }

    [Fact]
    public async Task ReportsAnExtensionEntryWithoutItsDocument() {
        using var fixture = new RecoveredProjectFixture();
        await fixture.RepairAsync();
        File.Delete(fixture.Path_("asset/json/custom/condition/condition7.json"));

        var report = new List<string>();
        var clean = await fixture.VerifyAsync(report);

        Assert.False(clean);
        Assert.Contains(report, line => line.Contains("customCondition.json") && line.Contains("7"));
    }

    [Fact]
    public async Task ReportsAParameterTheCompiledFunctionReadsButTheDocumentDoesNotOffer() {
        using var fixture = new RecoveredProjectFixture();
        await fixture.RepairAsync();
        // An entry whose block has no control cannot be edited, which is exactly what must not pass.
        fixture.Write("asset/json/custom/condition/condition7.json",
            """{"compAutoOrderLimitHeight":0,"id":7,"blockList":[{"name":"未命名块","blockAttrs":[],"blockCondition":[],"blockHeight":35,"autoOrder":true,"hideBlockTitle":false}],"isCustomPhraseDesc":false}""");

        var report = new List<string>();
        var clean = await fixture.VerifyAsync(report);

        Assert.False(clean);
        Assert.Contains(report, line => line.Contains("condition7.json") && line.Contains("opt") && line.Contains("has no control"));
    }

    [Fact]
    public async Task ReportsAControlTheCompiledFunctionNeverReads() {
        using var fixture = new RecoveredProjectFixture();
        await fixture.RepairAsync();
        var path = fixture.Path_("asset/json/custom/gameString/str3.json");
        var document = fixture.ReadObject("asset/json/custom/gameString/str3.json");
        var attributes = document["blockList"]!.AsArray()[0]!.AsObject()["blockAttrs"]!.AsArray();
        var ghost = JsonNode.Parse(attributes[0]!.ToJsonString())!;
        ghost["attr"]!["varName"] = "ghost";
        attributes.Add(ghost);

        File.WriteAllText(path, document.ToJsonString());

        var report = new List<string>();
        var clean = await fixture.VerifyAsync(report);

        Assert.False(clean);
        Assert.Contains(report, line => line.Contains("str3.json") && line.Contains("ghost"));
    }

    [Fact]
    public async Task ReportsAnUndeclaredCommandAttribute() {
        using var fixture = new RecoveredProjectFixture();
        await fixture.RepairAsync();
        fixture.Write("asset/json/server/command/data/ws4.json",
            """[[15001,{"99999999999_0.123":1,"___cmdID":"y"}]]""");

        var report = new List<string>();
        var clean = await fixture.VerifyAsync(report);

        Assert.False(clean);
        Assert.Contains(report, line => line.Contains("99999999999_0.123"));
    }

    [Fact]
    public async Task ReportsBrokenJson() {
        using var fixture = new RecoveredProjectFixture();
        await fixture.RepairAsync();
        fixture.Write("asset/json/broken.json", "{ this is not json");

        var report = new List<string>();
        var clean = await fixture.VerifyAsync(report);

        Assert.False(clean);
        Assert.Contains(report, line => line.Contains("broken.json"));
    }

    [Fact]
    public async Task AcceptsTheTemplateCustomAttributeFile() {
        using var fixture = new RecoveredProjectFixture();
        await fixture.RepairAsync();

        // sanity: the fixture's command definition is what makes the instance legal
        Assert.True(await fixture.VerifyAsync());
        Assert.True(File.Exists(fixture.Path_("asset/json/custom/cmd/cmd15001.json")));
    }
}
