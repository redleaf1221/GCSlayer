# GameCreator 引擎实现笔记(reverse engineering)

本文记录 GCSlayer 在实现过程中从 GameCreator 0.9942 的编辑器/引擎代码里**考证出来的事实**,以及每条事实的依据。
路径记号:

- `IDE` = 项目里的 `GameCreator/script.js`(编辑器侧脚本,也是引擎侧同一份代码;模板 `kdsrpg/coreTemplate/GameCreator2D/GameCreator/script.js` 内容相同)
- `ENG` = `GameCreatorLib/gamecreator.js`(引擎)
- 行号来自 GameCreator 0.9942;换版本可能偏移

凡是没有依据的推测,本文会显式标注"未确认"。

---

## 1. 发布管线(release)

打包由模板项目里的 `IDEProjectRelease` 负责,不是 IDE 主程序:

| 行为 | 依据 |
| --- | --- |
| 发布完成时集中删除编辑器专用文件:`onReleaseGameComplete` | 模板 `script.js` L109548–L109643 |
| JSON 合并进加密 zip:`mergeStartupJson`(整体 `asset/json/startup.json`,密码 `gc_zip`;按文件类型的 json/audio/video zip 密码 `gc_zip_2024`) | L109688–L109782 |
| 删除**整个** `asset/json/custom` 目录 | L109583 |
| 删除 `asset/json/custom/customModule`、`customGameAttribute.json`、`asset/json/server/variable`、`asset/json/scene/tile`、`asset/json/scene/sceneObjectModule/`、`asset/json/server/ui` | L109559–L109599 |
| 逐个删除 `animationSignalList.json`、`animationList.json`、`avatarActList.json`、`avatarList.json`、`avatarPartList.json`、`avatarRefObjList.json`、`server/serverConfig.json`、`standAvatarPartList.json`、`standAvatarList.json`、`expressionList.json`、`scene/sceneList.json` | L109584–L109599 |
| 另一类文件走 `deleteGameListDataOriFile`:保留列表本身但剥离每项文档/名字:`tileList`、`autoTileList`、`customCommandType`、`dataStructure`、`sceneObjectModelList`、`sceneObjectModuleList`、`customSceneEventType`、`customObjectEventType`、`customUIEventType`、`customMaterialList` | L109557–L109579 |
| 每项文档的发布路径由 `releasePath + "/" + c.path + i + ".json"` 拼出 | L109625 |
| **引擎 bug**:`releasePath + "asset/json/custom/customGameAttribute.json"` 少了一个 `/`,所以这个文件实际上没被删掉 | L109565 |

结论:被 release 删掉的东西(名字、每项文档、变量表)在发布包里就不存在了;能恢复的只有"其他幸存数据能够推导出来"的部分。

## 2. 列表模型(GameListData)

| 事实 | 依据 |
| --- | --- |
| 条目 id 与列表下标的关系:`getID(type,index) = (type-1)*1000 + index`,`getType(id) = floor((id-1)/1000)+1`,`getLocalID(id) = (id-1)%1000+1` | `gamecreator.js` L49721–L49824;`IDE` L9220–9222 |
| 列表有两种形态:带类型的 `{list:{type:[null,name…]}, type:{}}` 与扁平的 `{list:[null,name…]}` | 各列表文件 |
| `hasType=false` 时列表就是 `list` 数组,`id = i` | `ENG` L50525–L50536 |
| 列表数组里存的是**名字**;`id` 就是数组下标,文档文件名 `<prefix><id>.json` | `ENG` L50594、`IDE` L10086 |
| 文档里的 `"id"` 字段会被加载器**覆盖**,不一致不报错 | `ENG` L50609 |
| 名字只在列表数组里;列表被删 ⇒ 名字不可恢复。名字查不到时 IDE 显示字面量 `--/--` | `IDE` L9263–9266 |

### fragmentFileVersion

