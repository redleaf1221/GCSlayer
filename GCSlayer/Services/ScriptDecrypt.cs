using System.Diagnostics;
using CliFx.Infrastructure;
using GCSlayer.Models;

namespace GCSlayer.Services;

public class ScriptDecrypt(IConsole console) {
    public async Task<string> DecryptScriptAsync(string inputPath, string outputPath, bool removeFramework = false) {
        if (!File.Exists(inputPath)) {
            await console.Error.WriteLineAsync($"- File not found {inputPath}");
        }
        
        await CallGcjsDecrypt(inputPath);
        
        var decryptedContent = await File.ReadAllTextAsync(Path.Combine(Constants.GcJsDecryptPath, "decryptedScript.js"));
        
        await File.WriteAllTextAsync(outputPath, 
            removeFramework ? RemoveUnessentialCode(decryptedContent) : decryptedContent);
        
        return decryptedContent;
    }
    
    private async Task CallGcjsDecrypt(string inputPath) {
        var startInfo = new ProcessStartInfo {
            FileName = "node",
            Arguments = $"{Path.Combine(Constants.GcJsDecryptPath, "GCJSDecrypt.js")} {inputPath}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = new Process();
        process.StartInfo = startInfo;
        process.Start();
        await process.WaitForExitAsync();
        var output = await process.StandardOutput.ReadToEndAsync(); 
        await console.Output.WriteLineAsync(output);
    }
    
    private static string RemoveUnessentialCode(string script) {
        var idx = script.LastIndexOf("GameUI.init();", StringComparison.Ordinal);
        if (idx != -1) {
            script = script[(idx + "GameUI.init();".Length)..];
        }
        idx = script.IndexOf("Config.RELEASE_GAME = true;", StringComparison.Ordinal);
        if (idx != -1) {
            script = script[..idx];
        }
        return script.Trim();
    }

    public static async Task<bool> CheckNodeExists() {
        var startInfo = new ProcessStartInfo {
            FileName = "node",
            Arguments = $"-v",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try {
            using var process = new Process();
            process.StartInfo = startInfo;
            process.Start();
            await process.WaitForExitAsync();
            return (await process.StandardOutput.ReadToEndAsync()).Contains('v');
        } catch (Exception) {
            return false;
        }
    }
}
