## GCSlayer

A powerful CLI tool for recovering and reverse-engineering GameCreator projects from released game builds.

### Overview

GCSlayer extracts and decrypts GameCreator project files from compiled games, restoring them to editable project format. It handles multiple encryption schemes used by the GameCreator engine and reconstructs missing project assets.

### Features

* Full Project Recovery: Converts encrypted game builds back to editable projects.
* Asset Decryption: Handles images, JSON, audio, and video encryption, extracts merged json.
* Script Extraction: Handles obfuscated scripts.js, stripping framework codes.
* Missing Asset Inference: Automatically fills missing assets from templates/local sources.
* Project decryption: Decrypt certain files that are encrypted even in original projects.

### Quick Start

1. Download from release or build it.
2. Run `GCSlayer.exe recover "/path/to/game/install"`
3. Wait for finish.

### Arguments

`GCSlayer.exe recover game_path --local_source /path/to/repo --missing_list missing.json --output /path/to/output`

* `game_path`: Path to the game installation directory
* `--local_source`: Local repository for missing assets (optional, default to `./file_repo`)
* `--missing_list`: Output path for missing assets list (optional)
* `--output`/`-o`: Output directory (defaults to game folder name)

### Requirements

* .NET 9.0
* Node.js (for script decryption)

### Disclaimer

This tool is for educational and recovery purposes only. Use only on games you own or have explicit permission to modify. The authors are not responsible for any misuse.

### License

GPL 3.0
