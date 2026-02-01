using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using GCSlayer.Models;
using GCSlayer.Services;

namespace GCSlayer.Commands;

[Command("init", Description = "Init the GCSlayer, patch IDE, fetch template.")]
public class InitCommand : ICommand {
    [CommandParameter(0, Name = "ide_path", Description = "Path to IDE.")]
    public required string IdePath { get; init; }

    public async ValueTask ExecuteAsync(IConsole console) {
        try {
            if (!await ScriptDecrypt.CheckNodeExists()) {
                await console.Output.WriteLineAsync("Node.Js not found!");
                return;
            }
            var parameter = new InitParameter {
                IdePath = Path.GetFullPath(IdePath)
            };
            await new InitFlow(console, parameter).ExecuteAsync();
        } catch (Exception ex) {
            await console.Error.WriteLineAsync(ex.ToString());
        }
    }
}
