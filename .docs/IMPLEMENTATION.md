# Implementation status

Implementation authorized November 3, 2026. The user accepted the design and its
proposed defaults. Stage A is runnable, and the Calibre portion of Stage B is
available with the first authorized real-profile import.

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

## Stage A browsing update

- Read next and Recently added on Home show cover thumbnails. Read next still
  supports reordering with the arrow buttons beside each book.
- My Books offers List and Covers. Both share the search and filters and open
  the same book editor. The view choice lasts for the current app session.
- All views use the selected edition's local cover, with a placeholder for books
  without a usable image.
- The 33 checks and Release build passed. In an isolated Windows demo, verified
  sidebar thumbnails, opening and reordering books, and keeping a search when
  switching between List and Covers. The active app held the standard publish
  folder open, so the updated preview was packaged under `artifacts/CoverPreview`.

## Calibre connection and bulk editing

- Settings saves the Calibre folder after applying a preview. The importer reads
  the local SQLite catalog in a read-only transaction, copies covers into the
  profile, and links existing ebook files. The Calibre tab displays source series,
  publisher, identifiers, tags, and comments. Calibre tags stay separate from
  personal shelves. Missing page counts remain blank.
- Refresh matches Calibre's library and book UUIDs, previews source changes, and
  retains absent source records. Local changes to imported fields and reading
  history survive refresh. Applying saves records together after a pre-import backup.
- My Books supports bulk status and shelf changes in List and Covers. Changing
  filters drops selections outside the results. New Calibre records start as
  Unclassified, with explicit manga or comic records available under exclusions.
- The first authorized import added 376 records to the normal profile on November
  9, 2026, including 364 local cover copies and 376 ebook links. One record was
  excluded by its Manga tag. A repeat preview reported all 376 unchanged, and the
  source database hash matched before and after the operation.
- Verified preview and apply and bulk Read in an isolated Windows profile with
  synthetic Calibre data. The synthetic checks cover reconciliation, metadata
  precedence, file path bounds, backup creation, and undated classification.

## Stage A validation

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

## Library cleanup and EPUB bibliography prototype

- My Books uses visible status toggles and a separate shelf dropdown. Covers
  toggle selection during bulk edit and open details during regular browsing.
- Bulk removal can remove entries from Personal Library alone or also send their
  entire Calibre book folders to Calibre's trash. Combined removal requires a
  selection from one Calibre library. The app previews and rechecks the selected
  source IDs, folders, and formats and backs up the profile before removing records.
  Personal Library-only removal suppresses future imports of the same Calibre ID.
- The EPUB prototype browses extracted citations by section and text search.
  Possible local matches use normalized title and author evidence. Editable
  Markdown is stored separately. The Dominion EPUB yielded 411 bibliography
  entries across General, Antiquity, Christendom, and Modernitas. All 411 were
  saved on Dominion in the normal profile. This prototype extracts dedicated
  bibliography sections. Endnotes and inferred citation relationships remain
  separate.
- All 47 checks passed. The installed Calibre CLI moved both formats of a
  disposable book to trash, and the app removed its local record. A stale
  format preview was rejected. The isolated Windows build verified cover
  selection, status filtering, the removal warning, and citation browsing. The
  updated Windows app was published to the regular launcher folder.

## Online lookup, sorting, and bibliography preview

- Download metadata in Book & editions searches Open Library by ISBN, title, or
  author. ISBN lookup removes spaces and hyphens before validating ISBN-10 and
  ISBN-13 values. Results show work and available edition details, with a preview
  of the selected cover. Edition fields are preselected for an exact ISBN hit.
  Title and author results let the reader opt in to the displayed edition fields.
  A chosen cover is copied locally when the book is saved. Existing covers are
  kept by default.
- My Books sorts List and Covers by title or displayed author in either direction.
  The current search stays in place for the session when closing a book refreshes
  the library.
  Starting a reading records no progress event. The update field starts at zero
  and shows the last recorded value and unit after the first update.
- The Bibliography tab shows scrollable Markdown generated from the saved EPUB
  citations alongside the reader's separate editable Markdown. The optional
  citation browser displays authors next to titles and looks up individual
  citations online. Those lookup results are suggestions.
