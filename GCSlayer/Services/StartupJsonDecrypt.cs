using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip;

namespace GCSlayer.Services;

public static class StartupJsonDecrypt {
    public static async Task DecryptStartupJsonAsync(string assetPath, Action<double>? progressCallback = null) {
        var startupJsonPath = Path.Combine(assetPath, "asset", "json", "startup.json");
        if (!File.Exists(startupJsonPath)) 
            throw new DirectoryNotFoundException($"startup.json not found: {startupJsonPath}.");
        var rawData = await File.ReadAllBytesAsync(startupJsonPath);
        File.Delete(startupJsonPath);
        using var ms = new MemoryStream(rawData);
        var zf = new ZipFile(ms);
        zf.Password = "gc_zip";
        await using Stream? zs = zf.GetInputStream(zf[0]);
        using JsonDocument jsonDoc = await JsonDocument.ParseAsync(zs);
        List<JsonProperty> properties = jsonDoc.RootElement.EnumerateObject().ToList();
        foreach (JsonProperty item in properties) {
            var path = Path.Combine(assetPath, item.Name);
            JsonElement jsonElement = item.Value;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, jsonElement.GetRawText());
            progressCallback?.Invoke(1D / properties.Count);
        }
        progressCallback?.Invoke(1D);
    }
}
