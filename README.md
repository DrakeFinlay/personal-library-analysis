# Personal Library

A personal Windows desktop book tracker with offline reading history,
flexible shelves, statistics, bibliographies, and local recommendations.

Start with the [approved design](.docs/DESIGN.md) and
[implementation status](.docs/IMPLEMENTATION.md).

Stage A provides a runnable offline reading tracker. Development uses synthetic
data before a separately authorized personal Goodreads/Calibre import. Online book
lookup/importers, advanced statistics, recommendations, OCR, and visual library
views follow in later stages.

Stack: C#/.NET 10, WPF, and SQLite, consistent with the neighboring projects.
Personal profiles, downloads, models, backups, and build output stay outside Git.

## Build and launch

Requires Windows and a .NET 10 SDK. Scripts use an installed SDK, an optional
`.tools/dotnet/dotnet.exe`, or the existing sibling Anki Stats SDK. Set
`PERSONAL_LIBRARY_DOTNET` to choose an explicit SDK executable.

```powershell
.\scripts\check.ps1
.\scripts\build.ps1 -Publish
.\scripts\create-shortcut.ps1
.\scripts\launch.ps1 -Demo
```

`Personal Library - Demo.lnk` opens a persistent synthetic library under `.local/demo`.
The normal `Personal Library.lnk` opens the independent profile under
`%LOCALAPPDATA%/PersonalLibrary`, initially empty. Shortcuts rebuild current source
before launch and write build failures to `.tools/launch.log`. Close running copies
before publishing a replacement build. The published
`artifacts/PersonalLibrary/PersonalLibrary.exe` launches directly without an SDK.
Keep the accompanying binaries, notices, and license files together.

## Using the first build

Add a book, enter its edition/page count/ownership, and save. Open Reading & history
to start it, record page/percentage progress, finish, reread, or mark it reference-only.
Use a blank date for undated reading history. Set shelves, Read next, ratings,
reviews, and private notes in the editor. Paste references in Bibliography to save
a Markdown document in the profile. Reading actions save immediately; other edits
save with Save & close. History dates also save with Save & close.

Settings provides light/dark modes, the annual goal, profile/backup folder access,
manual backup, and validated restore preview. Daily backups happen after changes;
14 daily and 4 weekly archives are retained. Manual/pre-restore archives are kept.
Backup errors are shown without discarding saved changes. Each backup includes the
database, covers, and bibliography files/revisions.

For isolated verification, set `PERSONAL_LIBRARY_DATA_DIR` to a folder under
`.local/` and confirm the TEST PROFILE banner. `--demo` seeds only an empty isolated
profile. Normal shortcut launch clears the override. Do not use personal imports as
test fixtures or commit them to Git.
