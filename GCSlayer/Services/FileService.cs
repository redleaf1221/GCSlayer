namespace GCSlayer.Services;

public static class FileService {
    public static async Task CopyDirectoryAsync(string sourcePath,
        string destPath,
        Action<double>? progressCallback = null) {
        
        var dir = new DirectoryInfo(sourcePath);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}.");

        Directory.CreateDirectory(destPath);

        FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories);
        var totalSize = files.Sum(f => f.Length);

        await Parallel.ForEachAsync(files, 
            new ParallelOptions{ MaxDegreeOfParallelism = Environment.ProcessorCount / 2}, 
            async (file, ct) => {
            var relativePath = Path.GetRelativePath(sourcePath, file.FullName);
            var destFile = Path.Combine(destPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            await using FileStream sourceStream = file.OpenRead();
            await using FileStream destStream = File.Create(destFile);
            await sourceStream.CopyToAsync(destStream, ct);
            
            progressCallback?.Invoke((double)file.Length / totalSize);
        });
        
        progressCallback?.Invoke(1D);
    }
    
    public static async Task DeleteDirectoryAsync(string targetPath,
        Action<double>? progressCallback = null) {
        
        var dir = new DirectoryInfo(targetPath);
        if (!dir.Exists) return;

        FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories);

        await Parallel.ForEachAsync(files, 
            new ParallelOptions{ MaxDegreeOfParallelism = Environment.ProcessorCount / 2}, 
            (file, _) => {
            File.Delete(file.FullName);
            
            progressCallback?.Invoke(1D / files.Length);
            return ValueTask.CompletedTask;
        });
        Directory.Delete(targetPath, true);
        
        progressCallback?.Invoke(1D);
    }
}
