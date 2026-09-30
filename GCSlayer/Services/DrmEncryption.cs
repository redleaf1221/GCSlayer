using System.Text;

namespace GCSlayer.Services;

public static class DrmEncryption {
    private const string Magic = "\x05\x05\x05";

    public static string Encrypt(string str) {
        // Ordinal matters: a culture sensitive comparison treats the control character prefix as
        // ignorable and matches every string, which silently turns the codec into a no-op.
        if (str.Length >= 3 && str.StartsWith(Magic, StringComparison.Ordinal)) return str;
        var shift = new Random().Next(1, 1315);
        var result = new StringBuilder();
        result.Append((char)5, 3);
        result.Append((char)shift);
        for (var i = 0; i < str.Length; i++) {
            if (i % 2 == 0) {
                result.Append((char)(str[i] + shift));
            } else {
                result.Append((char)(str[i] + shift + 1));
            }
        }
        return result.ToString();
    }

    public static string Decrypt(string str) {
        if (str.Length < 3 || !str.StartsWith(Magic, StringComparison.Ordinal)) return str;
        var shift = (int)str[3];
        str = str[4..];
        var result = new StringBuilder();
        for (var i = 0; i < str.Length; i++) {
            if (i % 2 == 0) {
                result.Append((char)(str[i] - shift));
            } else {
                result.Append((char)(str[i] - shift - 1));
            }
        }
        return result.ToString();
    }
}
