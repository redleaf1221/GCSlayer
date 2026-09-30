using System.Text.Json.Nodes;
using GCSlayer.Services;

namespace GCSlayer.Tests;

/// <summary>
/// The data side of the command parameter table: a value only means what the script says when it sits
/// in the right parameter of the right command, and the engine forms that parameter list by slicing
/// the instance and dropping its trailing per instance record.
/// </summary>
public class CommandPagesTests {
    private static List<CommandParameter> Read(string json) {
        var parameters = new List<CommandParameter>();
        CommandPages.Walk(JsonNode.Parse(json), parameter => parameters.Add(parameter));
        return parameters;
    }

    [Fact]
    public void ReadsTheParameterAtIndexTheEngineReadsItFrom() {
        // [13, resultType, varID, symbol, valueType, …, ___cmdID]: precompile_13 reads the result type
        // as cmd.params[0], the variable id as cmd.params[1], the symbol as cmd.params[2].
        const string page = """{"commands":[[13,3,2006,0,0,0,{"___cmdID":"x"}]]}""";

        var parameters = Read(page);

        Assert.Equal([0, 1, 2, 3, 4], parameters.Select(parameter => parameter.Index));
        Assert.Equal(13, parameters[0].CommandId);
        Assert.Equal(3, parameters[0].Value!.GetValue<int>());
        Assert.Equal(2006, parameters[1].Value!.GetValue<int>());
        Assert.Equal(5, CommandPages.ParameterCount(parameters[0].Instance));
        // a parameter the instance does not carry is not reported at all
        Assert.DoesNotContain(parameters, parameter => parameter.Index == 5);
    }

    [Fact]
    public void ReadsTheParametersOfACustomCommandFromTheAttributeObject() {
        // [10002, { attributeID: value }, ___cmdID]: the parser turns cmd.params[0] into the values
        // keyed by the attribute ids of the custom command document.
        const string page = """{"commands":[[10002,{"43114858493_0.09":1},"230632420730_0.26"]]}""";

        var parameters = Read(page);

        Assert.Equal(2, parameters.Count);
        Assert.Equal(10002, parameters[0].CommandId);
        Assert.True(parameters[0].Value is JsonObject { Count: 1 });
        Assert.Equal("230632420730_0.26", parameters[1].Value!.GetValue<string>());
    }

    [Fact]
    public void ReadsThePagesOfAComponentThatHoldsOnePagePerElement() {
        // sui*.json nests a page per element of "commands".
        const string page = """
            {"root":{"commands":[[[0,1,2,{"___cmdID":"a"}]]]}}
            """;

        var parameters = Read(page);

        Assert.Equal(2, parameters.Count);
        Assert.Equal(0, parameters[0].CommandId);
        Assert.Equal(1, parameters[0].Value!.GetValue<int>());
        Assert.Equal(2, parameters[1].Value!.GetValue<int>());
    }

    [Fact]
    public void ReportsCustomValueCallsWithTheCommandSlotTheySitIn() {
        const string page = """
            {"commands":[[0,0,null,null,null,null,null,null,null,null,null,null,null,null,null,null,
            [10,{"opt":0},[{"133654753522_0.44":0}]],null,null,null,null,null,null,{"___cmdID":"a"}]]}
            """;

        var calls = new List<(JsonArray Call, int? Command, int? Parameter)>();
        CommandPages.Walk(JsonNode.Parse(page), _ => { },
            (call, command, parameter) => calls.Add((call, command, parameter)));

        var found = Assert.Single(calls);
        Assert.Equal(0, found.Command);
        // the call sits at JSON index 16 of the instance, so it is cmd.params[15]
        Assert.Equal(15, found.Parameter);
        Assert.Equal(10, found.Call[0]!.GetValue<int>());
    }

    [Fact]
    public void NeverReadsPlainNumericDataAsCommands() {
        // A curve of coordinate pairs looks like a page, but no command instance is that short.
        const string data = """{"curveData":[[500,120],[30000,60000]]}""";

        Assert.Empty(Read(data));
    }
}
