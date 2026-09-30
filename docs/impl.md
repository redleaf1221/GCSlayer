# 实现细节

引擎侧的事实依据见 [re.md](re.md);本文只讲 GCSlayer 自己怎么实现、为什么这样实现。

---

## 1. 服务一览

| 文件 | 职责 |
| --- | --- |
| `Services/FullRecoveryFlow.cs` | `recover` 的编排:工作区 → 脚本解密 → 资源解密 → 抽取 startup.json → 补缺失文件 → **Repair** → **Verify** |
| `Services/WorkspaceService.cs` | 建立工程骨架、复制模板/仓库 |
| `Services/IdePatcher.cs` | `init`:给 IDE 打补丁(跳过工程签名校验)、解掉模板脚本混淆 |
| `Services/ScriptDecrypt.cs` | 解出 `Game/GCMain.ts`;`RemoveUnessentialCode` 丢掉与游戏无关的框架代码 |
| `Services/AssetCryptoService.cs` | 按脚本分析出的加密状态解密图片/JSON/音频/视频 |
| `Services/StartupCryptoService.cs` | 解开 `asset/json/startup.json`(zip,`gc_zip`)并**逐条**写回工程 |
| `Services/DrmEncryption.cs` | 通用 DRM 编解码(`drm_crypto`) |
| `Services/SaveFileCryptoService.cs` | 存档口令(`z1`) |
| `Services/FileService.cs` | 目录复制(可只补缺失) |
| `Services/ProjectRepairService.cs` | `fixup`:六步修复(见 §3) |
| `Services/ProjectVerifier.cs` | `verify`:独立重推期望并报未解引用处 |
| `Services/JavaScriptFunctionScanner.cs` | 从 `GCMain.ts` 的 AST 里读扩展函数的参数(见 §4.1) |
| `Services/ExtensionControlCatalog.cs` | 参数种类 → 控件(见 §4.2) |
| `Services/ExtensionInstanceIndex.cs` | 从事件数据反推文档原始 `attr.id`(见 §4.3) |
| `Services/CommandParameterTypes.cs` | 从编辑器脚本解 `(指令, 参数下标) → 语义`(见 §4.4) |
| `Services/CommandPages.cs` | 结构化遍历指令页/实例(见 §4.4) |
| `Services/GeneratedParameterApiStripper.cs` | 从 bundle 里清掉 IDE 会重新生成的参数 API 类(见 §3.6) |

## 2. recover 的流程

1. `WorkspaceService.SetupWorkspace` 建骨架(需要先跑过一次 `init`)。
2. `ScriptDecrypt` 解 `script.js` → 写 `Game/GCMain.ts`,并从脚本内容判断图片/JSON/音频/视频是否加密(`EncryptionStatus.FromScriptAnalysis`)。
3. `AssetCryptoService` 按该状态解密资源。
4. `StartupCryptoService` 解开 `asset/json/startup.json` 并把其中**全部**条目写回工程(没有过滤),然后删掉 zip 本体。
5. 读 `asset/json/config.json` 拿工程名,把 `GameCreatorBin.gamecreator` 重命名为 `<工程名>.gamecreator`。
6. `FileService.CopyDirectoryAsync(repo, asset/json, overwriteExisting: false)`:**只补 release 删掉的缺失文件**,不覆盖游戏自己发布的内容。仓库优先用 `file_repo/<工程名>`,否则 `Constants.DefaultRepoPath`。
7. `ProjectRepairService.RepairAsync`(=`fixup`)。
8. `ProjectVerifier.VerifyAsync` —— 与第 7 步同一批期望,但用独立实现重推。

## 3. fixup 的六步

