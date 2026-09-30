using GCSlayer.Services;

namespace GCSlayer.Tests;

/// <summary>
/// The command parameter table is read out of the game's own editor script, so these tests pin down
/// what the script has to say for the table to type a slot: the accessors of the engine, the helpers
/// a command declares, the guards it uses, and the custom value calls the editor compiles.
/// </summary>
public class CommandParameterTypesTests {
    private static CommandParameterTypes Table(string script) => CommandParameterTypes.FromScript(script);

    /// <summary>A command whose body reads its parameters through every accessor form.</summary>
    private const string Accessors = """
        var CommandExecute;
        (function (CommandExecute) {
            function command_5(commandPage, cmd, trigger, triggerPlayer) {
                var a = Command.gameWorld.getWorldVariable(cmd.params[5]);
                var b = trigger.triggerPlayer.variable.getVariable(cmd.params[6]);
                var c = Command.gameWorld.getWorldSwitch(cmd.params[7]);
                var d = triggerPlayer.variable.getSwitch(cmd.params[8]);
                var e = Command.gameWorld.getWorldString(cmd.params[9]);
                var f = Game.player.variable.getString(cmd.params[10]);
                var g = Game.player.variable.variables[cmd.params[11]];
                triggerPlayer.variable.setVariable(cmd.params[12], 1);
            }
            CommandExecute.command_5 = command_5;
        })(CommandExecute || (CommandExecute = {}));
        """;

    [Fact]
    public void TypesEveryVariableAccessorOfTheEngine() {
        var types = Table(Accessors);

        Assert.Equal(VariableKind.WorldNumber, types.Variable(5, 5));
        Assert.Equal(VariableKind.PlayerNumber, types.Variable(5, 6));
        Assert.Equal(VariableKind.WorldSwitch, types.Variable(5, 7));
        Assert.Equal(VariableKind.PlayerSwitch, types.Variable(5, 8));
        Assert.Equal(VariableKind.WorldString, types.Variable(5, 9));
        Assert.Equal(VariableKind.PlayerString, types.Variable(5, 10));
        // "variable.variables[id]" is the table form of the player number variables.
        Assert.Equal(VariableKind.PlayerNumber, types.Variable(5, 11));
        Assert.Equal(VariableKind.PlayerNumber, types.Variable(5, 12));
        Assert.Equal(1, types.Commands);
    }

    [Fact]
    public void TypesTheSlotsOfAUnionParameterAndTheConditionTheyNeed() {
        // The "if" command keeps a constant, a world id and a player id in one array and tells them
        // apart with the value type it stores next to it.
        const string script = """
            var CommandExecute;
            (function (CommandExecute) {
                function command_0(commandPage, cmd, trigger, triggerPlayer) {
                    var params = cmd.params;
                    var value1 = getValue(params[1], params[2], params[16]);
                    function getValue(valueType, param, customDataDisplay) {
                        switch (valueType) {
                            case 0:
                                return param[0];
                            case 1:
                                return Command.gameWorld.getWorldVariable(param[1]);
                            case 2:
                                return triggerPlayer.variable.getVariable(param[2]);
                        }
                    }
                }
                CommandExecute.command_0 = command_0;
            })(CommandExecute || (CommandExecute = {}));
            """;

        var slots = Table(script).VariableSlots(0, 2);

        var world = Assert.Single(slots, slot => slot.Kind == VariableKind.WorldNumber);
        Assert.Equal([ParameterStep.At(1)], world.Path);
        Assert.Equal(VariableGuardOperator.Equal, world.Guard!.Operator);
        Assert.Equal([ParameterStep.At(1)], world.Guard.Path);
        Assert.Equal(1, world.Guard.Value);

        var player = Assert.Single(slots, slot => slot.Kind == VariableKind.PlayerNumber);
        Assert.Equal([ParameterStep.At(2)], player.Path);
        Assert.Equal(2, player.Guard!.Value);
        // the constant of the union is not a variable, so it never becomes a slot
        Assert.DoesNotContain(slots, slot => slot.Path is [{ Index: 0 }]);
    }

