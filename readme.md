# GCSlayer

把已发布的 GameCreator 游戏还原成可编辑工程,并顺手做逆向分析。

- 实现细节(服务划分、修复步骤、三个分析算法、不变量、验证方式):[docs/impl.md](docs/impl.md)
- GameCreator 引擎的实现笔记(发布管线、列表模型、扩展/指令/变量编码,含依据行号):[docs/re.md](docs/re.md)

## 概览

GCSlayer 从编译后的游戏里解出并解密 GameCreator 工程文件,还原成 IDE 能打开的工程;它处理引擎用到的多种加密方式,并重建 release 打包时删掉的那部分编辑器数据。

## 能做什么

- 完整工程还原:加密的游戏发布包 → 可编辑工程
- 资源解密:图片/JSON/音频/视频,以及合并进 `startup.json` 的 json
- 脚本还原:处理混淆过的 `script.js`,剥掉与游戏无关的框架代码
- IDE 补丁:让 IDE 接受没有原始签名的工程
- 缺失数据推导:从幸存的游戏数据反推条目、变量、扩展参数与文档

## 什么时候用

- 想给某个 GameCreator 游戏做二次开发,但拿不到工程
- 工程文件丢了,只剩编译产物
- 需要突破 IDE 的限制做正当的开发工作

## 快速开始

**先跑一次 `init`**,之后才能 recover。

