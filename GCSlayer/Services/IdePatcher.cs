using System.Text.RegularExpressions;
using CliFx.Infrastructure;

namespace GCSlayer.Services;

public partial class IdePatcher(IConsole console) {
    [GeneratedRegex(@"(function chefdsjfiroqwjkgfd\(ss, onFin\) {)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ScriptPatch_verify();
    
    [GeneratedRegex(@"(function localInterval\(\) {)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ScriptPatch_release();
    
    [GeneratedRegex(@"(static get ownSoft\(\) {)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ScriptPatch_own();
    
    [GeneratedRegex(@"(static fdsjkwuirewio\(str, force\) {)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ScriptPatch_crypt();

    private static readonly string[] TemplateScripts = [
        @"GameCreator\script.js",
        @"behaviorView\script.js"
    ];
    

    public async Task PatchIdeScript(string scriptPath) {
        await console.Output.WriteLineAsync("- Patch IDE script");
        if (!await ScriptDecrypt.IsGcjsEncrypted(scriptPath)) {
            await console.Output.WriteLineAsync("- Already patched, skipping");
            return;
        }
        File.Copy(scriptPath, scriptPath + ".bak");
        var script = await new ScriptDecrypt(console).DecryptScriptAsync(scriptPath);

        await console.Output.WriteLineAsync("- - Patch 'verify'");
        if (!ScriptPatch_verify().IsMatch(script)) {
            throw new Exception("Patch 'verify' failed.");
        }
        script = ScriptPatch_verify().Replace(script, 
            """
            $1
            // hacked~kissy
            gcide.LGWindow.showOwnVersion(1);
            onFin(1);
            return;
            """);
        
        await console.Output.WriteLineAsync("- - Patch 'release'");
        if (!ScriptPatch_release().IsMatch(script)) {
            throw new Exception("Patch 'release' failed.");
        }
        script = ScriptPatch_release().Replace(script, 
            """
            $1
            // hacked~kissy
            ra();
            return;
            """);
        
        await console.Output.WriteLineAsync("- - Patch 'own'");
        if (!ScriptPatch_own().IsMatch(script)) {
            throw new Exception("Patch 'own' failed.");
        }
        script = ScriptPatch_own().Replace(script, 
            """
            $1
            // hacked~kissy
            return true;
            """);
        
        await console.Output.WriteLineAsync("- - Patch 'crypt'");
        if (!ScriptPatch_crypt().IsMatch(script)) {
            throw new Exception("Patch 'crypt' failed.");
        }
        script = ScriptPatch_crypt().Replace(script, 
            """
            $1
            // hacked~kissy
            return str;
            """);
        
        await File.WriteAllTextAsync(scriptPath, script);
    }

    public async Task PatchCoreTemplate(string templatePath) {
        await console.Output.WriteLineAsync("- Patch core template");
        var scriptDecrypt = new ScriptDecrypt(console);
        foreach (var scriptPath in TemplateScripts) {
            await console.Output.WriteLineAsync($"- - Decrypt {scriptPath}");
            var fullPath = Path.Combine(templatePath, scriptPath);
            if (!await ScriptDecrypt.IsGcjsEncrypted(fullPath)) {
                await console.Output.WriteLineAsync("- - Already patched, skipping");
                continue;
            }
            File.Copy(fullPath, fullPath + ".bak", true);
            var script = await scriptDecrypt.DecryptScriptAsync(fullPath);
            await File.WriteAllTextAsync(fullPath, script);
        }
    }
}
