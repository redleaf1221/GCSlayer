using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using GCSlayer;
using GCSlayer.Models;
using GCSlayer.Services;

await new CliApplicationBuilder()
    .AddCommand<RecoverCommand>()
    .Build()
    .RunAsync();

namespace GCSlayer {
    [Command("recover", Description = "Recover project from released GameCreator game.")]
    public class RecoverCommand : ICommand {
        [CommandParameter(0, Name = "game_path", Description = "Path to game install.")]
        public required string GamePath { get; init; }
    
        [CommandOption("local_source", 's', Description = "Local source of missing assets.")]
        public string? LocalSource { get; init; }
    
        [CommandOption("gen_missing_list", Description = "Path for generating list of missing file.")]
        public string? MissingListPath { get; init; }
    
        [CommandOption("output", 'o', Description = "Output path.")]
        public string? OutputPath { get; init; }
    
        public async ValueTask ExecuteAsync(IConsole console) {
            try {
                var context = new OperationContext {
                    GamePath = Path.GetFullPath(GamePath),
                    OutputPath = OutputPath ?? Path.GetFileName(GamePath),
                    LocalSource = LocalSource != null ? Path.GetFullPath(LocalSource) : null,
                    MissingListPath = MissingListPath != null ? Path.GetFullPath(MissingListPath) : null
                };
                await new RecoverOrchestrator(context).ExecuteAsync();
                await new InferOrchestrator(context).ExecuteAsync();
            } catch (Exception ex) {
                await console.Error.WriteLineAsync(ex.Message);
            }
        }
    }
}
