namespace GCSlayer.Services;

public static class FileService {
    /// <param name="overwriteExisting">
    /// False keeps the files that are already present. The repository copy is only meant to add
    /// the list files the release stripped; anything the game still shipped must win.
    /// </param>
    public static async Task CopyDirectoryAsync(string sourcePath,
        string destPath,
        IProgress<double>? progress = null,
        bool overwriteExisting = true) {
        var dir = new DirectoryInfo(sourcePath);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}.");

        Directory.CreateDirectory(destPath);

        FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories);
        var totalSize = files.Sum(f => f.Length);
        long completedSize = 0;

        await Parallel.ForEachAsync(files,
            new ParallelOptions {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
            },
            async (file, ct) => {
                var relativePath = Path.GetRelativePath(sourcePath, file.FullName);
                var destFile = Path.Combine(destPath, relativePath);
                if (!overwriteExisting && File.Exists(destFile)) {
                    Interlocked.Add(ref completedSize, file.Length);
                    progress?.Report((double)completedSize / totalSize);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                await using FileStream sourceStream = file.OpenRead();
                await using FileStream destStream = File.Create(destFile);
                await sourceStream.CopyToAsync(destStream, ct);

                Interlocked.Add(ref completedSize, file.Length);

                progress?.Report((double)completedSize / totalSize);
            });

        progress?.Report(1D);
    }

    public static async Task DeleteDirectoryAsync(string targetPath,
        IProgress<double>? progress = null) {
        var dir = new DirectoryInfo(targetPath);
        if (!dir.Exists) return;

        FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories);

        var completedCount = 0;

        await Parallel.ForEachAsync(files,
            new ParallelOptions {
                MaxDegreeOfParallelism = Environment.ProcessorCount / 2
            },
            (file, _) => {
                File.Delete(file.FullName);

                Interlocked.Increment(ref completedCount);

                progress?.Report((double)completedCount / files.Length);
                return ValueTask.CompletedTask;
            });
        Directory.Delete(targetPath, true);

        progress?.Report(1D);
    }
}
