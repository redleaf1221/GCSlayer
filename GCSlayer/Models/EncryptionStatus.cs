namespace GCSlayer.Models;

public record EncryptionStatus {
    public bool ImageEncrypted { get; init; }
    public bool JsonEncrypted { get; init; }
    public bool AudioEncrypted { get; init; }
    public bool VideoEncrypted { get; init; }

    public static EncryptionStatus FromScriptAnalysis(string scriptContent) {
        return new EncryptionStatus {
            ImageEncrypted = scriptContent.Contains("os.resourceEncryption = true;"),
            JsonEncrypted = scriptContent.Contains("//json"),
            AudioEncrypted = scriptContent.Contains("//声音"),
            VideoEncrypted = scriptContent.Contains("//视频")
        };
    }
}
