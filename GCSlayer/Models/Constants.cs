namespace GCSlayer.Models;

public static class Constants {
    public static string TemplatePath => Path.Combine(AppContext.BaseDirectory, "template_project");
    public static string GcJsDecryptPath => Path.Combine(AppContext.BaseDirectory, "GCJSDecrypt");
    public static string FileRepoPath =>  Path.Combine(AppContext.BaseDirectory, "file_repo");
}
