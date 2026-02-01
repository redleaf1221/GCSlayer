# GCSlayer

A powerful CLI tool for recovering and reverse-engineering GameCreator projects from released game builds.

## Overview

GCSlayer extracts and decrypts GameCreator project files from compiled games, restoring them to editable project format. It handles multiple encryption schemes used by the GameCreator engine and reconstructs missing project assets.

## Features

* Full Project Recovery: Converts encrypted game builds back to editable projects.
* Asset Decryption: Handles images, JSON, audio, and video encryption, extracts merged json.
* Script Extraction: Handles obfuscated scripts.js, stripping framework codes.
* Patch IDE: Enables access to encrypted files beyond what the original author can see.

## When to Use GCSlayer

* You want to mod a GameCreator game but lack source access
* You need to recover lost project files from a compiled build
* You need to bypass IDE limitations for legitimate development work

## Quick Start

**Important**: You must run `init` once before recovering any projects.

1. **Download** from [releases](https://github.com/redleaf1221/GCSlayer/releases/latest) or build it on your own.
2. **Initialize tool**(required once): `GCSlayer.exe init "/path/to/gamecreator/ide"`.
3. **Recover project**: `GCSlayer.exe recover "/path/to/game/install"`.
4. **Wait** for finish.
5. **Open** the generated project in your IDE.

## Commands

### init - Prepare IDE and template

#### Usage

`GCSlayer.exe init <ide_path>`

#### Arguments

* `ide_path`: Path to the IDE installation directory.

#### What it does:

Patches the GameCreator IDE to accept recovered projects and extracts the core template.

More precisely:

1. Patches IDE validation to bypass project signature checks.
2. Removes script obfuscation from core templates.
3. Copies prepared templates to the tool's working directory.

### recover - Reconstruct projects

#### Usage

`GCSlayer.exe recover <game_path> [options]`

#### Arguments and options

* `game_path`: Path to the game installation directory.
* `--local_source`: Local repository for missing assets (optional, default to `./file_repo`).
* `--output`/`-o`: Output directory (defaults to tool folder).

#### What it does:

Decrypt all the assets and script, and give you a working project.

### drm_crypto – Direct decryption/encryption

#### Usage

`GCSlayer.exe drm_crypto <file_path> [options]`

#### Arguments and options

* `file_path`: Path to the file you want to decrypt or encrypt.
* `--output`/`-o`: Output file path.
* `--encrypt` Specific this to do encryption instead of decryption.

#### What it does:

Decrypt or encrypt using a certain encryption by GameCreator, which is used on some project files.

## Requirements

* .NET 9.0
* Node.js (for script decryption)

## Disclaimer

This tool is for educational and recovery purposes only. Use only on games you own or have explicit permission to modify. The authors are not responsible for any misuse.

## License

GPL-3.0
