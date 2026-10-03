# Project Boundary

Archarina64 is the editor product. It is separate from any ROM decompilation,
game source tree, ROM backup, or extracted asset workspace.

The editor can consume user-supplied ROM data and local extracted assets, but
those inputs are runtime data and are not part of this repository. Keeping that
boundary explicit prevents the editor fork from being confused with an OoT
decompilation and keeps the public source reviewable.

## Source ownership

- `src/Archarina64` owns the merged SharpOcarina-derived modeler and the
  Archarina64 dungeon-builder application.
- `src/SharedEditor` owns neutral document, asset, handoff, and playtest
  contracts shared by the editor surfaces.
- The existing SharpOcarina namespaces and GPLv3 notice are retained because
  the application is an extension of that codebase.

## Runtime inputs

Users supply their own legally obtained ROM and extracted assets. Runtime
workspaces should be kept outside the repository, for example under a user
profile or a separate local project directory. The repository's ignore rules
also exclude common ROM, scene, room, object, and local-cache extensions.

## Current alpha boundary

The source is suitable for editor development and review. N64 display-list and
material preservation during scene/modeler round trips remains active work, as
does final ROM injection and emulator gameplay validation. This repository does
not claim those workflows are finished merely because the Windows project
builds.

