using System.Text.Json;
using CliFx.Infrastructure;
using GCSlayer.Models;
using GCSlayer.Models.JsonModel;

namespace GCSlayer.Services;

public class FullRecoveryFlow(IConsole console, RecoverParameter parameter) {
    public async Task ExecuteAsync() {
        await console.Output.WriteLineAsync("Set up workspace");
        await new WorkspaceService(console).SetupWorkspace(parameter.ProjectPath, parameter.GamePath);

        await console.Output.WriteLineAsync("Decrypt script");
        var gameScript = await new ScriptDecrypt(console).DecryptScriptAsync(Path.Combine(parameter.GamePath, "script.js"),
            Path.Combine(parameter.ProjectPath, "Game", "GCMain.ts"));
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
        File.Move(Path.Combine(parameter.ProjectPath, "template_project.gamecreator"),
            Path.Combine(parameter.ProjectPath, $"{configJson.GameProjectName}.gamecreator"));
        await console.Output.WriteLineAsync("Extraction done");
        await console.Output.WriteLineAsync();
        
        // AnsiConsole.MarkupLine("[yellow]Infer missing assets...[/]");
        //
        // await AnsiConsole.Progress().StartAsync(async ctx => {
        //     ProgressTask task = ctx.AddTask("Copy from repo", maxValue: 1D);
        //     var repoPath = context.LocalSourcePath ?? Path.Combine(Constants.FileRepoPath, configJson.GameProjectName);
        //     await Parallel.ForEachAsync(missingAssets, 
        //         new ParallelOptions{ MaxDegreeOfParallelism = Environment.ProcessorCount / 2}, 
        //         (missingFile, ct) => {
        //             Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(context.ProjectPath, missingFile))!);
        //             if (File.Exists(Path.Combine(repoPath, missingFile))) {
        //                 File.Copy(Path.Combine(repoPath, missingFile),
        //                     Path.Combine(context.ProjectPath, missingFile));
        //             } else if (File.Exists(Path.Combine(Constants.TemplatePath, missingFile))) {
        //                 File.Copy(Path.Combine(Constants.TemplatePath, missingFile),
        //                     Path.Combine(context.ProjectPath, missingFile));
        //             }
        //             task.Increment(1D / missingFile.Length);
        //             return ValueTask.CompletedTask;
        //         });
        //     task.Increment(1D);
        // });
        //
        // await AnsiConsole.Progress().StartAsync(async ctx => {
        //     ProgressTask task = ctx.AddTask("Decrypt meaningless files", maxValue: 1D);
        //     List<string> jsonArr = ["custom/customBehaviorType.json", "avatar/avatarActList.json",
        //         "standAvatar/expressionList.json", "animation/animationSignalList.json"];
        //     foreach (var asset in jsonArr) {
        //         var rawText = await File.ReadAllTextAsync(Path.Combine(context.ProjectPath, "asset", "json", asset));
        //         var decryptedText = TemplateJsonEncryption.Decrypt(rawText);
        //         await File.WriteAllTextAsync(Path.Combine(context.ProjectPath, "asset", "json", asset), decryptedText);
        //         task.Increment(1D / missingAssets.Count);
        //     }
        //     task.Increment(1D);
        // });
        //
        // AnsiConsole.MarkupLine("[red bold]Deeper inference is work In Progress.[/]");
    }
}
