using System.Text;

namespace GCSlayer.Services;

public static class SaveFileCryptoService {
    public static string PasswordFromZ1(string z1) {
        if (string.IsNullOrEmpty(z1)) return z1;
        var passwordArr = z1.Split('|');
        var sb = new StringBuilder();
        foreach (var subStr in passwordArr) {
            if (string.IsNullOrEmpty(subStr)) continue;
            sb.Append((char)(subStr[0] - 1));
        }
        return sb.ToString();
    }
}