| 事实 | 依据 |
| --- | --- |
| 开关在项目 `asset/json/config.json` 的 `fragmentFileVersion` | `IDE` L9909 |
| 为真时每项文档独立成文件,路径由 loader 的 `childItems[].path` 给出(如 `asset/json/custom/condition/condition<N>.json`) | `IDE` L9908–L9930 |
| 为假时用的是历史文件名:`cumtomCondition.json`(作者自己的拼写)、`customDataDisplayList.json`,文档内联在列表的 `data` 里 | `IDE` L9909、L9919–L9920 |
| "自定义游戏数值"面板在 fragment 模式下**就是** `customGameNumberList.json` + `custom/gameNumber/num<N>.json` | `IDE` L9913–L9922 |
| `allowNoFiles=true` 的列表在文档缺失时不报错(条件/数值/字符串/材质/数据结构等);`false` 时该条目直接从列表里删掉 | `ENG` L50477–L50605 |

各类 loader 对应的列表文件/文档前缀:`loadCustomCondition`(L9908)、`loadCustomGameNumber`、`loadCustomGameString`(L9924)、`loadCustomBehaviorType`(L9905)、`loadMaterialList`(L9928)、`loadCustomDataDisplayInUI`(L9913)。

## 3. 自定义扩展(自定义条件/数值/字符串/行为/值函数)

### 3.1 编译产物

| 事实 | 依据 |
| --- | --- |
| 每个扩展编译成 `CustomCondition.f7 = f7;`,函数签名恒为 `function f7(trigger, p)`;`p` 就是编辑器里那张参数块 | 项目 `Game/GCMain.ts` 与 `IDE` 的编译代码 |
| 引擎运行时按 `CustomCondition["f" + id]` 取函数,把数据里的参数对象当 `p` 传入 | `ENG` L44906–L44913、L44937、L45085 |
| 自定义指令编译成 `CommandExecute.command_<id>` / `CommandExecute.customCommand_<id>`,另有准备函数 `precompile_<id>` | `ENG` L45786;项目脚本 |

### 3.2 文档格式

```jsonc
{
  "compAutoOrderLimitHeight": 0,
  "id": 7,                       // 装饰性,加载时会被列表下标覆盖
  "blockList": [{
    "name": "未命名块",           // 块标题;缺省值来自 [#GAME_UNNAMED_BLOCK]
    "blockAttrs": [{ "attr": { … }, "attrConditions": [], "x": 15, "y": 10, "width": 255, "height": 32 }],
    "blockCondition": [],
    "blockHeight": 300,           // 设计器画布高度,持久化,不会被重算
    "autoOrder": true,
    "hideBlockTitle": false
  }],
  "isCustomPhraseDesc": false,
  "customPhraseDescTS": "…", "customPhraseDescJS": "…"   // 仅在 isCustomPhraseDesc 为真时使用
}
```

| 事实 | 依据 |
| --- | --- |
| `attr` 必备字段:`compData.compType`(缺了直接抛错)、`varType`、`defaultValue`(varType==2 时必须是字符串,代码会 `toLocaleLowerCase()`)、`id`、`varName`;`blockAttrs`/`blockCondition` 必须是数组 | `IDE` L105494、L7498、L13323、L13815 |
| `compParam` 只有 compType 2(下拉框)会被解引用(`showIndex`/`items`),其它控件 `ObjectUtils.clone(undefined, …)` 无害 | `IDE` L105496–L105497 |
| `blockAttrs` 包装层的 `x/y/width/height` 只影响块设计器;运行时面板重算 `x/y`,`width` 会被读 | `IDE` L143178、L143183、L155950 |
| 参数标签:`CustomAttributeSetting.compName = attr.alias ? attr.alias : attr.varName` | `IDE` L7195–L7197 |
| 块标题取 `blockList[i].name` | `IDE` L158334、L158386 |

### 3.3 varType 与 compType

| varType | 含义 | 依据 |
| --- | --- | --- |
| 0 / 1 / 2 | 数值 / 字符串 / 布尔 | `IDE` L7273–L7282、L135441 |
| 3 / 4 | 自定义数据结构 / 自定义模块 | `IDE` L7284、L7291 |
| 5 | 对象 | `IDE` L7294–L7312 |

- `arrayMode` 使序列化类型 +6(`IDE` L7396–7397);`varType==4 && onlyPointTo` 按数值存(`IDE` L7388–L7391)。
- varType 3/4 的 compType 会被编辑器按模式强制写成 1001/1002(配置态)或 1003/1004(数据态)(`IDE` L105526–L105529)。

