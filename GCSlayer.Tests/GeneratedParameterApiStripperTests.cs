using GCSlayer.Services;
using Xunit;

namespace GCSlayer.Tests;

/// <summary>
/// The compiled copies of the editor generated parameter API classes are dead weight in the game
/// script, but only a definition that nothing else mentions may be dropped.
/// </summary>
public class GeneratedParameterApiStripperTests {
    private const string Definition = """
        var CustomConditionParams_1 = (function () {
            function CustomConditionParams_1() {
            }
            return CustomConditionParams_1;
        }());

        """;

    [Fact]
    public void Strip_RemovesADefinitionNothingMentions() {
        var script = "var gameLogic = 1;\n" + Definition + "var more = 2;\n";

        var (stripped, removed, skipped) = GeneratedParameterApiStripper.Strip(script, ["CustomConditionParams_"]);

        Assert.Equal(1, removed);
        Assert.Empty(skipped);
        Assert.DoesNotContain("CustomConditionParams_1", stripped);
        Assert.Contains("var gameLogic = 1;", stripped);
        Assert.Contains("var more = 2;", stripped);
        // The remaining script is untouched, including its line structure.
        Assert.DoesNotContain("\n\n\n", stripped);
    }

    [Fact]
    public void Strip_KeepsAClassTheScriptUses() {
        var script = Definition + "var params = new CustomConditionParams_1();\n";

        var (stripped, removed, skipped) = GeneratedParameterApiStripper.Strip(script, ["CustomConditionParams_"]);

        Assert.Equal(0, removed);
        Assert.Equal(["CustomConditionParams_1"], skipped);
        Assert.Equal(script, stripped);
    }

    [Fact]
    public void Strip_KeepsAClassThatInheritsAnotherOne() {
        // The fourth occurrence is the __extends call of a compiled class with a base class.
        var script = """
            var GUI_1 = (function (_super) {
                __extends(GUI_1, _super);
                function GUI_1() {
                }
                return GUI_1;
            }(GUIBase));

            """;

        var (stripped, removed, _) = GeneratedParameterApiStripper.Strip(script, ["GUI_1"]);

        Assert.Equal(0, removed);
        Assert.Equal(script, stripped);
    }

    [Fact]
    public void Strip_RemovesAStaleClassTheCurrentGeneratedFileNoLongerDeclares() {
        // The bundle was compiled while the list still had entry 6; the generated file has dropped it.
        var stale = Definition.Replace("CustomConditionParams_1", "CustomNumberParams_6");
        var script = stale + Definition.Replace("CustomConditionParams_1", "CustomNumberParams_7");

        var (stripped, removed, skipped) = GeneratedParameterApiStripper.Strip(script, ["CustomNumberParams_"]);

        Assert.Equal(2, removed);
        Assert.Empty(skipped);
        Assert.DoesNotContain("CustomNumberParams_", stripped);
    }

    [Fact]
    public void Strip_IgnoresNamesThatAreNotInTheScript() {
        var (stripped, removed, skipped) = GeneratedParameterApiStripper.Strip("var a = 1;\n", ["CustomGameStringParams_"]);

        Assert.Equal(0, removed);
        Assert.Empty(skipped);
        Assert.Equal("var a = 1;\n", stripped);
    }

    [Fact]
    public void Strip_IsIdempotent() {
        var script = "var a = 1;\n" + Definition;

        var (once, _, _) = GeneratedParameterApiStripper.Strip(script, ["CustomConditionParams_"]);
        var (twice, removed, _) = GeneratedParameterApiStripper.Strip(once, ["CustomConditionParams_"]);

        Assert.Equal(once, twice);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void ReadParameterApiPrefixes_ReadsTheGeneratedFilesAndTheEditorDeclarations() {
        using var fixture = new RecoveredProjectFixture();
        fixture.Write("Game/system/CustomConditionParams.ts", "class CustomConditionParams_1 {\n}\n");

        // The file name and its class names both give the family prefix.
        Assert.Equal(["CustomConditionParams_"], GeneratedParameterApiStripper.ReadParameterApiPrefixes(fixture.Root));
    }
}
