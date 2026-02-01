using System.Diagnostics;
using System.Text;
using CliFx.Infrastructure;
using GCSlayer.Models;

namespace GCSlayer.Services;

public class ScriptDecrypt(IConsole console) {
    public async Task<string> DecryptScriptAsync(string inputPath) {
        if (!File.Exists(inputPath)) {
            throw new FileNotFoundException($"File not found {inputPath}");
        }

        await CallGcjsDecrypt(inputPath);

        var decryptedContent = await File.ReadAllTextAsync(Path.Combine(Constants.GcJsDecryptPath, "decryptedScript.js"));

        File.Delete(Path.Combine(Constants.GcJsDecryptPath, "decryptedScript.js"));

        return decryptedContent;
    }

    private async Task CallGcjsDecrypt(string inputPath) {
        var startInfo = new ProcessStartInfo {
            FileName = "node",
            Arguments = $"{Path.Combine(Constants.GcJsDecryptPath, "GCJSDecrypt.js")} {inputPath}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = new Process();
        process.StartInfo = startInfo;
        process.Start();
        await process.WaitForExitAsync();
        var output = await process.StandardOutput.ReadToEndAsync();
        await console.Output.WriteLineAsync(output);
    }

    public static string RemoveUnessentialCode(string script) {
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
            Arguments = "-v",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
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

    public static async Task<bool> IsGcjsEncrypted(string scriptPath) {
        await using var fs = new FileStream(scriptPath, FileMode.Open, FileAccess.Read);
        var buffer = new byte[32];
        var bytesRead = fs.Read(buffer, 0, buffer.Length);

        return Encoding.UTF8.GetString(buffer, 0, bytesRead)
            .StartsWith("//<<JS加密");
    }
}
