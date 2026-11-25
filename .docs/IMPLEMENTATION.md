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

## EPUB bibliography batch

- Reviewed 229 linked EPUBs and saved 34 new bibliographies and further-reading
  lists with 8,157 entries. The Bibliographies page now includes these 34 books
  and Dominion. The batch verifies the source EPUB hashes, validates a profile
  backup before each write batch, and checks that existing bibliographies and
  editable notes are preserved.
- The reader recognizes styled bibliography headings and reference entries,
  bounds extraction to the relevant sections, and resolves common named HTML
  entities locally. Nonstandard sections were reviewed against their EPUB
  layouts. The image-only bibliography in The Life and Death of Yukio Mishima
  was transcribed with offline OCR and checked against the seven source images.
  Automated extraction handles text sections. Chapter endnotes without a
  dedicated bibliography remain separate.
- All 115 executable checks passed. The Release build and Windows publish
  succeeded.

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
  exact title within the CSV. When titles repeat, a unique matching finish date
  can identify the correct row. Ambiguous sessions are flagged and skipped.
  Legacy completions stay undated, and partially
  read or abandoned books keep their Goodreads status. The Japanese to-read
  shelf keeps its name and also maps to To read.
- The authorized personal import ran on November 16, 2026 with pre-import
  backups. A repeat preview found no additional automatic changes and kept
  uncertain matches available for review. The 70 checks and Windows Release
  build passed.

### Reread recovery

- Reconciled all nine repeated titles in the account archive. Restored missing
  readings for seven titles and verified two complete histories already stored.
  Consolidated the two local Frankenstein records and matched both dated readings
  to their Goodreads editions while preserving the Calibre edition. Removed
  the contradictory undated Odyssey completion from the To read record.
  Verified a profile backup before writing and rechecked the saved history.
- The importer no longer creates a completion on a To read or currently reading
  record from Read Count alone. Distinct archive sessions can preserve unknown
  dates and year-only finishes for completed books. Year-only finishes count
  in the known year and are identified separately outside the exact-date
  scatterplot. The reading editor accepts a year-only finish on a completion.
- Verified that the saved Frankenstein readings produce both publication
  scatterplot points and a repeat recovery finds no remaining changes. All
  133 executable checks passed. The Release build and Windows publish succeeded.

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
- Double-clicking a Currently Reading card opens that book's editor. In the
  isolated Windows test profile, verified opening by title and cover and using
  the Move down button. All 93 synthetic checks and the Release build passed.
- Currently Reading covers are 25% wider and taller (125 × 185). Verified
  the larger covers and visible card controls in the isolated Windows test
  profile. All 93 synthetic checks and the Release build passed.

## Shelf autocomplete

- The book editor suggests existing shelf names as you type. Matching ignores
  case and shows names that start with the entered text first. Click a match or
  select it with the arrow keys and Enter. Each comma-separated entry can be
  completed independently, and the field still accepts new shelf names.
- All 93 synthetic checks and the Release build passed. In the isolated Windows
  test profile, `chr` suggested `Christian`, and `HIS` matched `history`.
  Keyboard and mouse selection preserved the other shelves, and the saved list
  persisted after reopening the book.

## Goodreads rating recovery and first recommendations

- **Settings → Recover Goodreads ratings** previews a ratings-only update using
  the Goodreads library CSV and the account-data ZIP. For archive ratings it
  requires a unique title in each source and a unique existing Goodreads ID
  link. It shows unmatched titles, preserves existing ratings, and backs up the
  profile before saving. A valid CSV rating continues to take precedence on a
  regular Goodreads import. Blank and zero-star CSV values preserve saved stars.
- The authorized normal-profile recovery added 489 ratings. The library CSV
  supplied zero positive ratings. The ZIP supplied 508 whole-star ratings, of
  which 19 had no unique linked book. Verified a pre-import backup, preservation
  of other book fields, and zero additional changes on a repeat preview. My Books
  displays saved ratings in List and Covers.
- The first **Recommendations** view ranked unread To read books from completed books' ratings,
  shared authors, personal shelves, and Calibre tags. It puts more weight on
  authors, reduces the influence of sparse evidence, uses up to three signals,
  and shows the reasons for each positive match. Low ratings can lower a match.
  Excluded books contribute neither candidates nor evidence.
