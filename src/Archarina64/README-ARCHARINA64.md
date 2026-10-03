# Archarina64

Existing-asset OoT scene-builder fork of the customized SharpOcarina modeling editor.

## Staged Shared-App Merge

Archarina64 remains a separate Dungeon Editor surface inside the shared
SharpOcarina application. `SharpOcarina.Shared` is the only shared runtime
assembly: it carries document lifecycle, authored-asset access, neutral Scene
handoffs, gameplay metadata, and Project64 launch configuration. Archarina64
continues to own ROM decoding, native `ZScene` materialization, dungeon tile
geometry, collision, progression analysis, and ROM injection.

SharpOcarina continues to own its native Scene and Modeling editors and their
OpenTK renderer. The in-process bridge embeds Archarina64 without combining
the native scene models or renderer contexts. Scene and Dungeon handoffs are
cloned and revision-checked; authored `.sog.xml` assets come from the shared
session repository. Sharp Scene-side shared metadata is persisted in a
`.shared.xml` companion file, leaving native Scene XML unchanged.

This boundary is intended for staged source integration and Debug/x64
validation. It does not claim that native ROM injection or emulator gameplay
acceptance is complete; those remain explicit Archarina acceptance gates.

Baseline: `51b637dba0ef35f9a6d975d8db3cdc507319c431`.
Branch: `archarina64/scene-builder`. The independent Git repository retains baseline history;
the `baseline` remote points to the sibling modeling checkout. No repository was published.

## Build and Run

### ROM and Asset Boundary

Users must supply their own legally obtained ROM and any extracted game assets used by
the editor. Public builds contain editor source, shared tooling, neutral UI resources,
and open-source dependencies only; they do not contain a ROM, extracted rooms, textures,
actors, `.zobj` files, display-list payloads, title-screen data, or other game assets.
The editor reads those files from the user-selected local ROM/object workspace at run
time. Do not add ROMs or extracted game assets to a public source repository or release
archive.

Open `Archarina64.sln` in Visual Studio 2022 and build Debug/x64. Dependencies can be
restored with `restore_sharpocarina_dependencies.ps1`, which now targets this solution.
Run `bin/x64/Debug/Archarina64.exe`. Output stays in this checkout for all configurations.

Use **Project > Open ROM / import library** to extract the local ROM into categorized palettes.
The visible interface is now a dedicated sandbox with a viewport, scene contents, inspector,
grid controls, and placement undo/redo. Actors drag into the viewport; object banks add room
dependencies. Double-clicking a room opens its source scene. Selected sections can now be saved
as tiles and appended to the active room; whole-room assembly is not yet available.
Use **Project > Browse ROM scenes** to choose a scene and one of its rooms, or select an entry
in the Rooms palette and click **Open source room**. The toolbar Room selector changes the
active room within that scene. Opening another source scene replaces the current scene after
the unsaved-changes prompt; it does not add a room to the current project.

**Two-sided preview** overrides compiled OBJ display-list culling only in the editor; it does
not change exported group flags. **Selected room only** hides neighboring room meshes and is
on by default. **Native display-list preview** uses the room display lists decoded from the
custom ROM loader, while leaving the captured OBJ available for face selection and tile work.
Both preview paths are in the toolbar; neither control changes exported group flags or certifies
collision/gameplay fidelity.
The catalog currently samples main room and scene headers. The **Connections** palette contains
scene transition presets. Drag one into the viewport, choose two room sides and a facing angle,
then place it. Required banks are added to both rooms; placement supports undo/redo. These are
transition records, not automatically cut doorways. Existing openings and gameplay parameters
still need checking. Scene validation remains advisory.

Settings, recent files and local libraries are separate from the modeling application.
Extracted assets and ROMs must remain local. Scene XML retains the SharpOcarina assembly
namespace on disk; Archarina64 maps that namespace when loading. This was tested with
a scene containing a transition, not every supported scene feature.

## Section Tiles

1. Select **Visual faces** below the main toolbar. Click individual triangles to toggle them.
2. Select **Collision faces** and choose the collision triangles explicitly. Collision is shown
   as a cyan wire overlay; it belongs to the entire scene, not just the current room.
3. Click **Save tile...**, enter a name, pivot and grid. Both selections must be nonempty.
4. Select **Place**, open the **Tiles** palette, choose a quarter-turn angle, and drag the tile
   into the viewport. Grid snap uses the tile's saved grid in X/Y/Z. Copies are independent.
5. **Source surfaces** preserves collision settings in the source scene. For another scene,
   explicitly choose a destination **Surface N**; this replaces all selected tile surface types.
   Rotating conveyor surfaces also requires an explicit destination-surface mapping.
6. Undo/redo includes both meshes. **Save scene** stores a reference to a private immutable
   geometry snapshot; **Open saved scene** restores it. Keep the LocalAppData cache with the project.
   Tile-bearing projects currently require Archarina64; the modeling fork does not restore these snapshots.

