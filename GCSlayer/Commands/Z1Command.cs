using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using GCSlayer.Services;

namespace GCSlayer.Commands;

[Command("z1", Description = "Decrypt a specific crypto method for game save files.")]
public class Z1Command : ICommand {
    [CommandParameter(0, Name = "z1", Description = "Z1 string.")]
    public required string Z1 { get; init; }

    public async ValueTask ExecuteAsync(IConsole console) {
        try {
            await console.Output.WriteLineAsync(SaveFileCryptoService.PasswordFromZ1(Z1));
        } catch (Exception ex) {
            await console.Error.WriteLineAsync(ex.ToString());
        }
    }
}
