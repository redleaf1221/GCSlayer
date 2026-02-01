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

        await console.Output.WriteLineAsync("Copy repo");
        var repoPath = parameter.LocalSourcePath ?? Path.Combine(Constants.FileRepoPath, configJson.GameProjectName);
        await FileService.CopyDirectoryAsync(repoPath, parameter.AssetsPath);

        await console.Output.WriteLineAsync("Done");
    }
}
