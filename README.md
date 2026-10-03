# Archarina64

Archarina64 is a standalone Windows editor project built from the open-source
SharpOcarina scene/modeling editor and extended with a dungeon-builder workspace.
It is the home for the editor itself, not an OoT decompilation repository.

## What it contains

- SharpOcarina-derived WinForms/OpenTK scene and mesh modeling tools.
- Archarina64 dungeon authoring: rooms, tiles, actors, transitions, collision,
  gameplay metadata, and project-side authoring workflows.
- `SharpOcarina.Shared`, the shared document, authored-asset, handoff, and
  Project64 playtest contracts used by both editor surfaces.
- Blender import/export helpers and source-level tests that belong to the editor.

The application continues to use the `SharpOcarina` namespace because it is a
source-level fork and extension of SharpOcarina. The upstream lineage and GPLv3
license notice are preserved in `src/Archarina64/ReadMe.txt` and
`src/Archarina64/LICENSE`.

## What it does not contain

This repository is **not** `zeldaret/oot`, an OoT decompilation, a ROM, or a
collection of extracted Nintendo game assets. It intentionally does not contain
OoT decomp source, ROM images, extracted rooms, actors, textures, display-list
payloads, title-screen data, sounds, or compiled game patches.

Users must provide their own legally obtained ROM and any locally extracted
assets required for their workflow. Those files stay outside this repository and
are restored at runtime from a user-selected workspace.

## Build

Requirements:

- Windows
- Visual Studio 2022 with the .NET Framework 4.6.2 developer tools, or a
  compatible MSBuild installation
- PowerShell

From the repository root:

```powershell
Set-Location src\Archarina64
.\restore_sharpocarina_dependencies.ps1
dotnet msbuild .\Archarina64.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64
```

The restore script downloads NuGet dependencies and the pinned SharpOcarina
v1.67 helper package into ignored local folders. It does not add those binaries
to Git. The executable is written to
`src\Archarina64\bin\x64\Debug\Archarina64.exe`.

The current alpha is intended for source review and editor development. Native
N64 material/display-list round trips, ROM injection, and emulator gameplay are
tracked acceptance areas rather than claims of completion.

## Project layout

```text
src/
  Archarina64/       merged modeler and dungeon-builder application
  SharedEditor/      shared document and editor contracts
docs/                provenance and project-boundary notes
```

## Lineage

Archarina64 is an independent fork/evolution of
[Nokaubure/SharpOcarina](https://github.com/Nokaubure/SharpOcarina). It is not
presented as the OoT decomp project and is intentionally published in its own
repository so the editor source, dungeon builder, and their build instructions
have a clear identity.

