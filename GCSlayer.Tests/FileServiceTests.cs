using GCSlayer.Services;

namespace GCSlayer.Tests;

public class FileServiceTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gcs-files-" + Guid.NewGuid().ToString("N"));

    public FileServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose() {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }

    private string Write(string relativePath, string content) {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task CopyDirectoryAsync_CopiesNestedFiles() {
        Write("src/a.json", "a");
        Write("src/nested/deep/b.json", "b");

        await FileService.CopyDirectoryAsync(Path.Combine(_root, "src"), Path.Combine(_root, "dst"));

        Assert.Equal("a", File.ReadAllText(Path.Combine(_root, "dst", "a.json")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(_root, "dst", "nested", "deep", "b.json")));
    }

    [Fact]
    public async Task CopyDirectoryAsync_KeepsExistingFilesWhenOverwriteIsDisabled() {
        Write("src/a.json", "from repo");
        Write("dst/a.json", "from the game");

        await FileService.CopyDirectoryAsync(Path.Combine(_root, "src"), Path.Combine(_root, "dst"),
            overwriteExisting: false);

        // a recovered project must never lose what the released game still shipped
        Assert.Equal("from the game", File.ReadAllText(Path.Combine(_root, "dst", "a.json")));
    }

    [Fact]
    public async Task CopyDirectoryAsync_StillAddsMissingFilesWhenOverwriteIsDisabled() {
        Write("src/missing-in-game.json", "from repo");

        await FileService.CopyDirectoryAsync(Path.Combine(_root, "src"), Path.Combine(_root, "dst"),
            overwriteExisting: false);

        Assert.Equal("from repo", File.ReadAllText(Path.Combine(_root, "dst", "missing-in-game.json")));
    }

    [Fact]
    public async Task CopyDirectoryAsync_OverwritesByDefault() {
        Write("src/a.json", "from repo");
        Write("dst/a.json", "from the game");

        await FileService.CopyDirectoryAsync(Path.Combine(_root, "src"), Path.Combine(_root, "dst"));

        Assert.Equal("from repo", File.ReadAllText(Path.Combine(_root, "dst", "a.json")));
    }

    [Fact]
    public async Task CopyDirectoryAsync_ThrowsForMissingSource() {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            FileService.CopyDirectoryAsync(Path.Combine(_root, "nope"), Path.Combine(_root, "dst")));
    }

    [Fact]
    public async Task DeleteDirectoryAsync_RemovesTheWholeTree() {
        Write("victim/a.json", "a");
        Write("victim/nested/b.json", "b");

        await FileService.DeleteDirectoryAsync(Path.Combine(_root, "victim"));

        Assert.False(Directory.Exists(Path.Combine(_root, "victim")));
    }

    [Fact]
    public async Task DeleteDirectoryAsync_IgnoresMissingDirectory() {
        await FileService.DeleteDirectoryAsync(Path.Combine(_root, "never-existed"));

        Assert.True(Directory.Exists(_root));
    }
}
