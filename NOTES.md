# 2026-10-03 - Separate editor repository

- Requested: replace the misleading OoT decomp-rooted public repository with a
  clean repository that clearly presents the SharpOcarina-derived modeler and
  Archarina64 dungeon builder as the product.
- Completed: exported only the tracked `Archarina64` and `SharedEditor` editor
  projects into this standalone repository. No OoT decomp tree, ROM, extracted
  game assets, or legacy payload history was copied.
- Completed: added a product README and project-boundary documentation that
  identify the SharpOcarina lineage, the dungeon-builder scope, the user-ROM
  boundary, and the active alpha limitations.
- Completed: restored dependencies and built the standalone `Archarina64`
  solution from this repository with `PublicAlpha=true`; the build produced
  `src/Archarina64/bin/StandaloneAlpha/Archarina64.exe` with no errors.
- Pending: publish this repository as the canonical public editor repository.
  The older decomp-rooted public repository should not be used as the editor's
  home.
