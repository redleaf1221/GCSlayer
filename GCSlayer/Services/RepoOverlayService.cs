using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using GCSlayer.Models;
using GCSlayer.Models.JsonModel;

namespace GCSlayer.Services;

/// <summary>The repository a project is overlaid with, or the fact that there is none.</summary>
/// <param name="Path">The repository root, or null when the project has no repository of its own.</param>
/// <param name="Source">Factual description of how the directory was chosen.</param>
public sealed record RepoResolution(string? Path, string Source);

/// <summary>
/// Overlays the local file repository onto a project that has just been repaired.
///
/// The repository holds information that came from somewhere else than the release (a game specific
/// dump, or hand made work on one), so it is applied **after** the repair and entry by entry: the
/// repair derives what the shipped data still allows, and the repository then fills in the names the
/// release deleted. Applying it before the repair, or only copying files that are missing, throws
/// that information away.
///
/// Only a repository that belongs to this game is used here: an explicit <c>--local_source</c>, or
/// <c>file_repo/&lt;gameProjectName&gt;</c>. <c>file_repo/Default</c> is the plain template, unrelated
/// to the game, so it never contributes entries - merging its names, type groups or documents would
/// move the project away from the game. The template only feeds the fill step
/// (<see cref="FileService.CopyDirectoryAsync"/> with <c>overwriteExisting: false</c>), which merely
/// makes sure the container files exist.
///
/// Rules:
///
///  - a json whose content is a name list (flat <c>{"list":[null,name…]}</c> or typed
///    <c>{"list":{"type":[null,name…]},"type":{…}}</c>) is merged per entry, per type. A non empty
///    repository name wins over the project's entry; an empty one never replaces an existing entry.
///    The project's entries are kept, the array never shrinks, and the repository's <c>type</c>
///    entries are added next to the existing ones;
///  - every other file is a document of this game, so it wins verbatim over a repaired document;
///  - nothing is ever deleted, and applying the overlay twice writes byte identical files.
/// </summary>
public class RepoOverlayService {
    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Picks the repository: an explicit path (--local_source) first, then the game's own
    /// <c>file_repo/&lt;gameProjectName&gt;</c>. There is no fallback: a repository is only used when
    /// it is the game's own or explicitly given, and <see cref="RepoResolution.Path"/> is null
    /// otherwise, which makes the overlay a no-op.
    /// </summary>
    public static RepoResolution Resolve(string? gameProjectName, string? explicitRepoPath) {
        if (!string.IsNullOrWhiteSpace(explicitRepoPath)) {
            var given = explicitRepoPath.Trim();
            var explicitPath = Path.GetFullPath(given);
            if (Directory.Exists(explicitPath))
                return new RepoResolution(explicitPath, $"--local_source {given}");
        }

        var name = gameProjectName?.Trim() ?? "";
        if (name.Length > 0) {
            var named = Path.Combine(Constants.FileRepoPath, name);
            if (Directory.Exists(named))
                return new RepoResolution(named,
                    $"file_repo/{name} (gameProjectName of asset/json/config.json)");
        }

        var missing = !string.IsNullOrWhiteSpace(explicitRepoPath)
            ? $"--local_source {explicitRepoPath.Trim()} not found; "
            : name.Length == 0
                ? "asset/json/config.json has no gameProjectName; "
                : $"file_repo/{name} not found; ";
        return new RepoResolution(null,
            missing + "no game specific repository");
    }

    /// <summary>
    /// Applies the game's own repository to a repaired project: list files are merged entry by entry,
    /// its documents replace repaired ones verbatim. When the project has no such repository the
    /// overlay does nothing and says so. Progress goes to <paramref name="report"/>.
    /// </summary>
    public async Task ApplyAsync(string projectPath, string? explicitRepoPath, Func<string, Task> report) {
        var fullPath = Path.GetFullPath(projectPath);
        var jsonRoot = Path.Combine(fullPath, "asset", "json");
        if (!Directory.Exists(jsonRoot))
            throw new DirectoryNotFoundException($"asset/json not found, not a project: {fullPath}");

        var gameProjectName = await ReadGameProjectNameAsync(fullPath);
        var resolution = Resolve(gameProjectName, explicitRepoPath);
        if (resolution.Path is null) {
            await report($"- repo overlay: {resolution.Source}; skipped, the project is left as "
                + "repaired and no entry is taken from file_repo/Default");
            return;
        }

        var assetRoot = Path.Combine(fullPath, "asset");
        var listFiles = 0;
        var replaced = 0;
        var added = 0;
        var kept = 0;
        var replacements = new List<string>();

        var sources = Directory.GetFiles(resolution.Path, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);
        foreach (var source in sources) {
            var relative = Path.GetRelativePath(resolution.Path, source);
            var destination = Path.Combine(assetRoot, relative);
            if (!TryLoadNameList(source, out var repoList)) {
                // a document of this game: it wins verbatim over the repaired one, and a document the
                // project is missing is added
                if (!File.Exists(destination)) {
                    Copy(source, destination);
                    added++;
                } else if (!SameContent(source, destination)) {
                    Copy(source, destination);
                    replaced++;
                    replacements.Add(relative.Replace('\\', '/'));
                } else {
                    kept++;
                }
                continue;
            }

            listFiles++;
            if (!File.Exists(destination)) {
                Copy(source, destination);
                continue;
            }
            MergeNameList(source, destination, repoList);
        }

        await report($"- repo overlay source: {resolution.Source}");
        await report($"- repo overlay: {resolution.Path} "
            + $"(game specific repository of this project), "
            + $"{listFiles} list file(s) merged entry by entry, "
            + $"{replaced} document(s) replaced, {added} added, {kept} kept");
        foreach (var relative in replacements)
            await report($"- repo overlay: replaced asset/{relative} with the game specific repository copy");
    }