控件工厂 `EditorDataUICompAttribute.createEditorUIComponent`(`IDE` L105413–L105512),只对 `needAppendParam = [0,1,2,13,39,41,43,46,54,55,56,57,58,59]` 的 compType 把 `compParam` 拷到控件上。常用取值:

| compType | 控件 | compParam |
| --- | --- | --- |
| 0 | NumericStepper | `{}` 即可(类内有默认值) |
| 2 | TitleCombobox | **必须**有非空 `items`;`showIndex` 决定是否加 `i-` 前缀 |
| 5 / 29 / 39 / 13 | 界面 / 立绘 / 场景 / 资源文件选择 | 13 用 `modeType/rootPath/formatType` |
| 6 / 7 / 8 | 玩家数值 / 开关 / 字符串变量选择(`ENG` 侧另有全局变体 16/17/18) | 值 = 变量 id(数字) |
| 9 / 15 | 单行文本 / 勾选框 | `{}` |
| 36 / 50 / 51 | 自定义数值 / 条件(开关) / 字符串引用 | 值形如 `[id, {}, {}]` |
| 41 | TitleTransComp | `loopType/timeType/immediateMode` |
| 46 | TitleSelectUIComp | `typeMode`;值形如 `{uiID, compName, compID, varName}` |
| 55 | 自定义数据属性选择 | `numType/strType/boolType/getSetType` |
| 1003 / 1004 | 数据结构 / 自定义模块 | — |

依据:`IDE` L105413–L105512、L141283–L141288、L165030–L165089、L7294–L7312。

### 3.4 文档 ↔ 编号

- `<N>.json` 里的 `N` **就是**函数编号,也就是列表下标(列表被删时,唯一能从脚本反推出来的编号就是 `CustomX.f<N>` 的 `N`)。
- `f<N>` 与 `condition<N>.json` 一一对应:条件分支指令读 `params[0]-3` 得到编号(`ENG` L44908–L44913)。
- 脚本里出现 `CumtomGameNumber.f6/f11` 这种拼错的模块名时,引擎查的是 `CustomGameNumber`,所以那些函数**运行时不可达**;GCSlayer 不把它们当作同族扩展(不猜拼写)。

### 3.5 生成物 `Game/system/*Params.ts`

| 事实 | 依据 |
| --- | --- |
| 四个文件由 `CodeIDELocal` 声明:`CustomCommandParams.ts` / `CustomConditionParams.ts` / `CustomGameNumberParams.ts` / `CustomGameStringParams.ts` | `IDE` L133601–L133602 等 |
| 类名形如 `CustomConditionParams_<id>`,注释是 `自定义条件 <id>-<列表名>`;字段由文档的 blockAttrs 生成 | `IDE` L13435–L13462 |
| 只有"列表被保存"时才重新生成(`needSaveCustomFiles` 脏标记;单个条目被编辑也会触发) | `IDE` L106781–L106800 |
| **游戏安装目录里没有 `Game/` 目录**:恢复出的工程里的 `Game/system/*.ts` 来自模板/IDE,不是发布包内容 | 实测(Steam 安装目录) |
| 编译产物在 `Game/GCMain.ts` 里体现为 `var X = (function () { function X() {} return X; }());`,纯类型声明,游戏自身不实例化 | 实测(LoveNest bundle) |

注意:由此不能从 `.ts` 文件反推"作者原始状态";能反推的是 **bundle** 里的类(它是作者编译出来的)。例如 bundle 里存在 `CustomGameNumberParams_6/_11`、`CustomGameStringParams_15`,说明作者编译时列表里有这些编号,而脚本里没有可达的 `CustomGameNumber.f6/f11`。

## 4. 实例数据格式

### 4.1 自定义条件/数值/字符串的调用

```jsonc
[ 10, { "opt": 1, "filePath": "…", "useVar": false, … }, [ { "133654753522_0.447…": 1, … } ] ]
```