- All 102 synthetic checks, the Release build, and the self-contained Windows
  publish passed. The isolated Windows profile showed ranked books, explanations,
  the ownership filter, saved stars in My Books, and unmatched titles in the
  ratings preview. The initial local ranking
  found positive evidence for 81 of 197 candidates in the normal library.

## Recommendations overview

- **Top picks from To read** shows up to five cover tiles ranked by the existing
  rating-based score. **Owned and unread** shows up to five picks based on edition
  holdings and also considers unclassified books. It prefers picks outside the
  first row. Each tile opens the book editor and shows its ranking evidence on
  hover. Per-shelf recommendations and offline catalog discovery remain planned.
- The normal library returned five distinct picks per row. The isolated Windows
  profile rendered both cover rows and opened the selected book. All 104 synthetic
  checks and the Release build and publish passed.

## Library view and recommendation feedback

- **My Books** keeps its search, filters, sort order, selection, and scroll
  position while opening or closing a book. **Refresh library** reloads the saved
  records and keeps the current view. Reopening a book reads its latest saved
  details even before refreshing the library results.
- **Merge two books** opens two independent searches seeded from the current
  library query. A selected pair can use the existing merge review and backup
  workflow even when automatic duplicate detection never suggested the pair.
- Recommendation scoring gives less weight to sparse author evidence. Each row
  shows at most one title per known author. The same profile that previously
  showed six Murakami books returned two across the ten picks after the change.
- **Not now** pauses recommendations for a title for one year. **Not interested**
  removes it from To read and Read next where applicable and suppresses future
  recommendations while keeping the book and holdings. Goodreads refresh keeps
  the local preference. Deliberately adding the title to To read clears dismissal.
- The isolated Windows profile verified saved editor closure and manual refresh
  with an active query, status filter, and scrolled results. It also verified both
  recommendation actions, persistence after relaunch, and the manual picker
  through the merge review. All 109 synthetic checks and the Release build and
  publish passed.

## Expanded recommendation sections

- **Top picks from To read** and **Owned and unread** now show up to ten picks
  each, arranged five covers per row. The author limit applies across each full
  section. The isolated Windows profile showed ten picks in each section. All
  109 synthetic checks, the Release build, and the Windows publish passed.

## Responsive recommendations and Statistics

- Recommendations shows ten covers across when the usable width reaches 1,800
  device-independent pixels and five across in smaller windows. Verified both
  layouts in the isolated Windows profile, including the maximized 3,440-pixel
  display.
- Statistics shows lifetime pages, books completed per year, pages per year, and
  original publication year versus completion date. Page totals use the edition
  recorded for each completed reading. Annual charts count rereads separately
  and show missing page counts. Lifetime pages include undated completions with
  known page counts. The scatterplot discloses omissions for unavailable dates
  and original publication years and shows solid, unnumbered dots at the saved
  coordinates.
- Selecting a year shows the contributing reading records. Selecting a dot
  opens its reading in the editor's Reading & history tab, or lists the readings
  when several share that exact coordinate. Verified the dots and both click
  paths in the isolated profile. All 114 synthetic checks, the Release build,
  and the Windows publish passed.

## Full-screen publication-year chart

- The chart uses denser publication-year grid lines and a labeled BCE/CE
  divider. Completion dates show annual and half-year grid lines when space
  permits.
- Double-click the chart or select Full screen to open the borderless view.
  The mouse wheel zooms the publication-year axis around the pointer. The
  completion-date range, grid, and horizontal positions stay fixed. Reset
  zoom restores the full publication range. A single click still opens the
  readings at a dot.
- In the isolated Windows profile, verified that the full-screen chart fills
  the 3,440 × 1,440 display, wheel zoom moves only the publication-year axis,
  and returning to Statistics keeps the original scroll position. All 126
  checks passed, and the Release build and Windows publish succeeded.

## Reading highlights

- Statistics lists up to five most-read author credits by completed readings,
  breaking ties alphabetically. Favorite authors are ranked by average stars
  and require at least 4.5 stars across two or more distinct rated, completed
  books. Rereads count toward completed readings and each work contributes one
  current rating. Author grouping uses the saved credit with case and whitespace
  normalized.
- The oldest book uses the original publication year, including BCE years.
  The longest and shortest books use the known page counts of the editions
  actually read. Undated completions are eligible. Each book opens its reading
  record.