- The first lookup adapter uses the Open Library
  [Search API](https://openlibrary.org/dev/docs/api/search),
  [Books API](https://openlibrary.org/dev/docs/api/books), and
  [Covers API](https://openlibrary.org/dev/docs/api/covers). Requests run when
  the reader searches or selects a result in the online lookup dialog. Applied
  metadata, provider IDs, and copied covers are stored with the saved book.
- All 53 checks passed. The Windows test profile showed the generated Markdown
  beside the existing notes, a citation author in the browser, and a reading
  started with no progress logged. A live ISBN search in the citation dialog
  returned edition details and a cover. Live title and edition requests were
  also checked against Open Library.

## Goodreads CSV import

- Settings opens a Goodreads CSV preview with additions, updates, unchanged
  entries, exclusions, pre-Goodreads undated mappings, and skipped rows.
  Existing Goodreads IDs, equivalent ISBNs, and unique exact title-and-author
  matches can reconcile automatically. Weaker matches and potential duplicates
  are held for review. A different ISBN on a matched work keeps both editions.
- Goodreads takes priority for status, rating, review, and its shelves. Local
  notes, bibliographies, Calibre files, covers, ownership, and separate reading
  records survive. The legacy shelf maps to undated history and stays in the
  stored source fields. Manga and comics identified by explicit shelves remain
  excluded. A Read Count alone cannot reconstruct individual rereads.
- Calibre refresh updates its own source snapshot while keeping the Goodreads
  title, author, and ISBN-matched edition fields supplied by the export.
- Applying a current preview makes a pre-import backup, then saves updates and
  the last import report in one transaction. The app keeps source columns and
  skipped rows locally. An unchanged export can be previewed repeatedly without
  creating books, editions, or history entries again.
- All 66 checks passed, and the Release build was published. The Windows test
  profile previewed and imported five synthetic records, skipped two duplicate
  IDs, and displayed the completed import report.
- Settings can also use reading sessions from a Goodreads account-data ZIP
  alongside the library CSV. The ZIP supplies dates and rereads while the CSV
  supplies the book identities and shelves. Archive sessions are matched by
  exact title within the CSV. Duplicate CSV titles are flagged and their
  archive sessions skipped. Legacy completions stay undated, and partially
  read or abandoned books keep their Goodreads status. The Japanese to-read
  shelf keeps its name and also maps to To read.
- The authorized personal import ran on November 16, 2026 with pre-import
  backups. A repeat preview found no additional automatic changes and kept
  uncertain matches available for review. The 70 checks and Windows Release
  build passed.

## Library cleanup and lookup-first entry

- **Add a book** opens search before the editor. Local search lists My Books
  first and then results from the installed reference catalog. Search Open
  Library offers the online database. A result can reopen an existing book
  or prefill title, author, edition information, and an available selected
  cover. Manual entry is available from the search window.
- **My Books → Review duplicates** flags shared ISBNs, Open Library identities,
  and normalized title-and-author pairs. Bulk edit can also review two
  manually selected books. A confirmed merge retains both editions and their
  reading records, combines shelves and notes, and keeps a Calibre link and a
  Goodreads link when both are present. It creates a pre-merge backup.
  Pairs with two links from the same provider, two active readings, conflicting
  Open Library work IDs, or two sets of bibliography files require separate
  review. Keep the record with the bibliography when only one has one.
- Book covers fall back to another locally covered edition if the selected
  edition has no usable image. Calibre refresh can recopy a missing cover
  even when its metadata is unchanged. Use **Missing local cover** to find
  books without a cover file and **Use cover only** in metadata lookup to add
  art without changing the book's title or edition details.
- **Settings → Offline Open Library catalog** builds a separate SQLite index
  from the Open Library Authors and Editions dumps. It searches locally by
  title, author, and ISBN. **Preview ISBN matches** offers a backed-up update
  of missing edition IDs and page counts where the ISBN is unambiguous and
  title and author agree. Book cover images are fetched individually on
  request. Catalog builds are cancellable and publish the index after it
  finishes. [Open Library data dumps](https://openlibrary.org/developers/dumps)
- All 78 synthetic checks passed. The Windows test profile verified lookup
  from the offline index, prefilled metadata, saving the book, and reopening
  the existing record from a repeat search. The self-contained Windows
  Release package was published. The full production-sized dump remains
  untested and uninstalled. Personal cleanup decisions are pending review.

## Open Library search and Goodreads catalog cleanup

- A full title that yields zero Open Library results gets one retry with the
  subtitle removed. The lookup dialog names the search source and shows when
  it retried the main title. Live Open Library returned zero for the supplied
  full title, two results for “Millennial Nuns,” and an edition for the
  paperback ISBN 9781982158033.
- Goodreads rows explicitly identified as manga or comics are skipped and
  recorded in the import preview and report. Earlier imports with the same
  evidence are surfaced only as excluded records. Rows without explicit
  evidence can still require manual classification.
- Duplicate review ignores excluded records and pairs with conflicting
  volume numbers. It recognizes trailing Goodreads series annotations such
  as `(1Q84, #1-3)` and displays covers for visual comparison.
- **My Books → Find Goodreads covers** lists active Goodreads books without
  a local cover. It searches one chosen book at a time and saves the reader's
  selected art locally. For an ISBN match without a metadata cover URL, the
  app can try the Open Library Covers API directly by ISBN. Goodreads import
  does not supply cover images, and this workflow does not fetch covers in bulk.
- All 82 synthetic checks passed. The Windows Release build and publish succeeded.
  The isolated Windows profile showed the Goodreads cover queue and kept explicitly
  tagged manga outside it and the normal library.

## Edition publishers

- **Book & editions** has an optional publisher field for each edition.
  Applying edition details in metadata lookup includes the publisher when
  Open Library supplies it.
- Calibre and Goodreads can supply publishers. Existing linked editions can
  recover publishers from source metadata already saved in the profile. The
  offline catalog's ISBN match preview can fill missing publishers. Imports
  and bulk catalog matches preserve entered or explicitly cleared values.
- All 84 synthetic checks and the Windows Release build passed. In the
  isolated Windows test profile, a manually entered publisher was saved and
  displayed again after reopening the edition.

## Currently Reading sections

- Home includes a **Daily Reads** section by default. Existing active books
  start under **Other books**. Use **Move to section** on a book card to change
  its group, and use **Move up** or **Move down** to order books within that group.
- **Manage sections** creates, renames, reorders, and removes sections. Removing
  a section returns its books to **Other books**. Assignments persist across
  rereads and stay with the active reading when duplicate books are merged.
- All 93 synthetic checks passed. The Release build and self-contained Windows
  publish succeeded. In the isolated Windows test profile, verified that moving
  a book into Daily Reads preserved its displayed progress and saving a custom
  section added it to Home.

## Deliberate limitations and follow-ups

- Stage B broader catalog coverage, lifetime/chart statistics,
  quotation/vocabulary capture, and accepted citation relationships are
  still planned. Calibre custom column mapping remains deferred.
- Recommendations, OCR/extraction, automatic external Markdown watching, visual
  browsing modes beyond the list and cover grid, citation graph, and Anki
  integration remain later stages.
- Initial backups contain complete profile copies. The retention policy keeps 14
  daily and 4 weekly archives; manual/pre-restore archives are retained. Large-source
  deduplication is deferred until document import makes it useful.
- Open Library online lookup and downloading new covers require an internet
  connection. The published app and installed metadata index work offline.
- No 10,000-book benchmark, startup timing, high-DPI sweep, or recommendation-quality
  evaluation has been performed. The design's performance targets remain unmeasured.
- Contributor names use an editable display string initially. Structured ordered
  contributor roles and full date precision/provenance arrive with catalog imports.
- Settings live inside `personal.sqlite` initially. The optional Open Library
  index lives under `resources/open-library.sqlite` outside profile backups.
- Publication year fields accept signed years; approximate/year-only reading dates
  are not yet offered. Reading dates are exact or unknown in this initial editor.

The normal profile stays under `%LOCALAPPDATA%/PersonalLibrary`. Synthetic/test
profiles stay under ignored `.local/`; they are never copied into the normal profile.