7. Enable **Magnetic snap** in the assembly row to align a tile doorway socket to a compatible
   socket in the current scene. Matching requires equal opening dimensions and opposed facing;
   the radius and quarter-turn are considered automatically. **Room door anchors** adds existing
   scene transitions as alignment guides. Placement records socket occupancy in the private
   geometry snapshot, but it does not create or rewrite a transition automatically.
8. Use **Tile sockets...** with a saved tile selected to edit pivot-relative doorway sockets. Use
   **Select box...** to select whole visual/collision triangles and report boundary-overlapping faces;
   **Select connected** grows a selection across shared geometry edges. These tools never silently
   clip triangles or infer a playable opening.
9. Use **Auto-chop entrances** to analyze the active room's native transitions. Each entrance gets
   a separate private tile with complete visual/collision faces, a pivot-relative doorway socket, and
   a Tiles-palette entry. Regions with no complete geometry are skipped and reported with warnings.
10. Use **Analyze / generate** after opening a ROM scene. Archarina64 scores decoded room geometry,
    collision, floor faces, actors, spawns, paths, cameras, cutscenes, waterboxes, doors and reachability,
    then saves a deterministic source-room generation plan under that ROM's private LocalAppData cache.
11. Use **Analyze ROM** to decode every scene indexed by the imported ROM, without replacing the active
    editing scene. It writes `rom_analysis.xml` and `rom_generated_plan.xml` under the private fingerprinted
    `procedural` cache and records per-scene decode diagnostics.

Tiles preserve positions, UVs, normals, vertex colors, materials and group render settings.
Textures are copied into a content-addressed private cache. They are not stored in the repository
or executable. Libraries are filtered by source ROM fingerprint.

First-version boundaries: scale-1 scenes without alternate setups for placement; ordinary static
visual groups only (no animation, custom display lists, billboards, LOD or multitexture); triangles
are selected whole, not clipped. No actors, doors, waterboxes or puzzle relationships are captured.
There is no post-placement tile-instance transform inspector yet; choose rotation before placement
and undo/re-place as needed. The editor does not certify that the chosen visual/collision selections
match or form a watertight playable section. In-game collision acceptance remains pending.

## Acceptance Status

## Procedural Dungeon Assembly

The core analyzer now reads native room transitions and creates entrance tile plans tagged with
the correct scene, room, transition index, bounds, and generated socket. Per-scene/room/transition
rules can override the default entrance envelope without containing ROM data. Regions select whole
visual and collision triangles and report faces crossing the envelope.

Room packages are also defined for reusable geometry, actors, spawns, object dependencies, sockets,
and typed no-code logic links. The current link graph validates endpoints and supported relationship
types; palette controls and native export compilation are still being wired.

The first procedural planner pass is now available from the sandbox. It does not mutate the ROM or
the active scene: it produces a traceable plan containing source-room mesh recipes, preserved-source
material policy, actor placements, native-transition reuse, and square-corridor requirements when a
selected room pair has no native edge. Native scene export, logic solving, and final injection remain
separate validation stages.

The ROM-wide pass now aggregates those reports across all indexed scenes, preserving scene/room identity
for cross-scene source selection. It is still an analysis and planning pass: it does not infer arbitrary
event flags, solve puzzle dependencies, synthesize new mesh topology, or inject generated output.

The logic pass now reads the existing actor property metadata from `ActorNames.xml` and records
switch/chest/collectible/path references with their source property, target field, mask-derived value,
and evidence text. Matching references produce confidence-scored cause/effect candidates such as a
pressure plate opening a door or a switch enabling an enemy spawn. These are candidates, not gameplay
certification: ambiguous shared flags are reported, and final behavior still requires in-game testing.

Generated plans now include a progression report. It identifies the start room, traverses generated
room connections, orders room/encounter steps, carries inferred cause/effect steps, and reports
unreachable rooms or copied logic candidates. A plan is marked solvable only when all generated rooms
are reachable and no progression validation errors remain.

Plans also include native-assembly preparation. Linked flag references are grouped, assigned generated
values, and applied to copied actor variables or rotation fields while preserving the source evidence.
This prevents independently reused room instances from blindly sharing original flags. Native scene
command emission, transition serialization, and ROM injection are still separate stages.

Use the sandbox **Starting items** field as a comma-separated inventory, such as `Sword, Hookshot,
Bombs`. The generator carries that profile into the plan, recognizes item names from existing actor
metadata where available, excludes rooms with unmet known requirements, and stops instead of emitting
an incomplete plan. Partial generation is available only through the programmatic opt-in setting.

Progression is inventory-aware: the solver carries starting items through the generated graph, collects
item grants from reachable rooms, and can use those earned items to unlock later rooms. Missing items
are reported only when no reachable progression state can supply them.

The progression solver also models dungeon resources. Small-key rewards become consumable inventory,
locked-room plans spend keys before granting their room rewards, and boss-key rewards unlock rooms
marked as boss-door gated. Rewards are collected once per room state, so graph backtracking cannot
duplicate a chest or key. A plan with an underfunded lock is reported as a progression softlock
instead of being certified as solvable. Door/key classification is currently conservative and
metadata-backed; emulator validation is still required for ambiguous actor behavior.