- All 129 checks passed. Verified the five highlights and the reading link
  with the isolated Windows test profile. The Release build and regular
  Windows publish succeeded.
- The top-five ranking and minimum 4.5-star average were verified in the
  executable checks. All 130 checks passed.

## Bibliography reader, image OCR, and cited recommendations

- Bibliographies opens a dedicated reader from a title-and-author list. The
  list keeps its scroll position when a reader closes and uses two columns
  below 1,450 available device-independent pixels and three at that width or
  above.
- Add Bibliography lets the reader choose a source in My Books, paste text,
  or scan one or more photos with Windows OCR using the installed profile
  language. Scanned text is editable before save. The profile retains the
  original images by SHA-256 and the existing Markdown revision history. EPUB
  citations and editable text remain independently accessible.
- Cited by books you enjoyed shows up to ten works cited by books rated four
  or five stars with a completed reading. It uses structured EPUB citations
  and recognizable book references in pasted or scanned text. Picks show their
  sources, can be looked up or added through the regular add-book flow, and
  support Not now and Not interested. Ambiguous or unrecognized plain text
  stays available in the reader.
- In an isolated Windows profile, verified both column counts, direct reading
  and return to a scrolled list, navigation from a recommendation to its
  citation, and scanning and saving a synthetic page image. All 118
  executable checks passed. The Release build and Windows publish succeeded.

## Online bibliography lookup

- Find Bibliography Online searches public Internet Archive editions by title
  and author or ISBN. The reader chooses an edition, previews its bibliography,
  and explicitly saves it to the source book. The workflow reads a public EPUB
  or a recognizable section of the archive's OCR text. It skips restricted
  records and private files. Results require bibliography text.
- Saved online bibliographies retain the edition and download links, content
  hash, retrieval date, and extracted reference text. They are accessible
  offline and stored alongside any local EPUB bibliography and editable
  Markdown. Identifiable book citations can contribute to recommendations.
- The scan fallback depends on visible section headings and recognizable
  citation formatting. Missing downloads, image-only scans, and PDFs without
  public scan text may have no importable bibliography.
- All 124 executable checks and the Release build passed. Verified live
  Internet Archive search, preview, save, and reopening in an isolated Windows
  profile using a public scan of Books and Their Makers During the Middle Ages.
  The Windows build was published to the regular launcher folder after closing
  the running app. The published application assembly matches the verified
  test build.

## Collection categories and comic removal

- Music book and Photography / art book are available in an individual book's
  Reading & history tab, the My Books filters, and bulk edit. They preserve
  ownership, metadata, and any previous reading records. These categories and
  Reference only stay out of reading counts and recommendation evidence.
  Goodreads refresh preserves the two new local categories.
- Reviewed and removed 191 comic and manga records from Personal Library,
  including 8 without the existing exclusion flag. Verified the pre-removal
  backup, checked that the 761 remaining book records were identical, and
  reran the candidate audit. The operation used Personal Library-only removal.
  Goodreads and Calibre refreshes honor source IDs recorded on removal.
  Calibre also skips new records with explicit comic or manga evidence.
- All 135 checks passed. Verified the category filters and bulk menu in the
  isolated Windows profile. The Release build and regular Windows publish
  succeeded.

## Library purchase tracking

- Book & editions saves optional MSRP, price purchased, vendor, and new/used
  condition per edition. MSRP and price paid support independent CAD, USD,
  JPY, and EUR currencies. A recorded zero and a blank remain distinct.
- Statistics shows recorded MSRP and spend in CAD, plus condition and vendor
  pies by edition count. It reports missing values and foreign prices awaiting
  conversion. Each total shows its average per included edition when available.
  Bank of Canada exchange rates are dated, refreshable, and cached
  in the profile for offline use. Converted spending uses the displayed snapshot.
- All 139 checks passed, including mixed currencies, offline rate reuse, vendor
  grouping, and persistence. In the isolated Windows profile, verified the CAD
  totals and both pies, then saved a vendor edit and confirmed it updated the chart.
  The Release build and regular Windows publish succeeded.

## Physical copies, sales, and lifetime purchases

- Book & editions supports multiple physical copies of the same edition. Each
  copy has its own optional purchase year, price, currency, vendor, condition,
  Sold physical copy checkbox, and optional sale proceeds with currency.
