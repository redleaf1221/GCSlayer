# GCSlayer

A powerful CLI tool for recovering and reverse-engineering GameCreator projects from released game builds.

## Overview

GCSlayer extracts and decrypts GameCreator project files from compiled games, restoring them to editable project format. It handles multiple encryption schemes used by the GameCreator engine and reconstructs missing project assets.

## Features

- Full Project Recovery: Converts encrypted game builds back to editable projects.
- Asset Decryption: Handles images, JSON, audio, and video encryption, extracts merged json.
- Script Extraction: Handles obfuscated scripts.js, stripping framework codes.
- Patch IDE: Enables access to encrypted files beyond what the original author can see.

## When to Use GCSlayer

- You want to mod a GameCreator game but lack source access
- You need to recover lost project files from a compiled build
- You need to bypass IDE limitations for legitimate development work

## Quick Start

**Important**: You must run `init` once before recovering any projects.

1. **Download** from [releases](https://github.com/redleaf1221/GCSlayer/releases/latest) or build it on your own.
2. **Initialize tool**(required once): `GCSlayer.exe init "/path/to/gamecreator/ide"`.
3. **Recover project**: `GCSlayer.exe recover "/path/to/game/install"`.
4. **Wait** for finish.
5. **Open** the generated project in your IDE.

## Commands

* ### init - Prepare IDE and template

#### Usage

`GCSlayer.exe init <ide_path>`

#### Arguments

- `ide_path`: Path to the IDE installation directory.

#### What it does:

Patches the GameCreator IDE to accept recovered projects and extracts the core template.

More precisely:

1. Patches IDE validation to bypass project signature checks.
2. Removes script obfuscation from core templates.
3. Copies prepared templates to the tool's working directory.

* ### recover - Reconstruct projects

#### Usage

`GCSlayer.exe recover <game_path> [options]`

#### Arguments and options

- `game_path`: Path to the game installation directory.
- `--local_source`: Local repository for missing assets (optional, default to `./file_repo`).
- `--output`/`-o`: Output directory (defaults to tool folder).

#### What it does:

Decrypt all the assets and script, and give you a working project.

* ### drm_crypto – Direct decryption/encryption

#### Usage

`GCSlayer.exe drm_crypto <file_path> [options]`

#### Arguments and options

- `file_path`: Path to the file you want to decrypt or encrypt.
- `--output`/`-o`: Output file path.
- `--encrypt` Specific this to do encryption instead of decryption.

#### What it does:

Decrypt or encrypt using a certain encryption by GameCreator.

It is used on `project.gamecreator`, `asset/json/animation/animationSignalList.json` etc., hiding the project signature k1.

* ### z1 – Save file password restoration

#### Usage

`GCSlayer.exe z1 <z1>`

#### Arguments

- `z1`: Z1 string from config.json in the project.

#### What it does:

Fetch the real password for release game save file, which is a AES encrypted zip archive.

You can get the z1 from project `asset/config.json`.

* ### fixup – Repair a recovered project

#### Usage

`GCSlayer.exe fixup <project_path>`

#### Arguments

- `project_path`: Path to a project produced by `recover`.

#### What it does:

The release pipeline (see `IDEProjectRelease.onReleaseGameComplete` in the core template) deletes
every editor list before shipping, so a recovered project only carries the template stub for them.
`recover` runs this step automatically; run it by hand after recovering with an older build.

More precisely, it refills what the released data still allows to derive:

1. Rebuilds the item lists (animations, avatars, standing avatars) from their data files.
2. Reassembles `custom/customGameAttribute.json` from the four attribute files the release splits
   it into, bringing the author's attribute names back.
3. Extends the variable lists with a slot for every id the shipped data references: the `set variable`
   commands, the save file, and every other command parameter the editor script types as a variable
   reference (see *Command parameters* below). Existing names and entries are kept, never replaced.
4. Rebuilds the custom condition/number/string/behaviour lists from the compiled game script and
   recovers the **parameters of every entry** by parsing that script (see below).
5. Pads the remaining name lists and fills every entry that lost its name with a stub, so the IDE
   shows an entry instead of an empty one.
6. Drops the compiled copies of `Game/system/*Params.ts` from `Game/GCMain.ts`. The IDE generates
   that file from the extension documents and compiles it into the game script, so a bundle built
   from documents the release lost only carries stale classes. A class is removed only when its name
   occurs exactly three times in the script — the definition itself (`var`, `function`, `return`) —
   and the result must still parse, so anything the game really uses is kept.

Names that never made it into the release cannot be recovered; everything else is derived from the
game itself.

#### Extension parameters

The editor compiles a custom condition/number/string into the game script as
`CustomCondition.f7 = f7` with the signature `function f7(trigger, p)`, where `p` is the block the
user fills in: every `p.<name>` the function reads is one editor control. `Game/GCMain.ts` is parsed
into an ESTree AST (Acornima), so comments, strings, template literals and regular expressions can
never be mistaken for code — the game still carries a commented out earlier revision of some
functions, which must not contribute.

Each parameter is classified by the expression it sits in:

| in the script | control |
| --- | --- |
| `if (p.opt == 0) … else if (p.opt == 1)` | dropdown of the compared values |
| `p.useVar ? getString(p.name1) : p.name` | checkbox, string variable picker, text |
| `variable.getVariable(p.id)` / `getSwitch` / `getString` | number / switch / string variable picker |
| `p.modelData.dataID` | structure picker |
| `p.trophy[i]` | list |
| `CustomGameNumber["f" + p.x[0]](trigger, p.x[1])` | custom number reference picker |

Values the function itself does not constrain (a flag it only forwards, an id) take the kind the
shipped data holds for that parameter — the object of the call record described below — when it has
one; otherwise they fall back to the engine default of the kind, which is a plain number. An author
chosen name decides nothing: a parameter called `isThing` that no expression constrains and no
instance shows gets a number control. Dropdown labels are always the numbers the function compares
against, and no alias is written, so the panel shows the parameter name as the label.

An existing document that carries attribute ids other than the ones `fixup` derives or recovers is
treated as author data and never rewritten.

The release deleted the extension documents, but the shipped command pages still store the calls of
those entries as `[functionID, { parameter: value, … }, [ { attributeID: value, … } ]]`: the editor
writes every value twice, once keyed by the parameter name and once keyed by the attribute id of the
document, and reads the second one. `fixup` recovers those ids and writes them into the rebuilt
document, so a value the author had set keeps its place instead of falling back to the control's
default. Both records line up positionally, and an id is only taken when every call of that entry
agrees. Which document a call belongs to is decided by the command parameter table (*Command
parameters* below) when the script describes the slot the call sits in — the "if" command stores a
custom condition in one slot and a custom number in another, so two documents of different modules
that declare the very same parameter names are still told apart. A call the table cannot classify is
matched by its parameter names alone, and an ambiguous vote is dropped.

#### Command parameters

`fixup` also reads what every command does with `cmd.params[N]`. The editor compiles each command into
`CommandExecute.command_<id>(commandPage, cmd, trigger, triggerPlayer)`, and the commands that have to
prepare something into `precompile_<id>`; those functions are the only surviving description of the
parameter layout: which slot the engine passes to `Command.gameWorld.getWorldVariable`,
`triggerPlayer.variable.getString` or `variable.variables[id]`, and which slot it expands into
`CustomCondition["f" + id](trigger, block)`.

The project's own `GameCreator/script.js` (falling back to `GameCreatorLib/gamecreator.js`) is parsed
into an ESTree AST (Acornima), so comments, strings and regular expressions can never be mistaken for
code. A value is followed from `cmd.params[N]` through local variables, through the helper functions
the command declares (the `getValue` of the "if" command reads its `param[1]` as the world number
variable of `cmd.params[2]`) and through the shared helpers of the script
(`CustomCompData.getSuperNumber`), so a slot inside a union is typed as precisely as the engine reads
it. Accessors the engine only builds as text for `eval` — the whole body of `precompile_13` — are
recovered from the string concatenation that assembles them. Nothing is cached on disk: the table is
rebuilt for every run, and when the script is missing or does not parse, `fixup` says so and keeps
working with the `set variable` command and the save file.

A parameter at JSON index `i` of a command instance is `cmd.params[i - 1]`: the engine parses an
instance as `[cmdID, parameters…]` and drops the trailing per instance record, and the "set variable"
instance `[13, resultType, varID, …]` is read as `cmd.params[0]` = result type, `cmd.params[1]` =
variable id, `cmd.params[2]` = symbol, `cmd.params[3]` = value type. A custom command (id ≥ 10000) is
`[10002, { attributeID: value, … }, ___cmdID]`, whose `cmd.params[0]` is that object.

The editor also stores a constant and a variable id in the same slot and separates them with a flag or
a mode: `command_17` waits `cmd.params[0]` milliseconds and reads the same slot as a variable id only
while `cmd.params[3]` is set. The table records the condition a slot needs, and `fixup` only takes an
id out of an instance that satisfies it.

Note that the game script registers two game numbers under a misspelled module
(`CumtomGameNumber.f6`/`f11`), i.e. exactly the two numbers missing from `CustomGameNumber`; they are
recovered into the real list so those entries are editable too. The engine cannot call them, which is
the author's original bug.

The IDE regenerates `Game/system/CustomConditionParams.ts` (and its siblings) when the corresponding
extension list is saved, so those generated files refresh once the project is edited in the IDE.

#### What the recovery cannot see

Not everything under `asset/json/custom/` was lost, and not everything that is missing can be
derived, so `verify` reports rather than hides the difference:

* The author never named seven of his custom commands, three data structures and the one custom
  object event type. The documents still say so (`"name": ""`, `"varName": ""`), and the
  auto-generated `Game/system/CustomCommandParams.ts` shipped with an empty name for exactly those
  commands, so the blanks are his, not the release's.
* Three data entries per module list and the extension list names were blanked by the release; a
  name that is not mentioned anywhere in the release cannot be recovered and stays blank.
* Event data still references parameters of extension entries the author later rewrote (e.g. a
  `w26str*` variant of custom string 10, or an `opt`/`saveID` pair for value 16). Those documents are
  gone, the compiled function no longer reads those names, and the engine ignores unknown keys, so
  the recovered controls follow the function.
* A parameter the compiled function does not constrain and whose entry has no shipped instance is a
  number: a forwarded flag, for example, has no evidence of anything else, and the tool never reads a
  kind out of the parameter's name. Aliases are not recovered either, so a panel labels every control
  with the parameter name.
* `moduleID` 9 and 10 are referenced by `dataStructure/ds15002.json` but no such custom module
  exists — a stale reference in the author's own structure definition.

* ### verify – Check a project for references the IDE cannot resolve

#### Usage

`GCSlayer.exe verify <project_path>`

#### What it does:

Re-derives the expectations from the project data itself (item documents, command references, the
save file, the recovered game script) and fails with a non-zero exit code when something is left
unresolved: a list entry without a document, a document without an entry, an id the data uses but
the lists do not cover, a command parameter the shipped command definition does not declare, or an
extension parameter the compiled function reads while its document offers no control (the reason a
recovered entry opens with an empty block).
`recover` runs it automatically at the end.

## Requirements

- .NET 9.0
- Node.js (for script decryption)

## Tests

```bash
dotnet test GCSlayer.sln
```

The suite covers the DRM codec, the `z1` password derivation, the script analysis helpers, the
command parameter table extracted from synthetic editor scripts, the file helpers and the whole list
repair against a miniature recovered project. No IDE component is needed; the only download is NuGet
restoring xunit into the local package cache.

## Credits

- Me, of course.
- **print_0**: For GCJSDecrypt, the deobfuscation tool for KDSEncode.
- **Karson.DS**: For GameCreator, the **slightly** flawed game engine.


## Disclaimer

This tool is for educational and recovery purposes only. Use only on games you own or have explicit permission to modify. The authors are not responsible for any misuse.

## License

GPL-3.0
