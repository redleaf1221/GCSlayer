using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using GCSlayer.Models;
using GCSlayer.Services;

namespace GCSlayer.Commands;

[Command("recover", Description = "Recover project from released GameCreator game.")]
public class RecoverCommand : ICommand {
    [CommandParameter(0, Name = "game_path", Description = "Path to game install.")]
    public required string GamePath { get; init; }

    [CommandOption("local_source", Description = "Path for local source of missing assets.")]
    public string? LocalSourcePath { get; init; }

    [CommandOption("output", 'o', Description = "Output path.")]
    public string? OutputPath { get; init; }

    public async ValueTask ExecuteAsync(IConsole console) {
        try {
            if (!await ScriptDecrypt.CheckNodeExists()) {
                await console.Output.WriteLineAsync("Node.Js not found!");
                return;
            }
            var parameter = new RecoverParameter {
                GamePath = Path.GetFullPath(GamePath),
                OutputPath = OutputPath ?? Path.GetFileName(GamePath),
                LocalSourcePath = LocalSourcePath != null ? Path.GetFullPath(LocalSourcePath) : null
            };
            await new FullRecoveryFlow(console, parameter).ExecuteAsync();
        } catch (Exception ex) {
            await console.Error.WriteLineAsync(ex.ToString());
        }
    }
}
