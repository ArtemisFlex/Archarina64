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
- Pending: run a fresh restore/build from this new repository, then publish it
  as the canonical public editor repository. The older decomp-rooted public
  repository should not be used as the editor's home.

