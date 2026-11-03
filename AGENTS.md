# Project instructions

Read `.docs/DESIGN.md` and `.docs/IMPLEMENTATION.md` before changing behavior.
The user approved beginning implementation on November 3, 2026, including the
proposed defaults. Work through the documented stages using synthetic data.

Use `codex/` task branches. Preserve unrelated edits. Keep project documentation
in `.docs/`, with README, AGENTS, and third-party notices/licenses as exceptions.
Never merge or import personal Goodreads/Calibre data without explicit instructions.

Use C#/.NET 10, WPF, Core/Infrastructure/App projects, and executable Checks.
Run `scripts/check.ps1` and `scripts/build.ps1` for substantive behavior changes.
Publish with `scripts/build.ps1 -Publish` when packaging or launch behavior changes.
Use the ignored `.tools/` and `.local/` directories for build caches and test data.
Never write test data to the normal profile. For UI verification set
`PERSONAL_LIBRARY_DATA_DIR` to an isolated folder and verify the TEST PROFILE banner.

Keep large dependencies, models, profiles, imports, backups, and build output out
of Git. Personal reading data remains separate from replaceable catalog resources.
Record actual completed features, checks, and material gaps in `.docs/IMPLEMENTATION.md`.
