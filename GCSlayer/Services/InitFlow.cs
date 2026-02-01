using CliFx.Infrastructure;
using GCSlayer.Models;

namespace GCSlayer.Services;

public class InitFlow(IConsole console, InitParameter parameter) {
    public async Task ExecuteAsync() {
        await console.Output.WriteLineAsync("Patch IDE");
        var idePatcher = new IdePatcher(console);
        await idePatcher.PatchIdeScript(parameter.IdeScriptPath);
        await idePatcher.PatchCoreTemplate(parameter.CoreTemplatePath);
        await console.Output.WriteLineAsync("Copy core template to local");
        await FileService.CopyDirectoryAsync(parameter.CoreTemplatePath, Constants.TemplatePath);
        await console.Output.WriteLineAsync("Done");
    }
}
