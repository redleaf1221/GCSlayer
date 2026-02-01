using CliFx.Infrastructure;
using GCSlayer.Models;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;

namespace GCSlayer.Services;

public class AssetCryptoService(IConsole console) {
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".mp3",
        ".ogg"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".jpg",
        ".jpeg",
        ".gif",
        ".png",
        ".dds"
    };

    private async Task ZipUnpackAsync(FileInfo[] assets, IProgress<double>? progress = null) {
        var processedCount = 0;
        await Parallel.ForEachAsync(assets,
            new ParallelOptions {
                MaxDegreeOfParallelism = 1
            },
            async (file, ct) => {
                try {
                    var rawData = await File.ReadAllBytesAsync(file.FullName, ct);
                    File.Delete(file.FullName);
                    using var stream = new MemoryStream(rawData);
                    var zf = new ZipFile(stream);
                    zf.Password = "gc_zip_2024";
                    var buffer = new byte[4096];
                    await using Stream? zs = zf.GetInputStream(zf[0]);
                    await using Stream fsOutput = File.Create(file.FullName);
                    StreamUtils.Copy(zs, fsOutput, buffer);
                    Interlocked.Increment(ref processedCount);
                    progress?.Report((double)processedCount / assets.Length);
                } catch (ZipException) {
                    await console.Output.WriteLineAsync($"- {file.FullName} failed to unzip, skipping");
                }
            });
        progress?.Report(1D);
    }

    private static byte[] ImageProcess(byte[] data) {
        if (data.Length < 3) return data;

        (data[1], data[2]) = (data[2], data[1]);

        var middle = (data.Length - 1) / 2;
        var result = new byte[data.Length - 1];

        Array.Copy(data, 0, result, 0, middle);
        Array.Copy(data, middle + 1, result, middle, data.Length - middle - 1);

        return result;
    }

    public async Task DecryptAudioAsync(string assetPath, IProgress<double>? progress = null) {
        var dir = new DirectoryInfo(assetPath);
        if (!dir.Exists) {
            await console.Error.WriteLineAsync("- Directory does not exist: " + assetPath);
        }

        FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => AudioExtensions.Contains(f.Extension))
            .ToArray();
        await ZipUnpackAsync(files, progress);
        await console.Output.WriteLineAsync($"- {files.Length} files decrypted");
    }

    public async Task DecryptImageAsync(string assetPath, IProgress<double>? progress = null) {
        var dir = new DirectoryInfo(assetPath);
        if (!dir.Exists) {
            await console.Error.WriteLineAsync("- Directory does not exist: " + assetPath);
        }

        FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => ImageExtensions.Contains(f.Extension))
            .ToArray();

        var processedCount = 0;
        await Parallel.ForEachAsync(files,
            new ParallelOptions {
                MaxDegreeOfParallelism = Environment.ProcessorCount / 2
            },
            async (file, ct) => {
                var rawData = await File.ReadAllBytesAsync(file.FullName, ct);
                var data = ImageProcess(rawData);
                await File.WriteAllBytesAsync(file.FullName, data, ct);
                Interlocked.Increment(ref processedCount);
                progress?.Report((double)processedCount / files.Length);
            });
        progress?.Report(1D);
        await console.Output.WriteLineAsync($"- {files.Length} files decrypted");
    }

    public async Task DecryptPlainJsonAsync(string assetPath, IProgress<double>? progress = null) {
        var dir = new DirectoryInfo(assetPath);
        if (!dir.Exists) {
            await console.Error.WriteLineAsync("- Directory does not exist: " + assetPath);
        }

        FileInfo[] files = dir.GetFiles("*.json", SearchOption.AllDirectories)
            .Where(f => !f.Name.EndsWith("startup.json")).ToArray();
        await ZipUnpackAsync(files, progress);
        await console.Output.WriteLineAsync($"- {files.Length} files decrypted");
    }

    public async Task DecryptVideoAsync(string assetPath, IProgress<double>? progress = null) {
        var dir = new DirectoryInfo(assetPath);
        if (!dir.Exists) {
            await console.Error.WriteLineAsync("- Directory does not exist: " + assetPath);
        }

        FileInfo[] files = dir.GetFiles("*.mp4", SearchOption.AllDirectories);
        await ZipUnpackAsync(files, progress);
        await console.Output.WriteLineAsync($"- {files.Length} files decrypted");
    }

    public async Task DecryptAllAssetsAsync(string assetPath, EncryptionStatus status) {
        if (status.ImageEncrypted) {
            await console.Output.WriteLineAsync("- Decrypt image");
            await DecryptImageAsync(Path.Combine(assetPath, "image"));
        }
        if (status.VideoEncrypted) {
            await console.Output.WriteLineAsync("- Decrypt video");
            await DecryptVideoAsync(Path.Combine(assetPath, "video"));
        }
        if (status.AudioEncrypted) {
            await console.Output.WriteLineAsync("- Decrypt audio");
            await DecryptAudioAsync(Path.Combine(assetPath, "audio"));
        }
        if (status.JsonEncrypted) {
            await console.Output.WriteLineAsync("- Decrypt json");
            await DecryptPlainJsonAsync(Path.Combine(assetPath, "json"));
        }
    }
}
