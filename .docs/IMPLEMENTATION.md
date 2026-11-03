# Implementation status

Implementation authorized November 3, 2026. The user accepted the design and its
proposed defaults. Current delivery target: Stage A, with synthetic data only.

## Implemented in the first build

- C#/.NET 10 WPF app with Core, Infrastructure, App, and executable Checks projects.
- Reading-focused Home, annual book goal, ordered reading/read-next lists, recent
  to-read additions, basic searchable library with status/shelf/excluded filters.
- Work identity, editable editions, selected edition, per-edition ownership/pages/
  cover/language/ISBN, whole-star ratings, review/private notes, flat shelves.
- Independent reading/rereading records, page/percentage progress, dated/undated
  completion, partial/DNF outcomes, reference-only status, and editable history dates.
- Local SQLite schema version 1. Book aggregates are stored as versionable JSON
  documents keyed by work ID; settings are stored in a separate SQLite table.
  Relational catalog/search/citation tables can follow as integrations require.
- Markdown bibliography files, initial-paste retention, revisions, atomic replacement,
  and hash checks protecting external edits. No parser/model required to save text.
- Daily/weekly checksummed ZIP backups, consistent SQLite snapshot, validation/
  preview, pre-restore backup, and staged replacement. Profile lock prevents a
  second app instance from writing the same profile.
- Reproducible random synthetic profile (18 fictional works), distinct test banner,
  isolated data override, build/publish/check/launch scripts, and shortcut creation.

## Validation

The final check run passed 33 focused checks, including reread/edition identity,
annual attribution, progress bounds, external Markdown conflicts, backup round-trip,
corrupt/unsafe backup rejection, second-writer locking, and 14-daily/4-weekly
retention. A self-contained Windows x64 Release package built successfully.

Windows UI verification used `.local/ui-preview` only. Confirmed Home renders the
synthetic reading cards; editing/saving page progress updates Home and survives
relaunch. Added a new fictional book and started its reading through the UI.
Verified light/dark Home and Settings, dark library status filtering, and editor
controls. Corrected primary-button contrast, derived-window themes, dark selected
tabs/dropdowns, and edition display labels. No personal sources were read or imported.

The published build and package license notices are local under ignored
`artifacts/PersonalLibrary`. Repository-root normal/demo shortcuts were generated.
A Windows GitHub Actions build/check workflow is included but has not run remotely.

## Deliberate limitations and follow-ups

- Stage B importers/search providers, lifetime/chart statistics, quotation/vocabulary
  capture, and structured bibliography/citation matching are not implemented yet.
- Recommendations, OCR/extraction, automatic external Markdown watching, visual
  library modes, citation graph, and Anki integration remain later stages.
- Initial backups contain complete profile copies. The retention policy keeps 14
  daily and 4 weekly archives; manual/pre-restore archives are retained. Large-source
  deduplication is deferred until document import makes it useful.
- No online requests are made by the app. Dependencies are restored during build;
  the published self-contained app works without a development SDK or network.
- No 10,000-book benchmark, startup timing, high-DPI sweep, or recommendation-quality
  evaluation has been performed. The design's performance targets remain unmeasured.
- Contributor names use an editable display string initially. Structured ordered
  contributor roles and full date precision/provenance arrive with catalog imports.
- Settings live inside `personal.sqlite` initially, not the proposed `settings.json`.
  There is no reference catalog database until Stage B needs one.
- Publication year fields accept signed years; approximate/year-only reading dates
  are not yet offered. Reading dates are exact or unknown in this initial editor.

The normal profile stays under `%LOCALAPPDATA%/PersonalLibrary`. Synthetic/test
profiles stay under ignored `.local/`; they are never copied into the normal profile.