1. **条目列表**(动画/立绘/行走图):从各自的数据文件 + 被引用的 id 反推,id 规则见 re.md §2;没有文档但被引用的 id 会补一个空文档。
2. **`custom/customGameAttribute.json`**:由 release 拆出的四个文件(`customWorldSetting`/`customPlayerSetting`/`customWorldData`/`customPlayerData`)重装,保留原文件的 `k1`。
3. **变量表**:收集变量 id 的三个来源——`set variable` 指令、存档数组、以及**编辑器脚本参数表**指认的其它指令参数(见 §4.4)。写入时是**补齐**语义:保留已有名字与条目,只按类型分组扩展槽位,绝不删除。世界/玩家分别写入(指令证据本身带侧别);只有存档无法区分侧别,才像以前一样两侧都补。
4. **扩展表 + 文档**:从脚本 AST 取 `CustomX.f<N>` 的编号与参数(§4.1),据此生成/更新 `condition<N>.json` 等文档;`attr.id` 尽量用从数据反推的原始 id(§4.3),否则用 `StableId(模块, 编号, 参数名)` 的哈希。
5. **扁平列表**:按被引用的最大 id 补齐到长度,槽位为空时用 id 字符串当 stub 名(作者名字已被 release 删掉,无法恢复)。
6. **生成类清理**:删掉 `Game/GCMain.ts` 里 IDE 参数 API 文件的编译产物(§3.6)。

三条贯穿全部步骤的不变量:

- **幂等**:第二次运行内容零变化(验证方式见 §6)。
- **作者文档不可覆盖**:扩展文档若带有既不是 `StableId` 推导出来的、也不是从数据反推出来的 `attr.id`,就视为作者数据,跳过不写。
- **不写坏脚本**:清理 `GCMain.ts` 后必须仍能被 Acornima 解析,否则整体回滚。

### 3.6 生成类清理的判据

- 族名来自**工程自己的** `Game/system/*Params.ts` 文件名/类名,加上编辑器脚本里 `CodeIDELocal` 声明的 `*Params.ts` 名字(模板只带部分族,不能只看现有文件)。
- 只有类名在脚本里**恰好出现 3 次**(`var`/`function`/`return`,即只有定义)才删;出现第 4 次(带基类的 `__extends`)或有其它引用就保留。
- 删除后重新解析校验;解析失败则不写。

## 4. 分析用的核心算法

### 4.1 `JavaScriptFunctionScanner`

用 Acornima 把 `GCMain.ts` 解析成 ESTree,找 `function f<id>(trigger, p)`,再按参数在 AST 里的位置分类。
注释/字符串/模板串/正则由语法器天然排除——脚本里存在**被注释掉的旧版本函数**,文本扫描会把它们混进来。
模块归属:先看外层 IIFE 的单参数名,再看紧随的 `CustomX.f<id> = f<id>;` 赋值。

分类规则(全部是结构证据,不含任何"名字看起来像什么"):

| AST 位置 | 结论 |
| --- | --- |
| `if (p.x == 0) … else if (p.x == 1)`(与数字比较,收集到多个取值) | 下拉框,选项 = 比较过的数值 |
| `!p.x`、`if (p.x)`、`p.x ? a : b`、`p.x && …` | 布尔 |
| 与字符串字面量比较、`+` 的字符串拼接、`.length/.indexOf/.split/…` | 字符串 |
| 进入 `getVariable/getSwitch/getString`(引擎 API) | 对应的变量选择器 |
| 作为调用实参的 `String()/Number()/parseInt()` | 字符串 / 数值 |
| 取成员 `p.x.foo` | 对象,`foo` 记进 `Members` |
| `p.x[i]`(索引访问) | 列表 |
| `CustomGameNumber["f" + p.x[0]](trigger, p.x[1])` 之类 | 自定义数值/条件/字符串引用 |
| `flag ? getString(p.a1) : p.a` | `flag` 布尔;`a1` 字符串变量;`a` 由**成对分支推断**取同类的常量型 |

其它细节:嵌套函数若声明了自己的 `p`/`trigger` 则整棵子树跳过(遮蔽);参数顺序按首次出现的源码位置;
`Inferred` 标记"这个参数的种类是否由函数体证据得出"。

### 4.2 `ExtensionControlCatalog`

无状态;`种类 → 控件` 映射的 compType/compParam 数值来自引擎的控件工厂与参数设计器默认值(re.md §3.3)。

- 函数体未约束(`Inferred == false`)时,优先采用**数据里真实存过的值类型**(§4.3 的 `ObservedKind`);
- 仍未定时退回该种类的引擎默认控件(数值框);
- 下拉框标签一律是函数比较过的数值本身;
- **不写 alias**,面板标签由 IDE 回退成参数名。

### 4.3 `ExtensionInstanceIndex`

把指令页里形如 `[编号, {参数名: 值}, [{attrId: 值}]]` 的调用当一次投票:

