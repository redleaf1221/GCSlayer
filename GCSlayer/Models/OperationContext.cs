namespace GCSlayer.Models;

public record OperationContext {
    public required string GamePath { get; init; }
    public required string OutputPath { get; init; }
    public string? LocalSourcePath { get; init; }
    public string? MissingListPath { get; init; }

    public string ProjectPath => Path.GetFullPath(OutputPath);
    public static string TemplatePath => Path.Combine(AppContext.BaseDirectory, "template_project");
    public string AssetsPath => Path.Combine(ProjectPath, "asset");
    public static string GcJsDecryptPath => Path.Combine(AppContext.BaseDirectory, "GCJSDecrypt");
    
    public static string FileRepoPath =>  Path.Combine(AppContext.BaseDirectory, "file_repo");
}
