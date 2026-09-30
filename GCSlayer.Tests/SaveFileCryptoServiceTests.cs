using GCSlayer.Services;

namespace GCSlayer.Tests;

/// <summary>
/// z1 holds the save file password with every character shifted by one and separated by '|'
/// (see the Config.z1 generation in the GameCreator runtime).
/// </summary>
public class SaveFileCryptoServiceTests {
    [Fact]
    public void PasswordFromZ1_DecodesRealProjectVector() {
        // taken from the recovered project's asset/json/config.json
        const string z1 = "1|/|4|3|7|4|:|8|:|3|3|8|6|9|6|8|7|8|.|2|8|1|1|8|6|1|3|1|9|8|2|6";

        Assert.Equal("0.3263979227585767-1700750208715", SaveFileCryptoService.PasswordFromZ1(z1));
    }

    [Fact]
    public void PasswordFromZ1_IgnoresEmptySegments() {
        Assert.Equal("ab", SaveFileCryptoService.PasswordFromZ1("b||c|"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void PasswordFromZ1_ReturnsInputWhenEmpty(string? z1) {
        Assert.Equal(z1, SaveFileCryptoService.PasswordFromZ1(z1!));
    }
}
