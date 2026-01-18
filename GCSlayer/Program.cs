using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using GCSlayer;
using GCSlayer.Models;
using GCSlayer.Services;
using Spectre.Console;

await new CliApplicationBuilder()
    .SetExecutableName("GCSlayer")
    .AddCommand<RecoverCommand>()
    .Build()
    .RunAsync();

namespace GCSlayer {
    [Command("recover", Description = "Recover project from released GameCreator game.")]
    public class RecoverCommand : ICommand {
        [CommandParameter(0, Name = "game_path", Description = "Path to game install.")]
        public required string GamePath { get; init; }
    
        [CommandOption("local_source", Description = "Path for local source of missing assets.")]
        public string? LocalSourcePath { get; init; }
    
        [CommandOption("missing_list", Description = "Path for generating list of missing file.")]
        public string? MissingListPath { get; init; }
    
        [CommandOption("output", 'o', Description = "Output path.")]
        public string? OutputPath { get; init; }
    
        public async ValueTask ExecuteAsync(IConsole console) {
            try {
                if (!await ScriptDecrypt.CheckNodeExists()) {
                    AnsiConsole.MarkupLine("[red bold]Couldn't find NodeJs![/]");
                    return;
                }
                var context = new OperationContext {
                    GamePath = Path.GetFullPath(GamePath),
                    OutputPath = OutputPath ?? Path.GetFileName(GamePath),
                    LocalSourcePath = LocalSourcePath != null ? Path.GetFullPath(LocalSourcePath) : null,
                    MissingListPath = MissingListPath != null ? Path.GetFullPath(MissingListPath) : null
                };
                await new RecoverOrchestrator(context).ExecuteAsync();
                await new InferOrchestrator(context).ExecuteAsync();
            } catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red bold]{ex.ToString().EscapeMarkup()}[/]");
            }
        }
    }
}
