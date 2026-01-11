using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;

namespace GCSlayer.Services.AssetsDecrypt;

public static class AudioDecrypt {
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".mp3",
        ".ogg",
    };
    
    public static async Task DecryptAudioAsync(string assetPath, Action<double>? progressCallback = null) {
        var dir = new DirectoryInfo(assetPath);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {assetPath}.");

        FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => AudioExtensions.Contains(f.Extension))
            .ToArray();
        
        await Parallel.ForEachAsync(files, 
            new ParallelOptions{ MaxDegreeOfParallelism = 1}, 
            async (file, ct) => {
                var rawData = await File.ReadAllBytesAsync(file.FullName, ct);
                File.Delete(file.FullName);
                using var stream = new MemoryStream(rawData);
                var zf = new ZipFile(stream);
                zf.Password = "gc_zip_2024";
                var buffer = new byte[4096];
                await using Stream? zs = zf.GetInputStream(zf[0]);
                progressCallback?.Invoke(1D / files.Length);
                await using Stream fsOutput = File.Create(file.FullName);
                StreamUtils.Copy(zs, fsOutput , buffer);
            });
        progressCallback?.Invoke(1D);
    }
}