- Existing purchase fields remain readable. Recording an existing physical
  purchase as sold or adding another copy preserves the original purchase in
  copy history. The final physical sale clears physical ownership and retains
  ebook or audiobook ownership.
- Purchase Statistics has Lifetime and Current library tabs with scoped MSRP,
  spending, averages, and vendor/condition pies. Lifetime includes sold copies
  and reports proceeds separately. Edition MSRP applies to each physical copy.
- All 142 checks passed. The Release build and regular Windows publish succeeded.
  In an isolated TEST PROFILE, verified both statistics tabs, saved a sale with
  proceeds, added and saved a repurchase, and marked an existing purchase sold
  with unknown proceeds. Read back the saved records to verify prices, copy
  history, and digital ownership.

## Published covers and sourced MSRP

- My Books offers Find covers and MSRP for batch lookup, cancellation, review,
  and saving completed results. Google Books, Open Library, publisher pages,
  and BookPal supply published images or explicit print list prices.
- Each enriched edition records source URLs, matched ISBNs, alternate-cover
  status, and whether MSRP is estimated from a comparable print edition.
  The editor shows MSRP evidence and purchase statistics disclose estimated
  items. Existing values and books changed since a search are preserved.
- Created and verified a full live-profile backup before research, then made
  another verified backup before applying results. Reviewed downloaded covers,
  including interior-page replacements, and tried the save on a profile copy.
- Updated 632 works in the authorized live profile. Added covers to 464
  previously uncovered works and improved covers for 17 works with interior
  pages or blank scans. Added 454 MSRP values, including 302 labeled estimates.
  The remaining gaps are 23 works without covers and 357 editions without MSRP.
  These counts describe this enrichment run and can change with later edits.
- Verified all personal fields, edition identities, ownership, purchase and
  sale records, and previously entered MSRP against the pre-update snapshot.
  The ignored local audit retains item-level sources and the unresolved list.
- All 149 checks passed. Verified batch search, backup/save, the downloaded
  cover after manual refresh, and the saved MSRP source in a synthetic TEST
  PROFILE. The Release build and regular Windows publish succeeded.

## Store suggestions, gifts, and library loans

- Purchased from is an editable dropdown of previously saved vendor names
  across edition purchases and physical-copy history. Typing suggests matching
  names and new store names remain supported.
- Acquisition details include gift and library-loan checkboxes for existing
  entries and individual physical copies. Gifts contribute to owned retail
  value and are excluded from spending and purchase breakdowns. Library loans
  are excluded from ownership and financial totals and retain reading history.
- All 151 checks passed. In an isolated TEST PROFILE, verified that typing
  “am” suggests Amazon, choosing it saves the full name, and gift and library
  states persist. Verified that a library loan clears physical ownership and
  cannot simultaneously be marked gifted or sold.
- The Release build and regular Windows publish succeeded.

## Bulk gift and library marking

- The My Books bulk toolbar includes Keep gift/library, Gift, and Library.
  Apply to selected combines acquisition changes with optional status and shelf
  changes in the existing batch save.
- Changes target each selected book's selected edition and latest unsold copy.
  Existing edition-level purchases use the same gift and library flags as the
  configuration window. If all copies are sold, Library adds a loan record.
- Regression checks cover gift spending, library ownership, selected editions,
  preservation of reading and purchase history, and previously sold copies.
- All 154 checks passed. In an isolated TEST PROFILE, applied Gift and then
  Library to three selected books and verified that both batch updates persisted.
  The Release build and regular Windows publish succeeded.

## Library display and bulk configuration

- Owned and unread requires To read status and a currently held physical copy.
  Ebook-only, audiobook-only, unclassified, sold, and library-loan entries are
  excluded from this section. Digital To read books remain eligible for Top picks.
- My Books Display saves a requested 1–20 cover columns and independent title
  and detail font sizes. Narrow windows reduce the column count to fit.
- Bulk selection supports Shift ranges in either direction using the current
  filtered sort order. The shelf field suggests saved names and accepts new ones.
- Configure selected opens an opt-in batch editor for language, publisher,
  author, work/edition years, pages, rating, status, and additive shelves.
  Only checked fields change; edition edits target the selected edition.
- All 162 checks passed. Synthetic Windows verification covered four-column
  layout, shelf completion, range selection, and a three-book language update
  with preserved selection. Saved records confirmed that unselected books were
  unchanged.
