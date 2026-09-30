using GCSlayer.Services;

namespace GCSlayer.Tests;

/// <summary>Mirrors the GameCreator DRM codec (LGProject attribute encrypt/decrypt).</summary>
public class DrmEncryptionTests {
    [Fact]
    public void EncryptThenDecrypt_RoundTrips() {
        const string text = """{"k1":0.40394177775262174-1691478101022,"name":"爱之巢"}""";

        var encrypted = DrmEncryption.Encrypt(text);

        Assert.Equal(text, DrmEncryption.Decrypt(encrypted));
    }

    [Fact]
    public void Encrypt_WritesMagicPrefixAndShift() {
        var encrypted = DrmEncryption.Encrypt("abcd");

        Assert.StartsWith("\u0005\u0005\u0005", encrypted);
        Assert.Equal(4 + 4, encrypted.Length);
        Assert.InRange(encrypted[3], (char)1, (char)1314);
    }

    [Fact]
    public void Encrypt_IsIdempotent() {
        var once = DrmEncryption.Encrypt("abcd");

        Assert.Equal(once, DrmEncryption.Encrypt(once));
    }

    [Fact]
    public void Decrypt_ReturnsInputWithoutMagic() {
        Assert.Equal("plain text", DrmEncryption.Decrypt("plain text"));
    }

    [Fact]
    public void CodecNeverConfusesPlainTextWithEncryptedText() {
        // Regression: ICU collation ignores the U+0005 prefix, so a culture sensitive
        // StartsWith made Decrypt eat plain text and turned Encrypt into a no-op.
        Assert.Equal("abcd", DrmEncryption.Decrypt("abcd"));
        Assert.NotEqual("abcd", DrmEncryption.Encrypt("abcd"));
    }

    [Fact]
    public void RoundTrip_KeepsEveryShiftVariation() {
        // the shift byte makes even/odd characters differ, so a long text covers both branches
        var text = string.Concat(Enumerable.Range(0, 200).Select(i => (char)(32 + i % 90)));

        for (var i = 0; i < 25; i++)
            Assert.Equal(text, DrmEncryption.Decrypt(DrmEncryption.Encrypt(text)));
    }
}
