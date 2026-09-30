using System.Text.Json.Nodes;
using GCSlayer.Services;

namespace GCSlayer.Tests;

/// <summary>
/// A miniature recovered project: only the files a released GameCreator game still ships, plus
/// the template stubs the recovery leaves behind.
/// </summary>
internal sealed class RecoveredProjectFixture : IDisposable {
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "gcs-fixture-" + Guid.NewGuid().ToString("N"));

    public RecoveredProjectFixture() {
        Directory.CreateDirectory(Root);

        // item documents that survived the release
        Write("asset/json/animation/data/ani1.json", """{"totalFrame":1,"layers":[],"isParticle":false,"id":1}""");
        Write("asset/json/animation/data/ani1001.json", """{"totalFrame":2,"layers":[],"isParticle":false,"id":1001}""");
        Write("asset/json/avatar/data/avatar1.json", """{"picUrls":[],"parts":[{"id":0}],"actionListArr":[],"id":1}""");
        Write("asset/json/standAvatar/data/standAvatar1.json",
            """{"picUrls":[],"parts":[{"id":0,"partID":3}],"actionListArr":[],"id":1}""");
        // a UI element still pointing at an avatar whose document is gone
        Write("asset/json/ui/data/ui1.json", """{"name":"窗口","avatarID":5,"actionID":1}""");

        // the template stubs the recovery leaves behind
        Write("asset/json/animation/animationList.json", """{"list":{"1":[null,""]},"type":{"1":"","2":""}}""");
        Write("asset/json/avatar/avatarList.json",
            """{"list":{"1":[null,""],"12":[null,"","","",""]},"type":{"1":"","12":""}}""");
        Write("asset/json/standAvatar/standAvatarList.json", """{"list":{"1":[null,""]},"type":{"1":""}}""");
        Write("asset/json/standAvatar/standAvatarPartList.json", """{"list":[null,""]}""");
        Write("asset/json/avatar/avatarActList.json", """{"list":[null,""]}""");
        Write("asset/json/animation/animationSignalList.json", """{"list":[null,""]}""");
        Write("asset/json/standAvatar/expressionList.json", """{"list":[null,""]}""");
        Write("asset/json/variable/variable.json", """{"list":{"1":[null,""]},"type":{"1":""}}""");
        Write("asset/json/variable/switch.json", """{"list":{"1":[null,""]},"type":{"1":""}}""");
        Write("asset/json/variable/string.json", """{"list":{"1":[null,""]},"type":{"1":""}}""");
        Write("asset/json/server/variable/variable.json", """{"list":{"1":[null,""]},"type":{"1":""}}""");
        Write("asset/json/server/variable/switch.json", """{"list":{"1":[null,""]},"type":{"1":""}}""");
        Write("asset/json/server/variable/string.json", """{"list":{"1":[null,""]},"type":{"1":""}}""");
        Write("asset/json/custom/customCondition.json", """{"list":[null,""]}""");
        Write("asset/json/custom/customGameNumberList.json", """{"list":[null,""]}""");
        Write("asset/json/custom/customGameStringList.json", """{"list":[null]}""");
        Write("asset/json/custom/customValueFunction.json", """{"list":[null,""]}""");
        Write("asset/json/custom/customBehaviorType.json", """{"list":[null,""]}""");

        // the four attribute files the release splits customGameAttribute into
        Write("asset/json/custom/customWorldSetting.json", """{"blockList":[{"name":"通用设定"}],"compAutoOrderLimitHeight":0}""");
        Write("asset/json/custom/customPlayerSetting.json", """{"blockList":[]}""");
        Write("asset/json/custom/customWorldData.json", """{"attrs":{"speed":3,"bornScene":6}}""");
        Write("asset/json/custom/customPlayerData.json", """{"attrs":{}}""");
        Write("asset/json/custom/customGameAttribute.json",
            """{"worldAttributeSetting":{"blockList":[]},"playerAttributeSetting":{"blockList":[]},"worldAttributeConfig":{"attrs":{}},"playerAttributeConfig":{"attrs":{}},"k1":"keep-me"}""");

        // references the shipped data still holds: [13, resultType, varID, symbol, valueType, ...]
        Write("asset/json/server/command/data/ws1.json",
            """[[13,0,12001,0,5,1],[13,1,13001,0,5,1],[13,2,14001,0,5,1]]""");
        Write("asset/json/server/command/data/ws2.json",
            """[[13,3,11001,0,5,1],[13,4,12002,0,5,1],[13,5,15001,0,5,1]]""");
        // an "if" command comparing a world number variable: the parameter union stores a constant,
        // a world id (19001, used because cmd.params[1] == 1) and a player id (19501, not used)
        Write("asset/json/server/command/data/ws10.json",
            """[[0,0,1,[7,19001,19501,0],0,0,[0,0,0,0],0,[0,0,0],0,0,[0,0],"","",false,null,null,null,null,null,null,null,0,null,{"___cmdID":"a"}]]""");

        // a custom command definition plus one instance of it: the release keeps these, so they are
        // the author's own record of how a parameter of his plug-in is edited
        Write("asset/json/custom/cmd/cmd15001.json",
            """{"id":15001,"blockList":[{"name":"块","blockAttrs":[{"attr":{"id":"43114858493_0.09543223886965402","varName":"opt","varType":0,"compData":{"compType":2,"compParam":{"items":"常量,变量","showIndex":false}},"alias":"操作"}}]}]}""");
        Write("asset/json/server/command/data/ws3.json",
            """[[15001,{"43114858493_0.09543223886965402":1,"___cmdID":"x"}]]""");

        // plain text save file: one slot per variable id
        Write("savedata/life.gcdata",
            """{"variable":{"variables":[null,null,7],"switchs":[null,null,null,1],"strings":[null,2]}}""");

        // recovered game script: the only record of the extension ids and of their parameters
        Write("Game/GCMain.ts", """
            //script: 2.js
            var CustomCondition;
            (function (CustomCondition) {
                function f7(trigger, p) {
                    if (p.opt == 0) {
                        return p.useVarID;
                    }
                    else if (p.opt == 1) {
                        return false;
                    }
                    return W26_TransMap.getTrans(p.transId) != null;
                }
                CustomCondition.f7 = f7;
            })(CustomCondition || (CustomCondition = {}));
            var CustomGameNumber;
            (function (CustomGameNumber) {
                function f12(trigger, p) {
                    return p.useVar ? Game.player.variable.getString(p.name1) : p.name;
                }
                CustomGameNumber.f12 = f12;
            })(CustomGameNumber || (CustomGameNumber = {}));
            var CustomGameString;
            (function (CustomGameString) {
                function f3(trigger, p) {
                    return String(p.filePath);
                }
                CustomGameString.f3 = f3;
            })(CustomGameString || (CustomGameString = {}));
            """);

        // the editor/engine script of the project; only the command functions matter here
        Write("GameCreator/script.js", """
            //script: 21.js
            var CommandExecute;
            (function (CommandExecute) {
                function command_0(commandPage, cmd, trigger, triggerPlayer) {
                    var params = cmd.params;
                    var value1 = getValue(params[1], params[2], params[16]);
                    var value2 = getValue(params[4], params[5], params[17]);
                    function getValue(valueType, param, customDataDisplay) {
                        switch (valueType) {
                            case 0:
                                return param[0];
                            case 1:
                                return Command.gameWorld.getWorldVariable(param[1]);
                            case 2:
                                return trigger.triggerPlayer.variable.getVariable(param[2]);
                            case 4:
                                return CustomGameNumber["f" + customDataDisplay[0]](trigger, customDataDisplay[1]);
                        }
                    }
                }
                CommandExecute.command_0 = command_0;
            })(CommandExecute || (CommandExecute = {}));
            """);
    }

    public void Dispose() {
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
        GC.SuppressFinalize(this);
    }

    public string Path_(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Write(string relative, string content) {
        var path = Path_(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public JsonObject ReadObject(string relative) =>
        JsonNode.Parse(File.ReadAllText(Path_(relative)))!.AsObject();

    /// <summary>The controls of an extension document, as (varName, (varType, compType, items), default).</summary>
    public (string Name, (int VarType, int CompType, string Items) Control, string DefaultValue)[] Controls(
        string relative) {
        var block = ReadObject(relative)["blockList"]!.AsArray()[0]!.AsObject();
        return block["blockAttrs"]!.AsArray()
            .Select(entry => entry!.AsObject()["attr"]!.AsObject())
            .Select(attr => (
                attr["varName"]!.GetValue<string>(),
                (attr["varType"]!.GetValue<int>(),
                    attr["compData"]!["compType"]!.GetValue<int>(),
                    attr["compData"]!["compParam"]?["items"]?.GetValue<string>() ?? string.Empty),
                attr["defaultValue"]?.GetValue<string>() ?? string.Empty))
            .ToArray();
    }

    public JsonArray ListOf(string relative) => ReadObject(relative)["list"]!.AsArray();

    public JsonArray TypedListOf(string relative, string type) =>
        ReadObject(relative)["list"]!.AsObject()[type]!.AsArray();

    public async Task RepairAsync(List<string>? report = null) =>
        await new ProjectRepairService(message => {
            report?.Add(message);
            return Task.CompletedTask;
        }).RepairAsync(Root);

    public async Task<bool> VerifyAsync(List<string>? report = null) =>
        await new ProjectVerifier(message => {
            report?.Add(message);
            return Task.CompletedTask;
        }).VerifyAsync(Root);

    public Dictionary<string, string> HashAll() =>
        Directory.GetFiles(Root, "*.json", SearchOption.AllDirectories)
            .ToDictionary(path => path,
                path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
}
