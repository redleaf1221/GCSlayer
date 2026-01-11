namespace GCSlayer.Services.AssetsDecrypt;

public static class ImageDecrypt {
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".jpg",
        ".jpeg",
        ".gif",
        ".png",
        ".dds"
    };
    
    public static async Task DecryptImageAsync(string assetPath, Action<double>? progressCallback = null) {
        var dir = new DirectoryInfo(assetPath);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {assetPath}.");
        
        FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => ImageExtensions.Contains(f.Extension))
            .ToArray();
        
        await Parallel.ForEachAsync(files, 
            new ParallelOptions{ MaxDegreeOfParallelism = Environment.ProcessorCount / 2}, 
            async (file, ct) => {
                var rawData = await File.ReadAllBytesAsync(file.FullName, ct);
                var data = DoDecrypt(rawData);
                await File.WriteAllBytesAsync(file.FullName, data, ct);
                progressCallback?.Invoke(1D / files.Length);
            });
        progressCallback?.Invoke(1D);
    }
    
    private static byte[] DoDecrypt(byte[] data) {
        if (data.Length < 3) return data;
        
        (data[1], data[2]) = (data[2], data[1]);
        
        var middle = (data.Length - 1) / 2;
        var result = new byte[data.Length - 1];
        
        Array.Copy(data, 0, result, 0, middle);
        Array.Copy(data, middle + 1, result, middle, data.Length - middle - 1);

        return result;
    }
}