    [Fact]
    public void TypesTheConditionAFlagPutsOnASlot() {
        // "cmd.params[3] ? getVariable(cmd.params[0]) : cmd.params[0]" stores a duration and a
        // variable id in one slot, so the id only counts while the flag is set.
        const string script = """
            var CommandExecute;
            (function (CommandExecute) {
                function command_17(commandPage, cmd, trigger, triggerPlayer) {
                    if (cmd.params[1] == 1) {
                        trigger.waitTime(cmd.params[3] ? triggerPlayer.variable.getVariable(cmd.params[0]) : cmd.params[0]);
                    }
                }
                CommandExecute.command_17 = command_17;
            })(CommandExecute || (CommandExecute = {}));
            """;

        var slot = Assert.Single(Table(script).VariableSlots(17, 0));

        Assert.Equal(VariableKind.PlayerNumber, slot.Kind);
        Assert.Empty(slot.Path);
        Assert.Equal(VariableGuardOperator.Truthy, slot.Guard!.Operator);
        Assert.Equal([ParameterStep.At(3)], slot.Guard.Path);
    }

    [Fact]
    public void TypesTheCustomValueCallsOfTheCommand() {
        const string script = """
            var CommandExecute;
            (function (CommandExecute) {
                function command_0(commandPage, cmd, trigger, triggerPlayer) {
                    var params = cmd.params;
                    var customCommandID = MathUtils.int(params[0]) - 3;
                    var customFunc = CustomCondition["f" + customCommandID];
                    customFunc(trigger, params[15][1]);
                    var number = CustomGameNumber["f" + params[16][0]](trigger, params[16][1]);
                    var text = CustomGameString["f" + params[23][0]](trigger, params[23][1]);
                    var value = CustomValueFunction["f" + (params[8] + 1)](trigger, triggerPlayer);
                }
                CommandExecute.command_0 = command_0;
            })(CommandExecute || (CommandExecute = {}));
            """;

        var types = Table(script);

        // the parameter block of a call is the slot the editor stored the call in
        Assert.Equal("CustomCondition", types.CustomModule(0, 15));
        Assert.Equal("CustomGameNumber", types.CustomModule(0, 16));
        Assert.Equal("CustomGameString", types.CustomModule(0, 23));
        Assert.Equal("CustomValueFunction", types.CustomModule(0, 8));
        // the id expression of the call is part of the reference as well
        Assert.Equal("CustomCondition", types.CustomModule(0, 0));
    }

    [Fact]
    public void RecoversTheAccessorsTheEngineOnlyBuildsAsText() {
        // precompile_13 assembles the whole assignment as a string, which is why the concatenation
        // itself has to name the accessor.
        const string script = """
            var CommandExecute;
            (function (CommandExecute) {
                function precompile_13(commandPage, cmd, index) {
                    var value;
                    if (cmd.params[3] == 1) {
                        value = "Command.gameWorld.getWorldVariable(" + cmd.params[5] + ")";
                    }
                    else if (cmd.params[3] == 2) {
                        value = "triggerPlayer.variable.getVariable(" + cmd.params[6] + ")";
                    }
                    else if (cmd.params[3] == 5) {
                        value = "CustomGameNumber.f" + (cmd.params[10][0]) + "(trigger,cmd.params[10][1])";
                    }
                }
                CommandExecute.precompile_13 = precompile_13;
            })(CommandExecute || (CommandExecute = {}));
            """;

        var types = Table(script);

        Assert.Equal(VariableKind.WorldNumber, types.Variable(13, 5));
        Assert.Equal(VariableKind.PlayerNumber, types.Variable(13, 6));
        Assert.Equal("CustomGameNumber", types.CustomModule(13, 10));
    }

    [Fact]
    public void KeepsWorkingWhenTheEditorScriptIsMissingOrBroken() {
        var missing = Path.Combine(Path.GetTempPath(), "gcs-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(missing);
        try {
            var without = CommandParameterTypes.Build(missing);

            Assert.False(without.Available);
            Assert.NotNull(without.Error);
            Assert.Empty(without.VariableSlots(13, 5));
            Assert.Null(without.CustomModule(0, 15));
        }
        finally {
            Directory.Delete(missing, true);
        }

        Assert.Throws<Acornima.SyntaxErrorException>(() => CommandParameterTypes.FromScript("function ( {"));
    }

    [Fact]
    public void BuildsTheTableFromTheProjectsOwnEditorScript() {
        using var fixture = new RecoveredProjectFixture();

        var types = CommandParameterTypes.Build(fixture.Root);

        Assert.True(types.Available);
        Assert.Equal("GameCreator/script.js", types.ScriptPath);
        Assert.Equal("CustomGameNumber", types.CustomModule(0, 16));
        Assert.True(types.TypedParameters > 0);
        Assert.True(types.VariableTypings > 0);
    }
}
