using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using GCSlayer.Services;

namespace GCSlayer.Commands;

[Command("verify", Description = "Check a recovered project for references the IDE cannot resolve.")]
public class VerifyCommand : ICommand {
    [CommandParameter(0, Name = "project_path", Description = "Path to the recovered project.")]
    public required string ProjectPath { get; init; }

    public async ValueTask ExecuteAsync(IConsole console) {
        try {
            var clean = await new ProjectVerifier(console).VerifyAsync(Path.GetFullPath(ProjectPath));
            if (!clean)
                Environment.ExitCode = 1;
        } catch (Exception ex) {
            await console.Error.WriteLineAsync(ex.ToString());
            Environment.ExitCode = 1;
        }
    }
}
