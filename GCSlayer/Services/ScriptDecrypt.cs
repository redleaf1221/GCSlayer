using System.Diagnostics;
using GCSlayer.Models;
using Spectre.Console;

namespace GCSlayer.Services;

public static class ScriptDecrypt {
    public static async Task<EncryptionStatus> DecryptScriptAsync(string inputPath, string outputPath) {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Script file not found: {inputPath}");
        
        await DecryptScriptContentAsync(inputPath);
        
        var decryptedContent = await File.ReadAllTextAsync(Path.Combine(OperationContext.GcJsDecryptPath, "decryptedScript.js"));
        
        EncryptionStatus encryptionStatus = EncryptionStatus.FromScriptAnalysis(decryptedContent);
        
        AnsiConsole.MarkupLine("- Remove unessential code");
        decryptedContent = RemoveUnessentialCode(decryptedContent);
        
        AnsiConsole.MarkupLine($"- Cleaned script saved to {Path.GetFileName(outputPath)}");
        await File.WriteAllTextAsync(outputPath, decryptedContent);
        
        return encryptionStatus;
    }
    
    private static async Task DecryptScriptContentAsync(string inputPath) {
        var startInfo = new ProcessStartInfo {
            FileName = "node",
            Arguments = $"{Path.Combine(OperationContext.GcJsDecryptPath, "GCJSDecrypt.js")} {inputPath}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = new Process();
        process.StartInfo = startInfo;
        process.Start();
        await process.WaitForExitAsync();
        var output = await process.StandardOutput.ReadToEndAsync(); 
        AnsiConsole.Markup($"[dim]{output}[/]");
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
}
