namespace GCSlayer.Models;

public record RecoverParameter {
    public required string GamePath { get; init; }
    public required string OutputPath { get; init; }
    public string? LocalSourcePath { get; init; }

    public string ProjectPath => Path.GetFullPath(OutputPath);
    public string AssetsPath => Path.Combine(ProjectPath, "asset");
}
