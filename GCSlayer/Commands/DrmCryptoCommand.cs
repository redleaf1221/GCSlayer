using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using GCSlayer.Services;

namespace GCSlayer.Commands;

[Command("drm_crypto", Description = "DRM related encryption and decryption.")]
public class DrmCryptoCommand : ICommand {
    [CommandParameter(0, Name = "input", Description = "Input file path.")]
    public required string InputPath { get; init; }

    [CommandOption("output", 'o', Description = "Output file path.")]
    public string? OutputPath { get; init; }

    [CommandOption("encrypt", 'e', Description = "Do encryption instead of decryption.")]
    public bool DoEncryption { get; init; }

    public async ValueTask ExecuteAsync(IConsole console) {
        try {
            if (!File.Exists(Path.GetFullPath(InputPath))) {
                await console.Error.WriteLineAsync("File not found");
            }
            var inputText = await File.ReadAllTextAsync(Path.GetFullPath(InputPath));
            var outputText = DoEncryption ? DrmEncryption.Encrypt(inputText) : DrmEncryption.Decrypt(inputText);
            var realOutputPath = OutputPath ?? Path.GetFullPath(InputPath) + "_processed";
            Directory.CreateDirectory(Path.GetDirectoryName(realOutputPath)!);
            await File.WriteAllTextAsync(realOutputPath, outputText);
        } catch (Exception ex) {
            await console.Error.WriteLineAsync(ex.ToString());
        }
    }
}
