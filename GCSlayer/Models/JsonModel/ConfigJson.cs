using System.Text.Json;
using System.Text.Json.Serialization;

namespace GCSlayer.Models.JsonModel;

public class ConfigJson {
    [JsonPropertyName("gameProjectName")]
    public string GameProjectName { get; init; } = "";
    
    [JsonPropertyName("z1")]
    public string Z1 { get; init; } = "";
    
    [JsonPropertyName("TEMPLETE_USER_UID")]
    public int TemplateUserId { get; init; }
    
    [JsonPropertyName("gameSID")]
    public string GameSId { get; init; } = "";

    public static async Task<ConfigJson> FromFileAsync(string filePath) {
        await using FileStream stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<ConfigJson>(stream,
            SourceGenJsonContext.Default.ConfigJson) 
               ?? throw new JsonException("Failed to deserialize config");
    }
}
