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

## Standing commit and documentation rules

For every requested change, validate and review the work, then commit the task's
changes before reporting completion; no separate commit request is needed. Use
focused, descriptive commits for each completed task, and separate independently
meaningful changes in larger tasks. Include related documentation in the same
commit. Stage only this task's files or hunks and preserve unrelated work. Follow
this repository's branch workflow. Commit locally; pushing or merging requires
the user's instruction. If committing is blocked, explain why and what remains.
Read-only tasks do not require a commit.

Assess documentation impact on every change. Update affected README sections,
usage guides, setup/configuration instructions, and implementation/status notes.
Add documentation for new features or workflows that lack it. Write practical
steps for a user who does not code: where to find the feature, how to use it, and
what to expect. Keep links and examples accurate and follow this repository's
documentation location conventions. If no documentation update is needed, explain
why in the completion report.

The completion report must describe what changed and how to use it, documentation
updated (or why none was needed), validation results, commit hash(es), branch,
and any remaining limitations. The shared policy is maintained at
`D:/Drake/git-projects/AGENTS.md`; read it when available. This section remains
applicable in separate checkouts and worktrees where that file is unavailable.
