# Personal Library

A personal Windows desktop book tracker with offline reading history,
flexible shelves, statistics, bibliographies, and local recommendations.

Start with the [approved design](.docs/DESIGN.md) and
[implementation status](.docs/IMPLEMENTATION.md).

The offline tracker includes Calibre and Goodreads imports and optional online
book lookup. Development uses synthetic fixtures. Advanced statistics,
recommendations, OCR, and Cover Flow follow in later stages.

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

Switch between List and Covers in My Books. The search and filters apply to both
views. The Home sidebar also shows cover thumbnails for queued and recent books.

In **Book & editions**, you can enter an ISBN with or without hyphens. For
**Download metadata**, `978-1433-58475-6` and `9781433584756` are equivalent.
The lookup accepts valid ISBN-10 and ISBN-13 values and ignores spaces and hyphens.
Each edition also has an optional **Publisher** field. You can enter it manually
or apply it with edition details from **Download metadata**.

In **Settings → Calibre library**, choose the folder containing `metadata.db`,
then use **Refresh / preview** and **Apply import**. Applying saves the folder for
future refreshes. The book editor's **Calibre** tab shows the source details and
links to ebook files in the original library. The app copies cover images into
the profile and leaves new books Unclassified.

In **Settings → Goodreads import**, choose the Goodreads library CSV. Review
the proposed updates and resolve uncertain matches before selecting **Back up
and import**. Goodreads takes priority for reading status, rating, review, and
its shelves. The app keeps Calibre links, ownership, covers, private notes, and
bibliographies. Books on `read-b4-goodreads` become undated completions.
If you also have a Goodreads account-data ZIP, select **Use reading dates from
Goodreads ZIP** before choosing the CSV. The app uses its reading sessions to
fill start dates and recover dated rereads for titles unique in the CSV.

In **My Books**, enable **Bulk edit**, select individual books or **Select all
results**, choose a status or enter a shelf name, then apply. Changing filters
keeps only the selected books still in the results. Marking a book Read in bulk
leaves the date unknown when the book has no prior completion.

Use **Remove selected** in bulk edit to review removal from Personal Library
or both libraries. The combined action uses Calibre's trash and includes every
format in each selected Calibre entry. Close Calibre before confirming that
action. A personal-only removal skips the record on later Calibre refreshes.

The book editor's **Bibliography** tab can extract a dedicated bibliography
from its linked EPUB. **Browse EPUB citations** opens search, sections, the
original citation text, and possible matches in My Books. Your Markdown notes
are saved separately.

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
