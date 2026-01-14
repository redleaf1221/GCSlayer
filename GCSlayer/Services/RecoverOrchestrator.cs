using GCSlayer.Models;
using GCSlayer.Models.JsonModel;
using GCSlayer.Services.AssetsDecrypt;
using Spectre.Console;

namespace GCSlayer.Services;

public class RecoverOrchestrator(OperationContext context) {
    public async Task ExecuteAsync() {
        AnsiConsole.MarkupLine($"[green]Processing game: {context.GamePath}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Copy all files...[/]");
        await AnsiConsole.Progress().StartAsync(async ctx => {
            if (Directory.Exists(context.ProjectPath)) {
                ProgressTask cleanTask = ctx.AddTask("Clean previous project", maxValue: 1D);
                await FileService.DeleteDirectoryAsync(context.ProjectPath,
                    add => cleanTask.Increment(add));
            }
            Directory.CreateDirectory(context.ProjectPath);
            ProgressTask templateTask = ctx.AddTask("Copy template", maxValue: 1D);
            await FileService.CopyDirectoryAsync(
                OperationContext.TemplatePath,
                context.ProjectPath, add => templateTask.Increment(add));
            await FileService.DeleteDirectoryAsync(Path.Combine(context.ProjectPath, "asset"));
            ProgressTask assetTask = ctx.AddTask("Copy game assets", maxValue: 1D);
            await FileService.CopyDirectoryAsync(
                Path.Combine(context.GamePath, "asset"),
                context.AssetsPath, add => assetTask.Increment(add));
        });
        
        AnsiConsole.MarkupLine("[yellow]Decrypt script...[/]");
        EncryptionStatus encryptionStatus = await ScriptDecrypt.DecryptScriptAsync(
            Path.Combine(context.GamePath, "script.js"),
            Path.Combine(context.ProjectPath, "Game", "GCMain.ts")
        );
        {
            var table = new Table();
            table.AddColumns("ImageEncrypt", "JsonEncrypt", "AudioEncrypt", "VideoEncrypt");
            table.AddRow(
                encryptionStatus.ImageEncrypted.ToString(),
                encryptionStatus.JsonEncrypted.ToString(),
                encryptionStatus.AudioEncrypted.ToString(),
                encryptionStatus.VideoEncrypted.ToString()
            );
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }
        
        AnsiConsole.MarkupLine("[yellow]Decrypt assets...[/]");
        await AnsiConsole.Progress().StartAsync(async ctx => {
            List<Task> operations = [];
            if (encryptionStatus.ImageEncrypted) {
                ProgressTask imageTask = ctx.AddTask("Decrypt image", maxValue: 1D);
                operations.Add(ImageDecrypt.DecryptImageAsync(Path.Combine(context.ProjectPath, "asset", "image"),
                    add => imageTask.Increment(add)));
            }
            if (encryptionStatus.JsonEncrypted) {
                ProgressTask plainJsonTask = ctx.AddTask("Decrypt plain json", maxValue: 1D);
                operations.Add(PlainJsonDecrypt.DecryptPlainJsonAsync(Path.Combine(context.ProjectPath, "asset", "json"),
                    add => plainJsonTask.Increment(add)));
            }
            if (encryptionStatus.AudioEncrypted) {
                ProgressTask audioTask = ctx.AddTask("Decrypt audio", maxValue: 1D);
                operations.Add(AudioDecrypt.DecryptAudioAsync(Path.Combine(context.ProjectPath, "asset", "audio"),
                    add => audioTask.Increment(add)));
            }
            if (encryptionStatus.VideoEncrypted) {
                ProgressTask videoTask = ctx.AddTask("Decrypt video", maxValue: 1D);
                operations.Add(VideoDecrypt.DecryptVideoAsync(Path.Combine(context.ProjectPath, "asset", "video"),
                    add => videoTask.Increment(add)));
            }
            
            await Task.WhenAll(operations);
            
            ProgressTask startupJsonTask = ctx.AddTask("Extract startup.json", maxValue: 1D);
            await StartupJsonDecrypt.DecryptStartupJsonAsync(context.ProjectPath,
                add => startupJsonTask.Increment(add));
        });
        ConfigJson configJson = await ConfigJson.FromFileAsync(Path.Combine(context.ProjectPath, "asset", "json", "config.json"));
        File.Move(Path.Combine(context.ProjectPath, "template_project.gamecreator"),
            Path.Combine(context.ProjectPath, $"{configJson.GameProjectName}.gamecreator"));
        
        AnsiConsole.MarkupLine("[green]Major recovery finished.[/]");
        AnsiConsole.WriteLine();
    }
}
