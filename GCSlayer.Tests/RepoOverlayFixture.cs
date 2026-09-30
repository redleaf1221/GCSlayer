using System.Security.Cryptography;
using GCSlayer.Models;

namespace GCSlayer.Tests;

/// <summary>
/// A repaired project (the miniature recovered project of <see cref="RecoveredProjectFixture"/>) plus
/// a repository holding the real names the release deleted, which is what the overlay has to put back
/// entry by entry. The repository is the project's own: its directory name is the project's
/// gameProjectName, and it lives inside <c>file_repo</c> like the one <c>fixup</c> resolves.
///
/// It is only materialized for the tests that resolve it without an explicit path (see
/// <see cref="WithToolRepoAsync"/>); every other test passes <see cref="GameRepo"/> explicitly.
/// </summary>
internal sealed class RepoOverlayFixture : IDisposable {
    /// <summary>Unique per fixture: the tool level repository must not be shared between test runs.</summary>
    public string GameProjectName { get; } = "OverlayGame" + Guid.NewGuid().ToString("N")[..10];

    public RecoveredProjectFixture Project { get; } = new();
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "gcs-repo-" + Guid.NewGuid().ToString("N"));
    /// <summary>Content directory of the repository, used as the explicit path by most tests.</summary>
    public string GameRepo { get; }
    /// <summary>Where the overlay looks the project's own repository up when no path is given.</summary>
    public string GameRepoPath => Path.Combine(Constants.FileRepoPath, GameProjectName);

    public RepoOverlayFixture() {
        GameRepo = Path.Combine(Root, "repos", GameProjectName);
        Project.Write("asset/json/config.json", $$"""{"gameProjectName":"{{GameProjectName}}"}""");
        WriteRepo(GameRepo);
    }

    public void Dispose() {
        Project.Dispose();
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Materializes the repository where the tool looks it up (<c>file_repo/&lt;gameProjectName&gt;</c>)
    /// and runs the test against it, removing it again afterwards.
    /// </summary>
    public async Task WithToolRepoAsync(Func<Task> body) {
        if (Directory.Exists(GameRepoPath))
            Directory.Delete(GameRepoPath, true);
        Directory.CreateDirectory(GameRepoPath);
        try {
            WriteRepo(GameRepoPath);
            await body();
        } finally {
            if (Directory.Exists(GameRepoPath))
                Directory.Delete(GameRepoPath, true);
        }
    }

    /// <summary>The repository file exactly as it sits in the repository directory.</summary>
    public string ReadRepo(string repo, string relative) =>
        File.ReadAllText(Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar)));

    public string ReadProject(string relative) => File.ReadAllText(Project.Path_(relative));

    /// <summary>SHA256 of every file of the project, keyed by relative path.</summary>
    public Dictionary<string, string> HashAllFiles() =>
        Directory.GetFiles(Project.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Project.Root, path),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    private static void WriteRepo(string repo) {
        // real names for ids the game left blank, and longer than the derivation produces
        WriteRepo(repo, "json/variable/variable.json", """
            {
              "list": {
                "1": [null, "生命值", "魔法值", "力量", "敏捷", "智力"],
                "12": [null, ""],
                "7": [null, "仓库独有类型"]
              },
              "type": { "1": "数值", "12": "保留类型", "7": "仓库类型" }
            }
            """);
        // shorter than the project's list: merging must not shrink it
        WriteRepo(repo, "json/variable/string.json", """{"list":{"1":[null]}}""");
        // a flat list where the repository names one entry and leaves another one empty
        WriteRepo(repo, "json/standAvatar/standAvatarPartList.json",
            """{"list":[null,"","仓库部件"]}""");
        // a repository list that is longer than the project's but carries no name in that slot,
        // exactly like the Default stub of the script derived extension lists
        WriteRepo(repo, "json/custom/customBehaviorType.json", """{"list":[null,""]}""");
        // a document the project already has with different content
        WriteRepo(repo, "json/custom/customGameAttribute.json", """
            {
              "worldAttributeSetting": { "blockList": [{ "name": "仓库世界属性" }] },
              "playerAttributeSetting": { "blockList": [] },
              "worldAttributeConfig": { "attrs": {} },
              "playerAttributeConfig": { "attrs": {} },
              "k1": "repo-k1"
            }
            """);
        // an extension document with non-empty blockAttrs
        WriteRepo(repo, "json/custom/condition/condition7.json", """
            {
              "compAutoOrderLimitHeight": 0,
              "id": 7,
              "blockList": [{
                "name": "仓库的块",
                "blockAttrs": [{
                  "attr": {
                    "id": "1_0.5",
                    "varName": "repoOpt",
                    "varType": 0,
                    "compData": { "compType": 2, "compParam": { "items": "甲,乙" } },
                    "defaultValue": ""
                  }
                }],
                "blockCondition": [],
                "blockHeight": 50,
                "autoOrder": true,
                "hideBlockTitle": false
              }],
              "isCustomPhraseDesc": false
            }
            """);
        // a document no other source provides: the overlay has to add it
        WriteRepo(repo, "json/ui/uiCompSetting.json", """{"compSetting":{}}""");
    }

    private static void WriteRepo(string repo, string relative, string content) {
        var path = Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
