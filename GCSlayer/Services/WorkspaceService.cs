using CliFx.Infrastructure;
using GCSlayer.Models;

namespace GCSlayer.Services;

public class WorkspaceService(IConsole console) {
    public async Task SetupWorkspace(string projectPath, string gamePath) {
        if (Directory.Exists(projectPath)) {
            await console.Output.WriteLineAsync("- Delete previous project");
            await FileService.DeleteDirectoryAsync(projectPath);
        }
        Directory.CreateDirectory(projectPath);
        if (!Directory.Exists(Constants.TemplatePath)) {
            throw new FileNotFoundException("Template not found, run GCSlayer init first.");
        }
        await console.Output.WriteLineAsync("- Copy template");
        await FileService.CopyDirectoryAsync(Constants.TemplatePath, projectPath);
        await FileService.DeleteDirectoryAsync(Path.Combine(projectPath, "asset"));
        await console.Output.WriteLineAsync("- Copy game assets");
        await FileService.CopyDirectoryAsync(Path.Combine(gamePath, "asset"), Path.Combine(projectPath, "asset"));
    }
}
