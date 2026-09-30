using CliFx.Infrastructure;
using GCSlayer.Models;
using GCSlayer.Models.JsonModel;

namespace GCSlayer.Services;

public class FullRecoveryFlow(IConsole console, RecoverParameter parameter) {
    public async Task ExecuteAsync() {
        await console.Output.WriteLineAsync("Set up workspace");
        await new WorkspaceService(console).SetupWorkspace(parameter.ProjectPath, parameter.GamePath);

        await console.Output.WriteLineAsync("Decrypt script");
        var gameScript = await new ScriptDecrypt(console).DecryptScriptAsync(Path.Combine(parameter.GamePath, "script.js"));
        await File.WriteAllTextAsync(Path.Combine(parameter.ProjectPath, "Game", "GCMain.ts"),
            ScriptDecrypt.RemoveUnessentialCode(gameScript));
        EncryptionStatus status = EncryptionStatus.FromScriptAnalysis(gameScript);
        await console.Output.WriteLineAsync("- Encryption status: ");
        await console.Output.WriteLineAsync($"- - Image: {status.ImageEncrypted}");
        await console.Output.WriteLineAsync($"- - Json: {status.JsonEncrypted}");
        await console.Output.WriteLineAsync($"- - Audio: {status.AudioEncrypted}");
        await console.Output.WriteLineAsync($"- - Video: {status.VideoEncrypted}");

        await console.Output.WriteLineAsync("Decrypt assets");
        await new AssetCryptoService(console).DecryptAllAssetsAsync(parameter.AssetsPath, status);

        await console.Output.WriteLineAsync("Extract startup.json");
        await new StartupCryptoService(console).ExtractStartupJsonAsync(parameter.ProjectPath);

        ConfigJson configJson = await ConfigJson.FromFileAsync(Path.Combine(parameter.AssetsPath, "json", "config.json"));
        File.Move(Path.Combine(parameter.ProjectPath, "GameCreatorBin.gamecreator"),
            Path.Combine(parameter.ProjectPath, $"{configJson.GameProjectName}.gamecreator"));

        await console.Output.WriteLineAsync("Copy repo (fill: only files the release deleted)");
        var repoPath = parameter.LocalSourcePath ?? Path.Combine(Constants.FileRepoPath, configJson.GameProjectName);
        if (!Directory.Exists(repoPath)) repoPath = Constants.DefaultRepoPath;
        // Fill: only add the files the release deleted, never replace what the game did ship. The
        // template repository (file_repo/Default) is fine here, because it only supplies container
        // files and template libraries. This runs before the repair because the repair reads several
        // of these files (and keeps the k1 signature they carry); the game's own real content is
        // applied again, entry by entry, after the repair (step "Overlay repo" below).
        await FileService.CopyDirectoryAsync(repoPath, parameter.AssetsPath, overwriteExisting: false);

        await console.Output.WriteLineAsync("Repair lists");
        await new ProjectRepairService(console).RepairAsync(parameter.ProjectPath);

        await console.Output.WriteLineAsync("Overlay repo (game specific repository only)");
        await new RepoOverlayService().ApplyAsync(parameter.ProjectPath, parameter.LocalSourcePath,
            message => console.Output.WriteLineAsync(message));

        await console.Output.WriteLineAsync("Verify");
        await new ProjectVerifier(console).VerifyAsync(parameter.ProjectPath);

        await console.Output.WriteLineAsync("Done");
    }
}
