using System.Text.Json;
using System.Text.Json.Serialization;

namespace GCSlayer.Models.JsonModel;

public class AssetEntryJson {
    [JsonPropertyName("fileLocalURL")]
    public string FileLocalUrl { get; init; } = "";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("isDirectory")]
    public bool IsDirectory { get; init; }

    public static async Task<List<AssetEntryJson>> ListFromFileAsync(string filePath) {
        await using FileStream stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<List<AssetEntryJson>>(stream,
                   SourceGenJsonContext.Default.ListAssetEntryJson)
               ?? throw new JsonException("Failed to deserialize config");
    }
}
