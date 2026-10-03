# Shared Editor Boundary

`SharpOcarina.Shared` is the neutral runtime layer used by the Scene, Modeling,
and Dungeon editor surfaces. It is deliberately independent of OpenTK,
`ZScene`, ROM display-list types, and editor controls.

## Ownership

- `ProjectDocument` owns document identity, dirty state, revisions, and batched change notifications.
- `SharedAssetRepository` owns the shared authored-geometry library location and legacy migration.
- `SharedSceneHandoff` carries room layout, doorway sockets, native connections, asset references, and gameplay packages between editors.
- `SharedGameplayContract` carries renderer-neutral actor, item, flag, reward, and cause/effect data.
- `Project64PlaytestService` owns emulator configuration and process launch state. ROM materialization remains in Archarina64.
- `SharedEditorWorkspace` owns one in-process session boundary and isolates handoff snapshots by publisher and document revision.

SharpOcarina and Archarina64 retain separate native scene models, mesh formats,
undo stacks, WinForms controls, and OpenTK contexts. The SharpOcarina host embeds
Archarina64 through its bridge; it does not merge the two native renderers.

## Persistence

Native SharpOcarina scene XML remains unchanged. Shared Scene-side metadata is
stored beside it as `<scene>.xml.shared.xml`. Authored geometry remains in the
shared LocalAppData library and is referenced by path or asset key rather than
copied into a native scene document.

## Validation

Build the combined solution from the Archarina64 checkout:

```powershell
MSBuild src\Archarina64\Archarina64.sln /t:Rebuild /p:Configuration=Debug /p:Platform=x64
```

Then run the repository-level verification script. Add `-UiSmoke` when the
WinForms host lifecycle should also be exercised:

```powershell
.\tools\verify-shared-editor-merge.ps1 -UiSmoke
```

The script rebuilds the combined solution, runs the Archarina regression suite,
and runs the SharpOcarina input tests.
Release packaging remains subject to Archarina's ROM/resource provenance audit.
