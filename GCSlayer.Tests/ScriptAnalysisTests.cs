using GCSlayer.Models;
using GCSlayer.Services;

namespace GCSlayer.Tests;

public class EncryptionStatusTests {
    [Fact]
    public void DetectsImageEncryption() {
        var status = EncryptionStatus.FromScriptAnalysis("var a = 1;\nos.resourceEncryption = true;\n");

        Assert.True(status.ImageEncrypted);
        Assert.False(status.JsonEncrypted);
        Assert.False(status.AudioEncrypted);
        Assert.False(status.VideoEncrypted);
    }

    [Fact]
    public void DoesNotTreatDisabledEncryptionAsEnabled() {
        var status = EncryptionStatus.FromScriptAnalysis("os.resourceEncryption = false;");

        Assert.False(status.ImageEncrypted);
    }

    [Theory]
    [InlineData("//json", true, false, false)]
    [InlineData("//声音", false, true, false)]
    [InlineData("//视频", false, false, true)]
    public void DetectsTheReleaseMarkerComments(string marker, bool json, bool audio, bool video) {
        var status = EncryptionStatus.FromScriptAnalysis($"prefix\n{marker}\nsuffix");

        Assert.Equal(json, status.JsonEncrypted);
        Assert.Equal(audio, status.AudioEncrypted);
        Assert.Equal(video, status.VideoEncrypted);
    }
}

public class ScriptDecryptTests {
    [Fact]
    public void RemoveUnessentialCode_KeepsOnlyTheGameModules() {
        const string script = """
            // GameCreator Runtime
            function GameUI() {}
            GameUI.init();
            //script: 2.js
            var gameLogic = 1;
            Config.RELEASE_GAME = true;
            Object.defineProperty(Laya.Image.prototype, 'skin', {});
            """;

        var result = ScriptDecrypt.RemoveUnessentialCode(script);

        // the raw literal uses the line endings of this source file, so compare normalised
        Assert.Equal("//script: 2.js\nvar gameLogic = 1;", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void RemoveUnessentialCode_ReturnsTrimmedScriptWithoutMarkers() {
        Assert.Equal("var onlyGameCode = 1;", ScriptDecrypt.RemoveUnessentialCode("\n  var onlyGameCode = 1;\n"));
    }
}