    // ---------------------------------------------------------------- name lists

    /// <summary>
    /// True for a json whose content is a name list: a flat <c>list</c> array, or a typed one whose
    /// <c>list</c> object holds one array per type. Every entry has to be a string or null, so a
    /// document that happens to carry a <c>list</c> of objects stays a document.
    /// </summary>
    private static bool TryLoadNameList(string path, out JsonObject root) {
        root = new JsonObject();
        JsonNode? node;
        try {
            node = JsonNode.Parse(File.ReadAllText(path));
        } catch (Exception exception) when (exception is IOException or JsonException) {
            return false;
        }
        if (node is not JsonObject json || json["list"] is null)
            return false;
        switch (json["list"]) {
            case JsonArray flat when IsNameArray(flat):
            case JsonObject typed when IsTypedNameList(typed):
                root = json;
                return true;
            default:
                return false;
        }
    }

    private static bool IsTypedNameList(JsonObject typed) {
        foreach (var pair in typed)
            if (pair.Value is not JsonArray array || !IsNameArray(array))
                return false;
        return true;
    }

    private static bool IsNameArray(JsonArray array) =>
        array.All(entry => entry is null || entry.GetValueKind() == JsonValueKind.String);

    /// <summary>
    /// Merges the repository's entries into the project's file: per entry a non empty repository name
    /// wins, an empty or absent one keeps the project's entry, and the array is never shortened. A
    /// slot the overlay adds without a name from either side is stubbed with its index, the same
    /// convention the repair uses, so a list never ends up with a nameless entry.
    /// </summary>
    private static void MergeNameList(string source, string destination, JsonObject repoRoot) {
        JsonObject? original;
        try {
            original = JsonNode.Parse(File.ReadAllText(destination)) as JsonObject;
        } catch (Exception exception) when (exception is IOException or JsonException) {
            original = null;
        }

        // an unreadable project file cannot be merged into; the repository's copy is all there is
        if (original is null) {
            Copy(source, destination);
            return;
        }

        var projectRoot = (JsonObject)original.DeepClone();
        if (repoRoot["list"] is JsonArray repoFlat) {
            projectRoot["list"] = MergeArray(projectRoot["list"] as JsonArray, repoFlat);
        } else if (repoRoot["list"] is JsonObject repoTypes) {
            if (projectRoot["list"] is not JsonObject projectTypes) {
                var typed = new JsonObject();
                foreach (var pair in repoTypes)
                    typed[pair.Key] = MergeArray(null, pair.Value as JsonArray ?? []);
                projectRoot["list"] = typed;
            } else {
                foreach (var pair in repoTypes) {
                    // replacing a value keeps the key's position, so existing type groups stay first
                    if (pair.Value is JsonArray repoArray)
                        projectTypes[pair.Key] = MergeArray(projectTypes[pair.Key] as JsonArray, repoArray);
                }
            }

            if (repoRoot["type"] is JsonObject repoTypeMap) {
                if (projectRoot["type"] is not JsonObject projectTypeMap) {
                    projectTypeMap = new JsonObject();
                    projectRoot["type"] = projectTypeMap;
                }
                foreach (var pair in repoTypeMap)
                    if (!projectTypeMap.ContainsKey(pair.Key))
                        projectTypeMap[pair.Key] = pair.Value?.DeepClone();
            }
        }

        // nothing the repository knows changed: keep the project's file (and its exact bytes)
        if (JsonNode.DeepEquals(original, projectRoot))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, projectRoot.ToJsonString(WriteOptions));
    }

    private static JsonArray MergeArray(JsonArray? project, JsonArray repo) {
        var projectLength = project?.Count ?? 0;
        var length = Math.Max(projectLength, repo.Count);
        var merged = new JsonArray();
        for (var index = 0; index < length; index++) {
            var repoName = NameAt(repo, index);
            var projectEntry = project is not null && index < projectLength
                ? project[index]?.DeepClone()
                : null;
            merged.Add(string.IsNullOrEmpty(repoName) ? projectEntry : JsonValue.Create(repoName));
        }

        // trailing repository slots without a name carry no information: keep the project's length
        var floor = Math.Max(projectLength, 1);
        while (merged.Count > floor && IsBlank(merged[^1]))
            merged.RemoveAt(merged.Count - 1);

        // a slot the overlay added without a name gets the index stub the repair writes
        for (var index = Math.Max(projectLength, 1); index < merged.Count; index++)
            if (IsBlank(merged[index]))
                merged[index] = JsonValue.Create(index.ToString());

        return merged;
    }

    private static string? NameAt(JsonArray array, int index) =>
        index < array.Count && array[index] is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;

    private static bool IsBlank(JsonNode? node) =>
        node is null || (node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            && string.IsNullOrEmpty(value.GetValue<string>()));

    // ---------------------------------------------------------------- helpers

    private static async Task<string> ReadGameProjectNameAsync(string projectPath) {
        var path = Path.Combine(projectPath, "asset", "json", "config.json");
        if (!File.Exists(path))
            return "";
        try {
            return (await ConfigJson.FromFileAsync(path)).GameProjectName;
        } catch (Exception exception) when (exception is IOException or JsonException) {
            return "";
        }
    }

    private static void Copy(string source, string destination) {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static bool SameContent(string first, string second) {
        var left = new FileInfo(first);
        var right = new FileInfo(second);
        if (left.Length != right.Length)
            return false;
        return File.ReadAllBytes(first).AsSpan().SequenceEqual(File.ReadAllBytes(second));
    }
}
