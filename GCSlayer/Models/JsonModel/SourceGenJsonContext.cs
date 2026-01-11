using System.Text.Json.Serialization;

namespace GCSlayer.Models.JsonModel;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ConfigJson))]
[JsonSerializable(typeof(AssetEntryJson))]
[JsonSerializable(typeof(List<AssetEntryJson>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
internal partial class SourceGenJsonContext : JsonSerializerContext;
