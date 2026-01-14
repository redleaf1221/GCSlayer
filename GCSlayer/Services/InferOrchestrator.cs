using GCSlayer.Models;
using GCSlayer.Models.JsonModel;
using System.Text.Json;
using Spectre.Console;

namespace GCSlayer.Services;

public class InferOrchestrator(OperationContext context) {
    public async Task ExecuteAsync() {
        List<string> missingAssets = await GetMissingAssetsList(context);
        if (missingAssets.Count == 0) {
            AnsiConsole.MarkupLine("[green]What? No missing assets![/]");
            return;
        }
        AnsiConsole.MarkupLine($"Found {missingAssets.Count} missing assets.");
        if (context.MissingListPath != null) {
            var jsonText = JsonSerializer.Serialize(missingAssets, SourceGenJsonContext.Default.ListString);
            await File.WriteAllTextAsync(context.MissingListPath, jsonText);
            AnsiConsole.MarkupLine($"[dim]Missing assets list wrote to {context.MissingListPath}.[/]");
        }
        AnsiConsole.WriteLine();
        
        AnsiConsole.MarkupLine("[yellow]Infer missing assets...[/]");
        
        ConfigJson configJson = await ConfigJson.FromFileAsync(Path.Combine(context.ProjectPath, "asset", "json", "config.json"));

        await AnsiConsole.Progress().StartAsync(async ctx => {
            ProgressTask task = ctx.AddTask("Copy from repo", maxValue: 1D);
            var repoPath = context.LocalSourcePath ?? Path.Combine(OperationContext.FileRepoPath, configJson.GameProjectName);
            await Parallel.ForEachAsync(missingAssets, 
                new ParallelOptions{ MaxDegreeOfParallelism = Environment.ProcessorCount / 2}, 
                (missingFile, ct) => {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(context.ProjectPath, missingFile))!);
                    if (File.Exists(Path.Combine(repoPath, missingFile))) {
                        File.Copy(Path.Combine(repoPath, missingFile),
                            Path.Combine(context.ProjectPath, missingFile));
                    } else if (File.Exists(Path.Combine(OperationContext.TemplatePath, missingFile))) {
                        File.Copy(Path.Combine(OperationContext.TemplatePath, missingFile),
                            Path.Combine(context.ProjectPath, missingFile));
                    }
                    task.Increment(1D / missingFile.Length);
                    return ValueTask.CompletedTask;
                });
            task.Increment(1D);
        });
        
        await AnsiConsole.Progress().StartAsync(async ctx => {
            ProgressTask task = ctx.AddTask("Decrypt meaningless files", maxValue: 1D);
            List<string> jsonArr = ["custom/customBehaviorType.json", "avatar/avatarActList.json",
                "standAvatar/expressionList.json", "animation/animationSignalList.json"];
            foreach (var asset in jsonArr) {
                var rawText = await File.ReadAllTextAsync(Path.Combine(context.ProjectPath, "asset", "json", asset));
                var decryptedText = TemplateJsonEncryption.Decrypt(rawText);
                await File.WriteAllTextAsync(Path.Combine(context.ProjectPath, "asset", "json", asset), decryptedText);
                task.Increment(1D / missingAssets.Count);
            }
            task.Increment(1D);
        });
        
        AnsiConsole.MarkupLine("[red bold]Deeper inference is work In Progress.[/]");
    }

    private static async Task<List<string>> GetMissingAssetsList(OperationContext context) {
        if (!File.Exists(Path.Combine(context.ProjectPath, "asset", "assetList.json"))) 
            throw new DirectoryNotFoundException("assetList.json not found.");
        List<AssetEntryJson> allAssets = await AssetEntryJson.ListFromFileAsync(
            Path.Combine(context.ProjectPath, "asset", "assetList.json"));
        List<string> missingAssets = allAssets
            .Where(entry => !entry.IsDirectory &&
                            !File.Exists(Path.Combine(context.ProjectPath, entry.FileLocalUrl)))
            .Select(entry => entry.FileLocalUrl)
            .ToList();
        return missingAssets;
    }
}