- 两个对象由编辑器同一个循环写出,所以**按下标一一对应**,由此把 `attrId` 绑到参数名;
- 同一文档的所有调用必须**一致**才采纳该 id,否则丢弃(歧义不猜);
- 归属哪个文档:优先用参数表(§4.4)给出的模块,再用"参数名集合完全相同"做回退;
- `ObservedKind` 顺带把数据里出现过的 bool / 字符串 / 对象 / 数组类型作为证据(数字不投票,因为无法区分数值/变量 id/枚举)。

### 4.4 `CommandParameterTypes` + `CommandPages`

`CommandPages` 结构化地找出指令页与实例(两种页面形态、`params[i-1]`、弹掉尾记录),并给出 `(指令, 参数下标, 值, 实例)`。
`CommandParameterTypes` 用 Acornima 解析工程里的 `GameCreator/script.js`(缺失时退回 `GameCreatorLib/gamecreator.js`),
从 `CommandExecute.command_<id>` / `precompile_<id>` 里跟踪 `cmd.params[N]`:

- 跟踪会穿过局部变量、指令自己声明的辅助函数(带形参代入)、以及脚本共享的辅助对象;
- `precompile_13` 这类"只为 eval 拼字符串"的代码,从字符串拼接里还原访问器;
- 记录**守卫条件**(真值判断或与常量比较,含取反与 switch-case),只有满足守卫的实例才会把该槽当作变量引用;
- 结果是一张 `(指令, 参数下标) → 变量种类 / 自定义模块` 的表,不落盘缓存;脚本缺失或解析失败就置 `Available=false`,调用方记一行日志后继续用其它来源。

这张表同时被变量表(§3.3)和扩展 `attr.id` 归属(§4.3)使用。

## 5. 规则:算法不得依赖作者命名

被禁止的:用作者写的参数名做查表、匹配、猜测(历史实现里有过的 `_known` 参数名表、`名字 + "Id"` 联想、`is/has/use/allow/can` 前缀与 `str/name/text/path/url/title` 子串先验,均已删除)。

允许的(引擎/IDE 自有约定,不是作者命名):`___cmdID`、`commands`、`params` 等引擎数据/形参名;`getString/setVariable/…` 等引擎变量 API;`Custom<Kind>` 模块名与 `CommandExecute.command_<id>`/`precompile_<id>` 命名;`avatarAct`/`signalId`/`partID`/`avatarID`/`standAvatarID` 等引擎数据字段;从 loader 抄下来的列表/目录文件名;`*Params.ts` 与 `CodeIDELocal`;`compType` 数值与 `compParam` 键;扩展目录路径判断。

代价(有意接受):函数体没约束、数据里也没出现过的参数退回数值框;面板不再显示作者别名。

## 6. 验证流程

```powershell
dotnet test GCSlayer.sln                                   # 单测
dotnet build GCSlayer.sln
GCSlayer.exe fixup  <project>                              # 修复
GCSlayer.exe fixup  <project>                              # 再跑一次,比对哈希应零差异
GCSlayer.exe verify <project>                              # 期望输出 OK: no unresolved references,exit 0
```

`ProjectVerifier` 刻意**不与修复实现共享逻辑**:它用自己的一套正则/扫描重新推导期望,校验的是修复范围的一个子集(条目↔文档双射、变量引用覆盖、扩展列表与脚本一致、扩展参数与文档控件互相覆盖、指令参数是否在指令定义里声明、脚本是否仍可解析)。它同时是"修复是否越权"的守卫:多出来的文档/控件都会被报出来。

## 7. 已知限制

- 参数表只覆盖脚本里**有编译产物**的指令(LoveNest 上是 36 条);自定义指令的参数是对象键,不在这张表里。
- 页面识别要求实例带尾记录或 `…command…` 属性;若某工程把两者都剥掉,这部分就不贡献表驱动的 id(其它来源仍在)。
- 参数表不从调用的首元素(如 `params[15][0]-3`)反推条目号;同一模块内若有两个参数名集合相同的文档,依旧弃权。
- release 删掉的名字(条目名、变量名、块名、别名)没有来源可恢复,只能是 stub 或留空。
- 存档文件不存在时,变量表的长度只能靠引用推导;作者未使用过的变量无法得知。