| 事实 | 依据 |
| --- | --- |
| 编辑器把值写**两遍**:slot[1] 按参数名(`varName`)键,slot[2] 按文档的 `attr.id` 键;两者由同一个循环写出,所以**位置一一对应** | `IDE` L7596–L7600、L7728 |
| 引擎运行时读 slot[1] | `ENG` L44912 |
| 编辑器面板读 slot[2](没有回退到名字) | `IDE` L126335 → L8066;`IDE` L101492 |
| 因此文档的 `attr.id` 与数据里的 id 不一致时,面板显示的是控件默认值 | 推论,实测一致(条件分歧 [7] 现象) |
| 只能给一个文档的调用 | 模糊时无法归属,除非知道该 slot 属于哪个模块(见 §5) |

### 4.2 自定义指令

- 数据形如 `[10002, { "<attrID>": value, … }, ___cmdID]`:`10000 + 列表槽位`,`cmdType = 10002` 对应 `cmd15002.json`(`CUSTOM_COMMAND_START_ID = 10000`,`IDE` L5428;`CommandPage.getTypeByCustomCmdID`,`IDE` L5217–L5221)。
- 自定义指令的参数只按 `attr.id` 存一份,没有名字镜像(`IDE` L156192、L156255、L156274)。
- 自定义行为是第三种:位置数组(`IDE` L7579、L128873)。

### 4.3 指令参数的下标与"尾记录"

| 事实 | 依据 |
| --- | --- |
| 内置指令实例是 `[cmdID, 参数…, 尾记录]`,引擎用 `Command.getCMDIDByParam` 取尾记录,`CommandPage.parse` 会把它 pop 掉;所以 JSON 下标 `i` 对应 `cmd.params[i-1]` | `CommandPage.parse` / `Command.getCMDIDByParam` |
| 例:设置变量 `[13, resultType, varID, symbol, valueType, …]` = `params[0]` 结果类型、`params[1]` 变量 id、`params[2]` 运算符、`params[3]` 值类型 | `precompile_13`(`IDE` L4455–L4570) |
| 命令页有两种形态:`{"commands":[[cmd,…],…]}` 与 UI 组件里 `{"commands":[[[cmd,…]],…]}` | 实测 `server/command/data/ws*.json`、`server/ui/sui*.json` |
| 编辑器把"常量"和"变量 id"存在同一个槽里,用标志位/模式区分(如 `params[3] ? getVariable(params[0]) : params[0]`、`params[4] != -5`、`switch (cmd.params[6])`) | `precompile_13` L4497–L4535、`command_17` L4681 等 |

## 5. 变量

| 事实 | 依据 |
| --- | --- |
| 设置变量指令的结果类型:`0/1/2` = 世界 数值/开关/字符串,`3/4/5` = 玩家 数值/开关/字符串,`6` = 场景对象开关 | `precompile_13` 的 `resultSetTypes` |
| 世界变量表:`asset/json/server/variable/{variable,switch,string}.json`;玩家变量表:`asset/json/variable/{variable,switch,string}.json` | IDE loader;release 删除列表 |
| 存档 `savedata/*.gcdata` 里 `variable.variables/switchs/strings` 是"每个变量一个槽"的数组,**不区分世界/玩家**,数组下标即 id(index 0 保留) | 实测存档结构 |
| 存档密码由项目 `z1` 推导(AES zip) | `z1` 命令实现 |
| 变量名也只在列表数组里;世界/玩家变量表都被 release 删掉,所以名字恢复不出来 | §1 删除清单 |

## 6. 编辑器读面板时的硬约束(实现控件时踩过的坑)

| 约束 | 依据 |
| --- | --- |
| `compData.compType` 必须是 `compMapping` 的键,否则 `new compMapping[compType]` 直接抛错 | `IDE` L105494、L105414–L105493 |
| compType 2 必须有 `compParam` 对象(`showIndex` 被直接解引用) | `IDE` L105496 |
| varType==2 的 `defaultValue` 必须是字符串 | `IDE` L7498 |
| `blockAttrs` / `blockCondition` 必须是数组 | `IDE` L13323、L13815 |
| `customPhraseDescJS` 出错会被 try/catch 成 `[#GAME_MAKE_A_MISTAKE]`,所以 `isCustomPhraseDesc` 保持 false 最安全 | `IDE` L101502–L101513 |
| 项目在 IDE 里打开后,内存中的列表是启动时加载的;保存会用旧内容覆盖磁盘 ⇒ 改完 json 必须重开 IDE | `IDE` L106797–L106804 |
