# Sandbox Source Audit - 2026-09-22

## Runtime Boundaries

- `MainForm.LoadZsceneData` decodes scene headers, room records, collision, actors and setups.
  `LoadZscene` also binds legacy controls and configures the renderer. It is not a standalone loader API yet.
- `ZScene` captures imported display-list geometry, exports native scene/room commands and writes packages.
  `ZRoom.Clone` retains shared mesh state, so it must not implement independent room tiles.
- `RomAssetCatalog` extracts main-setup room actor presets and object banks from the supplied ROM.
  Preset categories follow the editor's native actor metadata contract.
- `SandboxWorkspace` supplies the visible UI, while an invisible legacy control container services backend dependencies.
  Palette actor placement adds object dependencies, marks the project dirty and records undo state.
- Scene collision is shared. Independent room-section ownership, transforms, connectors and puzzle-flag remapping remain unfinished.

## ROM Data Boundary

All new extraction, imported mesh/texture working files, screenshots, logs and native test packages
use `%LOCALAPPDATA%/Archarina64`. They are not application content or embedded resources.
The source ROM remains read-only; the sandbox's injection command requires a separate output path.

The inherited checkout contained 395 tracked object/scene/room payloads, plus two embedded title-screen
binary resources. These payloads have been moved to a private baseline quarantine outside the checkout,
removed from project content/resource declarations, and excluded from future additions via `.gitignore`.
The original modeling checkout has not been altered. Old generated payloads and imported textures were
also moved out of Archarina64's build/source folders.

The inherited Git history STILL contains the original assets. Do not publish this repository history.
Publication needs a clean source-only repository/export after reviewing remaining inherited images,
audio, fonts and patch resources. Release builds are blocked until that provenance audit is complete.
This development executable is not certified as a distributable artifact.

Final housekeeping caveat: OneDrive stalled a follow-up move of the obsolete build-local
`arch_tile_library` cache and a full Git whitespace check. Those commands were stopped;
obsolete ignored cache files may remain under the build directory. They are not project
content and the running application no longer reads or writes that location. Do not zip
the development build directory for distribution. The release gate remains in force.

## Version Finding

The baseline's NTSC 1.2 ROM metadata contains scene-table addresses but omits the object table.
Reading address zero produced bogus object ranges. Import now resolves the table using a unique
match of three common-bank DMA ranges from the supplied ROM. Missing or ambiguous matches fail
import. Other ROM versions need explicit object-table metadata.

## Verification

The local NTSC 1.2 backup yields 2,961 palette entries, 386 rooms and 380 distinct object banks,
including 117 distinct scene-transition presets read from main scene command 0x0E.
The catalog samples main room headers only; this is NOT an exhaustive inventory of every alternate
setup, actor overlay, animation, dialogue record or isolated mesh in the ROM.

Tests open the source scene through the SharpOcarina backend, select every boss palette entry,
place an actor with object dependencies, undo and redo it, and generate a native scene plus 12 rooms.
The source ROM hash remains unchanged. Package generation is not emulator/gameplay verification.
Missing custom preview meshes use the inherited fallback marker; bundled ROM models are no longer used.

The reported boss-list closure could not be reproduced: all boss selections passed. Automated test
windows close themselves on completion. Palette dragging now requires crossing the OS drag threshold;
runtime errors are logged privately to `%LOCALAPPDATA%/Archarina64/errors.log`.

## Connection Authoring

## Section Tile Storage And Verification

`SectionTile.cs` preserves selected visual/collision geometry, UVs, normals, one-based vertex colors,
material settings and render flags. Tile textures use content-addressed copies under
`%LOCALAPPDATA%/Archarina64/tiles/textures`; library XML and immutable scene-geometry snapshots also
remain under that private root. Scene XML stores a snapshot reference, not embedded tile payloads.
Do not publish these private caches. Tile projects currently require Archarina64 and the cache.

Main-scene collision references are preserved only within the source scene by default. Cross-scene
placement requires an explicit destination surface override; collision name tags are removed from
captured groups so legacy exporter tag parsing cannot override this mapping. Unsupported animated,
billboard, custom, multitexture and LOD groups are rejected. Alternate-setup placement is not supported.

Verified: synthetic deep-copy/rotation/UV/color/surface tests, private local-ROM capture, two
placements, visual+collision undo/redo, actual editor save/reopen, native collision polygon count
and surface ID, and unchanged ROM SHA256. The real-ROM regression selects one visual triangle and
one collision triangle to exercise the data path; it does not prove a watertight floor-and-wall tile.
In-game collision/visual acceptance and ROM injection remain pending.

### Connection And Viewport Checks

Viewport/navigation regression checks additionally cover switching scene 0 -> scene 1 room 1 ->
scene 0 through the visible source-room command, room selector synchronization, and toggling
compiled-mesh preview culling without changing export group flags. Depth writes are reset before
framebuffer clearing; editable rooms reset native shader/multitexture state. Private screenshot
review confirmed the stale-depth missing-patch improvement, not full rendering fidelity.

Scene-level transition records are now separate from room actors. Placement requires two distinct
existing rooms, installs known object dependencies in both, and supports undo/redo. Validation uses
the editor's native scene and actor metadata contracts, including signed room indices and the
six-bit actor index in params bits 10-15. Source camera references are not copied; new connections
use the default -1 camera on both sides.
Parameters are preserved, not remapped; presets using reserved index bits are rejected.

Automated checks pass for malformed transition pointers, invalid placement, dialog cancellation,
both-room object dependencies, undo/redo and emitted native transition fields. Source ROM hash is
unchanged. Tile collision ownership, openings, geometric sockets, gameplay flag remapping, ROM
injection and emulator traversal remain unverified/unimplemented acceptance work.
