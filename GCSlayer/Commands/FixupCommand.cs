using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using GCSlayer.Services;

namespace GCSlayer.Commands;

[Command("fixup", Description = "Repair a recovered project so the IDE shows no missing list entries.")]
public class FixupCommand : ICommand {
    [CommandParameter(0, Name = "project_path", Description = "Path to the recovered project.")]
    public required string ProjectPath { get; init; }

    public async ValueTask ExecuteAsync(IConsole console) {
        try {
            await new ProjectRepairService(console).RepairAsync(Path.GetFullPath(ProjectPath));
            await console.Output.WriteLineAsync("Done");
        } catch (Exception ex) {
            await console.Error.WriteLineAsync(ex.ToString());
            Environment.ExitCode = 1;
        }
    }
}
