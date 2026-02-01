using CliFx.Infrastructure;
using GCSlayer.Models;

namespace GCSlayer.Services;

public class InitFlow(IConsole console, InitParameter parameter) {
    private static readonly string[] TargetPrefixes = [
        "json/animation/animationList.json",
        "json/animation/animationSignalList.json",
        "json/avatar/avatarActList.json",
        "json/avatar/avatarList.json",
        "json/avatar/avatarPartList.json",
        "json/avatar/avatarRefObjList.json",
        "json/custom/customBehaviorType.json",
        "json/custom/customCondition.json",
        "json/custom/customGameAttribute.json",
        "json/custom/customGameNumberList.json",
        "json/custom/customGameStringList.json",
        "json/custom/customValueFunction.json",
        "json/custom/customDataDisplayList.json",
        "json/server/variable/string.json",
        "json/server/variable/switch.json",
        "json/server/variable/variable.json",
        "json/server/serverConfig.json",
        "json/standAvatar/expressionList.json",
        "json/standAvatar/standAvatarList.json",
        "json/standAvatar/standAvatarPartList.json",
        "json/ui/uiCompSetting.json",
        "json/variable/string.json",
        "json/variable/switch.json",
        "json/variable/variable.json"
    ];
    
    public async Task ExecuteAsync() {
        await console.Output.WriteLineAsync("Patch IDE");
        var idePatcher = new IdePatcher(console);
        await idePatcher.PatchIdeScript(parameter.IdeScriptPath);
        await idePatcher.PatchCoreTemplate(parameter.CoreTemplatePath);
        await console.Output.WriteLineAsync("Copy core template to local");
        if (Directory.Exists(Constants.TemplatePath)) {
            await FileService.DeleteDirectoryAsync(Constants.TemplatePath);
        }
        await FileService.CopyDirectoryAsync(parameter.CoreTemplatePath, Constants.TemplatePath);
        await console.Output.WriteLineAsync("Copy essential files to file repo");
        if (Directory.Exists(Constants.DefaultRepoPath)) {
            await FileService.DeleteDirectoryAsync(Constants.DefaultRepoPath);
        }
        foreach (var file in TargetPrefixes) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Constants.DefaultRepoPath, file))!);
            if (File.Exists(Path.Combine(Constants.TemplatePath, "asset", file))) {
                File.Copy(Path.Combine(Constants.TemplatePath, "asset", file),
                    Path.Combine(Constants.DefaultRepoPath, file), true);
            }
        }
        await console.Output.WriteLineAsync("Done");
    }
}