- The Release build completed with no warnings or errors, and the regular
  Windows launcher build was published successfully.

## Home sidebar and annual title plans

- Home now shows the ten most recently saved books across reading statuses,
  newest first, excluding out-of-scope books. Reading updates, metadata edits,
  imports, batch saves, and annual plan changes can move a book into this list.
- Read next has been removed from Home and the book editor. Legacy saved queue
  fields remain readable for compatibility. The numeric reading challenge and
  target editor have moved from Home/Settings to the new top-navigation Goals page.
  Existing annual targets are preserved.
- Goals offers a separate Reads for [year] list. Search My Books by title or
  author to add a title; add missing books through + Add a book first. Show year
  switches lists. Each title occurs once per year and may belong to multiple
  years. Open a title to update its reading; Remove from year preserves the book
  and all reading history. Plans save immediately and merge with duplicate books.
- Completion is derived from an eligible completed reading in the selected year,
  including year-only dates and rereads. A title counts once in its list, while
  the separate numeric challenge still counts each completed reading. Undated
  history and special collections do not satisfy annual completion.
- All 171 synthetic checks passed, covering plan persistence, duplicate adds,
  year removal, history preservation, completion attribution, merging, and recent
  ordering across statuses. The Release build passed with no warnings or errors;
  the regular self-contained Windows publish succeeded after closing the test app.
  The isolated TEST PROFILE verified Home's revised sidebar, adding a planned
  title, switching to a separate year's empty list, and persistence after relaunch
  in the published build. No personal data was used.
- Plans use existing library titles and alphabetical order; manual ordering and
  free-text titles outside My Books are not included.

## Bibliography photo import and combined local citations

- Bibliographies offers Import bibliography photos directly. Choose a source
  book and images, scan offline, correct the draft, review citations/local matches,
  then save. Cancellation preserves successfully scanned pages in the draft;
  individual page failures are reported. Saved originals and Markdown revisions
  use the existing backup and external-edit protections.
- Cited books in My Books combines local matches across EPUB, online, and saved
  pasted/scanned text, without a rating, ownership, or reading-status requirement.
  Titles are deduplicated and retain original citation evidence and source links.
  Individual and combined bibliography views share matching rules. Wrapped OCR
  lines and lost italics can match known library titles with author evidence.
- Synthetic checks verify source deduplication, author mismatches, and combined
  persisted text/EPUB evidence. In an isolated TEST PROFILE, scanned a fictional
  page, reviewed two local matches, and saved the text and retained original PNG.
  Verified the combined view showed one existing title with two citing sources.
- Matches remain suggestions. Unsupported citation styles, OCR spelling errors,
  columns, and missing author/title evidence can prevent matching. Complete text
  is still saved. Other image formats depend on installed Windows codecs; OCR
  requires an installed Windows OCR language. See [BIBLIOGRAPHIES.md](BIBLIOGRAPHIES.md).

## Faster My Books and overlapping to-read groups

- My Books opens at To read in a compact, virtualized text Table without cover
  decoding. Status filters are on the far left; independent shelf and group
  menus narrow the range. Changing status keeps the shelf/group choice. Optional
  title/author/ISBN search is under Find a specific book and waits briefly after
  typing. Covers remains available.
- Normal database browsing materializes at most 50 matching books, after filtering
  and sorting the full library. Show all books expands the filtered range and Show
  first 50 collapses it. Status/shelf/group/search changes reset the limit. My Books
  no longer reloads and retains all book aggregates on navigation. Other screens
  still load their full calculation inputs, and Missing local cover checks files.
- Table rows open with a double-click or Enter. Title/Author headers and sort
  controls stay synchronized. Bulk selection uses Ctrl/Shift and Select all shown
  targets only the loaded rows. Existing batch status, shelf, acquisition,
  configuration, enrichment, and removal workflows remain accessible.
- Book & editions saves overlapping To-read groups with existing-name suggestions.
  Bulk Add to group preserves existing memberships. Groups are independent of
  shelves and persist through status changes, imports, and merges; edit a book's
  group field to remove its memberships. See [LIBRARY-BROWSING.md](LIBRARY-BROWSING.md).
