using System.Text.Json.Nodes;
using GCSlayer.Services;

namespace GCSlayer.Tests;

/// <summary>
/// Locks in the repository overlay: it runs after the repair, merges list entries one by one, and only
/// ever uses a repository that belongs to the game - an explicit path, or the project's own
/// <c>file_repo/&lt;gameProjectName&gt;</c>. <c>file_repo/Default</c> is the template and contributes
/// nothing here: it is only used by the fill step, which copies files the project is missing.
/// </summary>
public class RepoOverlayServiceTests {
    private static string? NameAt(JsonArray array, int index) => array[index]?.GetValue<string>();

    private static JsonArray TypedList(RepoOverlayFixture fixture, string relative, string type) =>
        fixture.Project.ReadObject(relative)["list"]!.AsObject()[type]!.AsArray();

    /// <summary>A null <paramref name="repository"/> makes the overlay resolve the project's own one.</summary>
    private static Task OverlayAsync(RepoOverlayFixture fixture, string? repository, List<string>? report = null) =>
        new RepoOverlayService().ApplyAsync(fixture.Project.Root, repository, message => {
            report?.Add(message);
            return Task.CompletedTask;
        });

    [Fact]
    public async Task MergesRepositoryNamesEntryByEntryAndKeepsProjectOnlyEntries() {
        using var fixture = new RepoOverlayFixture();
        await fixture.Project.RepairAsync();

        await OverlayAsync(fixture, fixture.GameRepo);

        var list = fixture.Project.ReadObject("asset/json/variable/variable.json");
        var type1 = list["list"]!["1"]!.AsArray();
        // the repository names win over the stubs the repair wrote, and its list is longer
        Assert.Equal(6, type1.Count);
        Assert.Equal(["生命值", "魔法值", "力量", "敏捷", "智力"],
            type1.Skip(1).Select(name => name!.GetValue<string>()));
        // entries only the project has stay: type 12 (the repository left that name empty) and the
        // type groups the derivation produced
        Assert.Equal("1", NameAt(list["list"]!["12"]!.AsArray(), 1));
        // a type group only the repository has is added next to them
        Assert.Equal("仓库独有类型", NameAt(list["list"]!["7"]!.AsArray(), 1));
        // existing type entries keep the project's value (even an empty one); the repository's
        // missing one is added
        var types = list["type"]!.AsObject();
        Assert.Equal("", types["1"]!.GetValue<string>());
        Assert.Equal("12", types["12"]!.GetValue<string>());
        Assert.Equal("仓库类型", types["7"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnEmptyRepositoryNameNeverErasesAnInferredName() {
        using var fixture = new RepoOverlayFixture();
        await fixture.Project.RepairAsync();
        Assert.Equal("1", NameAt(TypedList(fixture, "asset/json/variable/variable.json", "12"), 1));

        await OverlayAsync(fixture, fixture.GameRepo);

        // the repository holds "" for that slot, so the inferred name stays
        Assert.Equal("1", NameAt(TypedList(fixture, "asset/json/variable/variable.json", "12"), 1));
        // the same on a flat list: "" keeps the project's entry, a name wins, a project-only entry stays
        var parts = fixture.Project.ListOf("asset/json/standAvatar/standAvatarPartList.json");
        Assert.Equal(4, parts.Count);
        Assert.Equal("1", NameAt(parts, 1));
        Assert.Equal("仓库部件", NameAt(parts, 2));
        Assert.Equal("3", NameAt(parts, 3));
    }

    [Fact]
    public async Task UsesTheProjectsOwnRepositoryWhenNoPathIsGiven() {
        using var fixture = new RepoOverlayFixture();
        await fixture.Project.RepairAsync();
        var report = new List<string>();

        // no --local_source: the overlay has to find file_repo/<gameProjectName> on its own
        await fixture.WithToolRepoAsync(() => OverlayAsync(fixture, null, report));

        Assert.Contains("仓库部件", NameAt(
            fixture.Project.ListOf("asset/json/standAvatar/standAvatarPartList.json"), 2));
        Assert.Contains(report, line => line.Contains($"- repo overlay: {fixture.GameRepoPath}"));
        Assert.Contains(report, line => line.Contains("file_repo/" + fixture.GameProjectName));
    }

    [Fact]
    public async Task AShorterRepositoryListDoesNotShrinkTheProjectList() {
        using var fixture = new RepoOverlayFixture();
        await fixture.Project.RepairAsync();
        Assert.Equal(2, TypedList(fixture, "asset/json/variable/string.json", "1").Count);

        await OverlayAsync(fixture, fixture.GameRepo);

        var player = fixture.Project.ReadObject("asset/json/variable/string.json")["list"]!.AsObject();
        // the repository only provides one slot, the project's longer list and its entries survive
        Assert.Equal(2, player["1"]!.AsArray().Count);
        Assert.Equal("1", NameAt(player["1"]!.AsArray(), 1));
        // a type group only the project has is untouched
        Assert.Equal("1", NameAt(player["16"]!.AsArray(), 1));
    }

    [Fact]
    public async Task ARepositoryListLongerThanTheProjectOnlyGrowsWhereItHasNames() {
        using var fixture = new RepoOverlayFixture();
        await fixture.Project.RepairAsync();
        var before = fixture.ReadProject("asset/json/custom/customBehaviorType.json");

        await OverlayAsync(fixture, fixture.GameRepo);

        // the repository's extra slot has no name, so it carries no information: the list stays as
        // long as the script says (a stub entry the script does not know would fail verify), and the
        // file is not rewritten at all
        Assert.Single(fixture.Project.ListOf("asset/json/custom/customBehaviorType.json"));
        Assert.Equal(before, fixture.ReadProject("asset/json/custom/customBehaviorType.json"));
    }
    
    [Fact]
    public async Task GameSpecificRepositoryReplacesDocumentsVerbatim() {
        using var fixture = new RepoOverlayFixture();
        await fixture.Project.RepairAsync();
        var report = new List<string>();

        await OverlayAsync(fixture, fixture.GameRepo, report);

        Assert.Equal(fixture.ReadRepo(fixture.GameRepo, "json/custom/customGameAttribute.json"),
            fixture.ReadProject("asset/json/custom/customGameAttribute.json"));
        Assert.Equal(fixture.ReadRepo(fixture.GameRepo, "json/custom/condition/condition7.json"),
            fixture.ReadProject("asset/json/custom/condition/condition7.json"));
        Assert.Contains(report, line => line.Contains("(game specific repository of this project)"));
        Assert.Contains(report, line => line.Contains("replaced asset/json/custom/customGameAttribute.json"));
        Assert.Contains(report, line => line.Contains("replaced asset/json/custom/condition/condition7.json"));
    }

    [Fact]
    public async Task ApplyingTheOverlayTwiceLeavesTheFilesByteIdentical() {
        // the merge, and the no repository case, both have to be idempotent
        using var merged = new RepoOverlayFixture();
        await merged.Project.RepairAsync();
        await OverlayAsync(merged, merged.GameRepo);
        var first = merged.HashAllFiles();
        await OverlayAsync(merged, merged.GameRepo);
        Assert.Equal(first, merged.HashAllFiles());

        using var skipped = new RepoOverlayFixture();
        await skipped.Project.RepairAsync();
        await OverlayAsync(skipped, null);
        var skippedFirst = skipped.HashAllFiles();
        await OverlayAsync(skipped, null);
        Assert.Equal(skippedFirst, skipped.HashAllFiles());
    }

    [Fact]
    public async Task TheOverlayOnlySurvivesWhenItRunsAfterTheRepair() {
        // the order the tools use: infer first, then overlay the repository per entry
        using var repairedFirst = new RepoOverlayFixture();
        await repairedFirst.Project.RepairAsync();
        await OverlayAsync(repairedFirst, repairedFirst.GameRepo);
        var parts = repairedFirst.Project.ListOf("asset/json/standAvatar/standAvatarPartList.json");
        Assert.Equal("仓库部件", NameAt(parts, 2));

        // the old order: the repair rebuilds that list and the repository's name is gone
        using var overlaidFirst = new RepoOverlayFixture();
        await OverlayAsync(overlaidFirst, overlaidFirst.GameRepo);
        await overlaidFirst.Project.RepairAsync();
        parts = overlaidFirst.Project.ListOf("asset/json/standAvatar/standAvatarPartList.json");
        Assert.Equal("2", NameAt(parts, 2));
    }

    [Fact]
    public void ResolvesTheProjectsOwnRepositoryOrNothingAtAll() {
        using var fixture = new RepoOverlayFixture();

        // an explicitly given repository is used as it is
        var given = RepoOverlayService.Resolve(fixture.GameProjectName, fixture.GameRepo);
        Assert.Equal(Path.GetFullPath(fixture.GameRepo), given.Path);
        Assert.Contains("--local_source", given.Source);

        // without one, the project's own repository inside file_repo - which this fixture does not
        // materialize, so there is nothing to use (the found case is covered by the test above)
        var own = RepoOverlayService.Resolve(fixture.GameProjectName, null);
        Assert.Null(own.Path);
        Assert.Contains("file_repo/" + fixture.GameProjectName + " not found", own.Source);

        // no repository named after the project: nothing to use, and no Default fallback
        var none = RepoOverlayService.Resolve("NoSuchGame0098", null);
        Assert.Null(none.Path);
        Assert.Contains("file_repo/NoSuchGame0098 not found", none.Source);

        // an explicit path that does not exist does not fall back either
        var missing = RepoOverlayService.Resolve("NoSuchGame0098",
            Path.Combine(fixture.Root, "repos", "nope"));
        Assert.Null(missing.Path);
        Assert.Contains("not found", missing.Source);
    }

    [Fact]
    public async Task ThrowsWhenTheFolderIsNotAProject() {
        using var fixture = new RepoOverlayFixture();
        var empty = Path.Combine(fixture.Root, "not-a-project");
        Directory.CreateDirectory(empty);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            new RepoOverlayService().ApplyAsync(empty, fixture.GameRepo, _ => Task.CompletedTask));
    }
}
