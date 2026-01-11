using GCSlayer.Models;
using GCSlayer.Models.JsonModel;
using System.Text.Json;
using Spectre.Console;

namespace GCSlayer.Services;

public class InferOrchestrator(OperationContext context) {
    public async Task ExecuteAsync() {
        AnsiConsole.MarkupLine("[yellow]Infer missing assets...[/]");
        List<string> missingAssets = await GetMissingAssetsList(context);
        if (context.MissingListPath != null) {
            var jsonText = JsonSerializer.Serialize(missingAssets, SourceGenJsonContext.Default.ListString);
            await File.WriteAllTextAsync(context.MissingListPath, jsonText);
            AnsiConsole.Markup($"[dim]Missing assets list wrote to {context.MissingListPath}.[/]");
        }
        if (missingAssets.Count == 0) {
            AnsiConsole.Markup("[green]What? No missing assets![/]");
            return;
        }
        AnsiConsole.Markup($"Found {missingAssets.Count} missing assets.");
        
        AnsiConsole.Markup("[red bold]Work In Progress[/]");
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