Transition metadata is also preserved as structured traversal data. Normal native transitions remain
bidirectional unless the imported actor definition explicitly describes a one-way entrance, no-return
warp, or equivalent behavior. Explicit edge requirements are carried into generated connection plans
and checked before traversal, so a required item on a one-way connection cannot be bypassed by the
solver's reverse graph assumption.

Reward extraction accepts the metadata labels used by different actor families: `Content`, `Reward`,
`Item Drop`, and `Collectible`. Requirement-labeled properties are excluded from reward extraction,
and the original dropdown label is retained so dungeon economy analysis can distinguish small keys,
boss keys, maps, compasses, equipment, and ordinary consumables without embedding ROM data in the
repository or executable.

ROM-wide analysis also writes `dungeon_patterns.xml` for the supported OoT dungeon scene range. Each
profile records scene/room ownership, native connection density, reachability, actor-role counts,
reward items, boss-room count, object-bank dependencies, gate density, geometry quality, and simple
linear-versus-branching progression metrics. This is the first data layer for learning OoT dungeon
archetypes before generating new PCG geometry.

The generated plan now carries a mesh-independent topology contract. `DungeonTopologyPlanner` classifies
learned layouts as linear, branching, or hub-loop and emits deterministic room roles such as Start,
Puzzle, Reward, Hub, and Boss plus purpose-tagged edges. The future modeling fork can consume this
contract to generate geometry without changing progression logic.

Dungeon pattern profiles also classify decoded reward ownership: small keys, boss keys, maps, compasses,
other dungeon rewards, chest counts, chest-flag groups, locked-door references, and switch-gate counts.
Each reward is recorded with its scene/room/actor owner so later generation can preserve dungeon-local
ownership rules and avoid spending a key or reward twice.

Profiles also classify semantic room roles from traversal and actor evidence: Start, Reward, Hub, Puzzle,
Combat, DeadEnd, Corridor, and Boss. They record traversal depth and degree, enemy/chest/door/switch
counts, average reward depth, and enemy pacing. These metrics are analysis inputs, not hand-authored
labels, so the generator can learn pacing patterns from the actual ROM.

Each profile now includes an ordered progression chain of depth, role, rewards, gates, enemies, and
chests. The report compares chains across dungeons with average reward spacing, gate-to-reward balance,
shared-pattern findings, and outlier diagnostics. This comparison layer is intended to teach the
generator OoT-like pacing before it chooses a new topology.

Misaligned doors have a validated square-tube corridor specification with configurable width, height,
grid, turns, and endpoints. Corridor mesh generation and confirmed transition creation remain pending.

- Completed: replacement sandbox UI, local ROM extraction, categorized actor presets, placement,
  object dependencies, grid snap and placement undo/redo; separate executable/settings.
- Verified: Debug/x64 build, new UI smoke, all boss selections, actor placement/dependencies,
  undo/redo, native scene + 12 room files, unchanged source ROM hash. Local import yields
  2,961 entries, including 117 connection presets, 386 rooms and 380 object banks.
- Verified connections: cancel without mutation, explicit room mapping, both-room dependencies,
  undo/redo and native binary room/camera/actor/facing/parameter fields.
- Implemented section tiles: explicit visual/collision face selection, private library, pivot/grid,
  independent quarter-turn placement, collision surface mapping, geometry undo/redo and snapshots.
- Verified tiles: two source-ROM placements, independent-copy unit tests, UV/render-property/color
  preservation, undo/redo, editor save/reopen, native collision polygon count and destination surface
  ID. Source ROM hash unchanged. This is not emulator or watertight-section acceptance.
- Pending: whole-room placement; automatic collision partitioning beyond entrance envelopes;
  transition creation from aligned doorway sockets and in-game door traversal;
  puzzle dependency/flag remapping; region clipping and bulk selection; overworld assembly; asset-provider
  interface; full compatibility tests and export/injection/emulator acceptance.
- Deferred as agreed: custom NPC scripting/NPC Maker integration.

The playable two-room acceptance gate has NOT passed. No ROM was injected or modified.
Native package generation has passed, but lossless geometry and emulator behavior remain unverified.
ROM-derived data lives only in `%LOCALAPPDATA%/Archarina64`. Known inherited ROM payloads
were quarantined and removed from build content; inherited Git history still contains them.
Release builds are blocked pending a complete provenance audit. See `SOURCE_AUDIT.md` before publishing.

Local housekeeping: an aborted `../Archarina64-incomplete-clone` and a redundant
`packages/packages` copy remain. Automatic approval review blocked their cleanup;
neither is used by the active checkout/build.

## Next Acceptance Gate

Verify a deliberately selected floor-and-wall section placed twice in-game, including wall/floor
collision and visual fidelity. Then add geometric connection sockets, assemble two rooms, establish
a player spawn and verify both directions of traversal. Native export alone does not pass this gate.

Run `tests/run.ps1` for the current automated checks; pass `-RomPath` for optional read-only
catalog testing and `-UiSmoke` to instantiate and close the editor for the UI smoke check.
