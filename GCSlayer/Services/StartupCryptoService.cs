using System.Text.Json;
using CliFx.Infrastructure;
using ICSharpCode.SharpZipLib.Zip;

namespace GCSlayer.Services;

public class StartupCryptoService(IConsole console) {
    public async Task ExtractStartupJsonAsync(string projectPath, IProgress<double>? progress = null) {
        var startupJsonPath = Path.Combine(projectPath, "asset", "json", "startup.json");
        if (!File.Exists(startupJsonPath)) {
            await console.Error.WriteLineAsync("- startup.json not found");
            return;
        }
        var rawData = await File.ReadAllBytesAsync(startupJsonPath);
        File.Delete(startupJsonPath);
        using var ms = new MemoryStream(rawData);
        var zf = new ZipFile(ms);
        zf.Password = "gc_zip";
        await using Stream? zs = zf.GetInputStream(zf[0]);
        using JsonDocument jsonDoc = await JsonDocument.ParseAsync(zs);
        List<JsonProperty> properties = jsonDoc.RootElement.EnumerateObject().ToList();
        var processedCount = 0;
        foreach (JsonProperty item in properties) {
            var path = Path.Combine(projectPath, item.Name);
            JsonElement jsonElement = item.Value;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, jsonElement.GetRawText());
            processedCount++;
            progress?.Report((double)processedCount / properties.Count);
        }
        progress?.Report(1D);
        await console.Output.WriteLineAsync($"- {properties.Count} entries in startup.json processed");
    }
}