- All 182 synthetic checks passed. On this machine, first-50 database queries
  against a 2,000-book fixture took about 19–22 ms; these are query timings, not
  application-startup or full UI benchmarks. Windows verification in the isolated
  TEST PROFILE confirmed the default 50 rows, shelf/status switching, group
  intersections, 499-row expansion, and a saved custom group on 50 selected books.
  Verified synchronized header sorting, the saved 50-book custom group, editor
  closure with filters retained, and reopened OCR text in the published build.
  The Release build passed without warnings/errors, and the regular Windows
  launcher build was published. No personal data was used.

## Per-book saved change history

- Schema 2 adds an indexed SQLite journal of book creation, updates, and removals
  with before/after snapshots. Database triggers include specialized SQL ratings
  updates as well as normal/batch saves and imports. Merges retain both histories
  under the surviving book. The journal is separate from paged library records.
- Change history at the bottom of the book editor shows dated events and field
  differences, loading 100 entries at a time. Bibliography text/structured-source
  edits and new retained images have their own detail and update the source book's
  timestamp. Unchanged saves preserve timestamps and create no artificial events.
- Existing profiles get a verified pre-history-migration backup and a Tracking
  started snapshot. Schema 1 backups remain restorable and are upgraded before
  further writes. Backups include the journal; restore returns to that timeline.
- Synthetic checks cover starts/progress/finishes/rereads, field details, no-op
  saves, failed batches, attachment edits, merge history, paging, backup/restore,
  and migration of old profiles. History before this upgrade and external file
  edits cannot be reconstructed. See [CHANGE-HISTORY.md](CHANGE-HISTORY.md).
- The Release build passed with no warnings/errors. An isolated TEST PROFILE
  displayed saved note/status/reading changes, including both completed readings
  and their reread transitions, with before/after values in the history viewer.

## Goodreads To-read order and library-addition dates

- Books have optional To-read position and Date added fields, editable in Book &
  editions and visible in the table. My Books defaults to numeric rank ascending
  from 1; Descending reverses it. Unknown ranks remain last. Date/Title/Author
  sorting remains available and applies before the 50-result limit.
- Goodreads reads the canonical to-read shelf's position from Bookshelves with
  positions and its Date Added value. Source-managed values refresh while local
  edits, including explicit blanks, survive imports and merges. Dates remain
  independent of status changes and last-edit timestamps. New local books get
  today's addition date; older undated records are not assigned invented dates.
- Settings offers a targeted preview/recovery from retained Goodreads fields. It
  verifies a backup, updates only rank/date/change time in SQLite, records the
  changes in book history, rejects stale previews, and is idempotent. Other shelf
  positions, invalid dates, and unlinked books remain unknown.
- All 204 synthetic checks passed, including numeric reversal, missing values,
  sorting before pagination, exact field preservation, stale/repeat recovery,
  source refresh, and local overrides through merges. The Release build passed
  without warnings/errors and the regular Windows launcher build was published.
  An isolated TEST PROFILE verified positions 1 upward and reversed from 65,
  displayed dates, editable fields, and the before/after history viewer.
- The authorized normal-profile recovery filled 112 canonical To-read positions
  and 482 Date Added values across 482 books. All 759 book identities and every
  unrelated saved field were preserved exactly; existing profile files were
  hash-checked as unchanged. Verified migration and recovery backups were created.
  The journal contains 759 starting snapshots and 482 recovery events. An independent
  read-back against those snapshots verified the changes; a repeat preview is empty.
- The remaining 117 To-read positions are unknown: 110 books have no Goodreads
  link and seven use the separate Japanese To-read shelf without a canonical
  to-read position. Their source fields are retained and no global ranks were
  fabricated. These counts describe this recovery and can change with later edits.

## Deliberate limitations and follow-ups

- Stage B broader catalog coverage, estimated lifetime words and book-stack
  height, quotation/vocabulary capture, and accepted citation relationships
  remain planned. Calibre custom column mapping remains deferred.
- Description and review interpretation, embeddings, and broader catalog
  discovery remain later recommendation work. Direct PDF extraction, automatic
  external Markdown watching, visual browsing modes beyond the table and cover
  grid, citation graph, and Anki integration remain later stages.
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
- Publication year fields accept signed years. Completion dates support exact
  dates, year-only finishes, or an unknown date. Start dates accept exact dates
  or an unknown date.

The normal profile stays under `%LOCALAPPDATA%/PersonalLibrary`. Synthetic/test
profiles stay under ignored `.local/`; they are never copied into the normal profile.