1. 从 [releases](https://github.com/redleaf1221/GCSlayer/releases/latest) 下载,或自行构建。
2. 初始化(只需一次):`GCSlayer.exe init "<gamecreator ide 目录>"`
3. 还原工程:`GCSlayer.exe recover "<游戏安装目录>"`
4. 等它跑完。
5. 用 IDE 打开生成的工程。

## 命令

### init — 准备 IDE 与模板

`GCSlayer.exe init <ide_path>`

- `ide_path`:GameCreator IDE 安装目录。

给 IDE 打补丁以接受还原出的工程(跳过工程签名校验),并解掉核心模板的脚本混淆、把准备好的模板复制到工具工作目录。

### recover — 还原工程

`GCSlayer.exe recover <game_path> [--local_source <dir>] [-o <dir>]`

- `game_path`:游戏安装目录。
- `--local_source`:缺失文件的本地仓库(默认 `./file_repo`)。
- `--output`/`-o`:输出目录(默认取游戏目录名,落在当前工作目录)。

解密脚本与资源、抽取合并的 json,再依次跑 **Repair**(同 `fixup`)与 **Verify**,产出一个可直接打开的工程。

### fixup — 修复已还原的工程

`GCSlayer.exe fixup <project_path>`

- `project_path`:`recover` 产出的工程目录。

release 打包时会删掉编辑器专用的列表与文档,所以刚还原的工程里这些文件只是模板 stub。`fixup` 把"发布数据还能推导出来"的部分补回去(条目列表、`customGameAttribute.json`、变量表、扩展列表与文档、其余名字列表),并清掉 `Game/GCMain.ts` 里 IDE 会重新生成的参数 API 编译产物。`recover` 会自动跑这一步;用旧版本还原过的话可以单独重跑。

顺序是**先补齐、再推导、最后按条目覆盖**;其中"补齐"与"覆盖"是两件事、两种仓库,不能混:

- **补齐(第 6 步)**:`FileService.CopyDirectoryAsync(..., overwriteExisting: false)` 只补 release 删掉的**缺失文件**,不覆盖游戏自己发布的内容。它优先用 `file_repo/<工程名>`,找不到就用 `file_repo/Default`——这里用模板仓库是有意的,它提供的是容器文件与模板自带的部件(模板 JS 库、空列表骨架),保证工程"不缺文件"。
- **覆盖(第 8 步)**:修复之后再把这个**该游戏专属**仓库的内容**逐条目**合进去;`fixup` 会打印用的是哪个仓库及用了哪个规则。`Default` **绝不参与覆盖**——它是与这个游戏无关的普通模板,合并它的名字、`type` 组或文档只会把工程推离这个游戏。所以覆盖只认两样:显式 `--local_source`,或 `file_repo/<工程名>`;两者都没有时覆盖层什么都不做,只打印一行说明(例如 `- repo overlay: file_repo/<name> not found; ...; skipped`)。

覆盖的优先级是仓库的条目压过推导出的条目:列表里仓库的非空名字胜出,仓库没有该条目或名字为空则保留工程已有的,数组只加长不缩短,`type` 组只增不减;文档只在仓库是**该游戏专属**时才整份覆盖推导结果(那是同一个游戏的真数据)。

三条保证:重复运行内容不变(幂等);带作者痕迹的扩展文档不覆盖;改动后脚本仍必须能被解析。

### verify — 检查工程里 IDE 解不开的引用

`GCSlayer.exe verify <project_path>`

用**独立于修复逻辑**的实现重新推导期望,发现未解引用就以非零码退出:条目没有文档、文档没有条目、数据用到的 id 没被列表覆盖、指令参数在指令定义里不存在、扩展函数读的参数在文档里没有对应控件(即"打开是空块"的原因)等。`recover` 结束时自动执行。

### drm_crypto — 直接加解密

`GCSlayer.exe drm_crypto <file_path> [--output <path>] [--encrypt]`

对单个文件做 GameCreator 的加解密,用于 `project.gamecreator`、`asset/json/animation/animationSignalList.json` 这类藏了工程签名 `k1` 的文件。

### z1 — 存档口令

`GCSlayer.exe z1 <z1>`

由工程 `asset/json/config.json` 里的 `z1` 推导发布版存档(AES zip)的真实口令。

## 流程分析

`recover` 的九个阶段:

1. 建工作区(需要 `init` 过的模板)
2. 解密 `script.js` → `Game/GCMain.ts`;顺带从脚本判断图片/JSON/音频/视频的加密状态
3. 按该状态批量解密资源
4. 解开 `asset/json/startup.json`(zip),把其中全部条目写回工程
5. 按 `config.json` 的工程名重命名 `.gamecreator`
6. **补齐缺失文件**:从 `file_repo/<工程名>`(缺失时用模板仓库 `file_repo/Default`)复制 release 删掉的文件,不覆盖游戏自己发布的内容
7. Repair(同 `fixup`)
8. **按条目覆盖**:**该游戏专属**仓库(显式 `--local_source` 或 `file_repo/<工程名>`)的名字/文档逐条目合进刚修复好的工程;没有专属仓库就跳过,`Default` 在此不参与
9. Verify

**Repair 补什么**:条目列表(动画/立绘/行走图,从数据文件与引用反推)、`customGameAttribute.json`(由 release 拆出的四个属性文件重装)、变量表(`set variable` 指令 + 存档 + 编辑器脚本参数表指认的其它指令参数,**只补齐不删除**)、扩展列表与文档(从编译脚本 AST 读函数参数,从事件数据反推文档原始 `attr.id`)、其余名字列表长度、以及 `GCMain.ts` 里生成类的清理。

**为什么很多名字恢复不出来**:release 会删掉整个 `asset/json/custom`、两侧变量表、以及一批列表文件,名字只存在于被删的列表数组里,发布包里再无来源;能恢复的只有"别的幸存数据能推导出来"的部分,剩下的名字只能靠 `file_repo` 里那些从其它途径拿到的数据按条目补进去。

## 环境要求

- .NET 9.0
- Node.js(脚本解密用)

## 测试

```bash
dotnet test GCSlayer.sln
```

覆盖 DRM 编解码、`z1` 口令推导、脚本分析、指令参数表、文件工具,以及针对一个迷你还原工程的整套列表修复与校验。不需要 IDE 组件,唯一的外部下载是 NuGet 还原 xunit 到本地缓存。

## Credits

- Me, of course.
- **print_0**:GCJSDecrypt,KDSEncode 的反混淆工具。
- **Karson.DS**:GameCreator,那个**稍微**有点问题的游戏引擎。

## Disclaimer

本工具仅用于学习与数据恢复。请只对自己拥有或获得授权的游戏使用,作者不对任何误用负责。

## License

GPL-3.0
